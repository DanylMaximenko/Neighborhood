using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Neighborhood.Shared.Models;

namespace Neighborhood.GFXLoader;

/// <summary>
/// Decodes .tga sprites from the VFS on demand and caches the resulting
/// GDI+ Bitmaps in memory.
///
/// Key design decisions:
///   * Lazy decoding -- sprite is decoded only on first access.
///   * Weak references for large sprites so GC can reclaim memory under pressure.
///     Small sprites (<= 128x128) are kept as strong references (hot cache).
///   * Thread-safe reads via ReaderWriterLockSlim.
///   * Bitmaps are created in Format32bppArgb which matches our BGRA output.
///
/// Usage:
///   var bitmap = cache.Get("idle_0001.tga");   // decoded + cached
///   var bitmap = cache.Get("idle_0001.tga");   // served from cache
/// </summary>
public sealed class SpriteCache : IDisposable
{
    private const int SmallSpriteThreshold = 128; // pixels -- strong ref below this

    /// <summary>
    /// Default index: fileName -> entry (last-mounted wins for same name).
    /// Used as fallback when no level priority is set or sprite not found in priority folder.
    /// </summary>
    private readonly Dictionary<string, VfsEntry> _entries;

    /// <summary>
    /// Full-path index: internalPath -> entry.
    /// e.g. "topleft/buffet/ms_0000.tga" -> VfsEntry
    /// Used by GetByPath() for precise lookup.
    /// </summary>
    private readonly Dictionary<string, VfsEntry> _entriesByPath;

    /// <summary>
    /// Per-folder index: folderName -> (fileName -> entry).
    /// E.g. "ship1" -> {"ms_0000.tga" -> ship1/neighbor/ms_0000.tga entry}.
    /// Populated from InternalPath folder structure.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, VfsEntry>> _byFolder =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Active level folder prefix for priority lookup (e.g. "ship1", "cn_b1").
    /// When set, Get() checks this folder's sprites before falling back to _entries.
    /// </summary>
    private string? _priorityFolder;

    private readonly Dictionary<string, Bitmap>       _strongCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WeakReference<Bitmap>> _weakCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public SpriteCache(IReadOnlyDictionary<string, VfsEntry> entries)
    {
        // Build default index by file name (last-mounted wins for duplicate names).
        // Also build per-folder index for level-priority lookup.
        _entries = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase);
        _entriesByPath = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.Values)
        {
            if (!entry.Extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
                continue;

            _entries[entry.FileName] = entry;

            // Full-path index (normalized with forward slashes)
            _entriesByPath[entry.InternalPath.Replace('/', '/')] = entry;
            _entriesByPath[entry.InternalPath] = entry;

            // Index by top-level folder of InternalPath (e.g. "ship1" from "ship1/neighbor/ms_0000.tga")
            var sepIdx = entry.InternalPath.IndexOfAny(new[] { '/', System.IO.Path.DirectorySeparatorChar });
            if (sepIdx > 0)
            {
                var folder = entry.InternalPath.Substring(0, sepIdx);
                if (!_byFolder.TryGetValue(folder, out var folderMap))
                {
                    folderMap = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase);
                    _byFolder[folder] = folderMap;
                }
                // Don't override within same folder (first encountered wins per folder)
                folderMap.TryAdd(entry.FileName, entry);
            }
        }
    }

    /// <summary>
    /// Sets the active level folder for priority sprite lookup.
    /// Sprites from this folder will be preferred over same-named sprites from other levels.
    /// Call when a level is loaded (e.g. SetLevelPriority("ship1")).
    /// Pass null to disable priority and use default last-mounted behaviour.
    /// </summary>
    public void SetLevelPriority(string? levelFolder)
    {
        _priorityFolder = levelFolder;
        // Clear bitmap cache since sprites may now resolve differently
        Clear();
    }

    /// <summary>
    /// Returns a decoded Bitmap for the given sprite filename.
    /// If a level priority folder is set, sprites from that folder are preferred.
    /// Returns null if the sprite is not found.
    /// </summary>
    public Bitmap? Get(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // -- Fast path: check cache --------------------------------------------
        _lock.EnterReadLock();
        try
        {
            if (_strongCache.TryGetValue(fileName, out var strong))
                return strong;

            if (_weakCache.TryGetValue(fileName, out var weak) &&
                weak.TryGetTarget(out var cached))
                return cached;
        }
        finally { _lock.ExitReadLock(); }

        // -- Resolve entry: priority folder first, then default ----------------
        VfsEntry? entry = null;
        if (_priorityFolder != null &&
            _byFolder.TryGetValue(_priorityFolder, out var folderMap))
            folderMap.TryGetValue(fileName, out entry);

        if (entry == null && !_entries.TryGetValue(fileName, out entry))
            return null;

        var sprite  = TgaDecoder.Decode(entry.Data, fileName);
        var bitmap  = CreateBitmap(sprite);
        bool isSmall = sprite.Width <= SmallSpriteThreshold &&
                       sprite.Height <= SmallSpriteThreshold;

        _lock.EnterWriteLock();
        try
        {
            if (isSmall)
                _strongCache[fileName] = bitmap;
            else
                _weakCache[fileName] = new WeakReference<Bitmap>(bitmap);
        }
        finally { _lock.ExitWriteLock(); }

        return bitmap;
    }

    /// <summary>
    /// Returns decoded sprite metadata without creating a Bitmap.
    /// Useful for Editor.exe size queries and export.
    /// </summary>
    public DecodedSprite? GetDecoded(string fileName)
    {
        VfsEntry? entry = null;
        if (_priorityFolder != null &&
            _byFolder.TryGetValue(_priorityFolder, out var folderMap))
            folderMap.TryGetValue(fileName, out entry);

        if (entry == null && !_entries.TryGetValue(fileName, out entry))
            return null;

        return TgaDecoder.Decode(entry.Data, fileName);
    }

    /// <summary>All sprite filenames available in this cache.</summary>
    public IEnumerable<string> AvailableSprites => _entries.Keys;

    /// <summary>
    /// Looks up a sprite by its full internal path (e.g. "topleft/buffet/ms_0000.tga").
    /// This is the correct lookup for NFH2 where objects share sprite filenames
    /// across different sub-folders but are uniquely identified by their full path.
    /// Returns null if not found.
    /// </summary>
    public Bitmap? GetByPath(string fullPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Normalize separators
        fullPath = fullPath.Replace('\\', '/');

        _lock.EnterReadLock();
        try
        {
            if (_strongCache.TryGetValue(fullPath, out var strong)) return strong;
            if (_weakCache.TryGetValue(fullPath, out var weak) &&
                weak.TryGetTarget(out var cached)) return cached;
        }
        finally { _lock.ExitReadLock(); }

        if (!_entriesByPath.TryGetValue(fullPath, out var entry))
            return null;

        var sprite = TgaDecoder.Decode(entry.Data, fullPath);
        var bitmap = CreateBitmap(sprite);
        bool isSmall = sprite.Width <= SmallSpriteThreshold && sprite.Height <= SmallSpriteThreshold;

        _lock.EnterWriteLock();
        try
        {
            if (isSmall) _strongCache[fullPath] = bitmap;
            else _weakCache[fullPath] = new WeakReference<Bitmap>(bitmap);
        }
        finally { _lock.ExitWriteLock(); }

        return bitmap;
    }

    /// <summary>Number of sprites currently held in strong cache.</summary>
    public int StrongCacheCount
    {
        get { _lock.EnterReadLock(); try { return _strongCache.Count; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>Removes all cached bitmaps (entries remain available for re-decode).</summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var bmp in _strongCache.Values) bmp.Dispose();
            _strongCache.Clear();
            _weakCache.Clear();
        }
        finally { _lock.ExitWriteLock(); }
    }

    // --- Bitmap creation -----------------------------------------------------

    /// <summary>
    /// Creates a GDI+ Bitmap from decoded ARGB32 data using LockBits for
    /// zero-copy memory transfer (no per-pixel SetPixel overhead).
    /// </summary>
    private static Bitmap CreateBitmap(DecodedSprite sprite)
    {
        var bitmap = new Bitmap(sprite.Width, sprite.Height, PixelFormat.Format32bppArgb);
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, sprite.Width, sprite.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = sprite.Width * 4;

            // If GDI+ stride matches our stride, copy in one call
            if (bmpData.Stride == stride)
            {
                Marshal.Copy(sprite.Argb32Data, 0, bmpData.Scan0, sprite.Argb32Data.Length);
            }
            else
            {
                // Stride may be padded -- copy row by row
                for (int y = 0; y < sprite.Height; y++)
                {
                    var srcOffset = y * stride;
                    var dstPtr    = bmpData.Scan0 + y * bmpData.Stride;
                    Marshal.Copy(sprite.Argb32Data, srcOffset, dstPtr, stride);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        return bitmap;
    }

    // --- IDisposable ---------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        _lock.Dispose();
    }
}
