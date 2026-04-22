using System.Drawing;
using Neighborhood.Shared.Interfaces;
using Neighborhood.Shared.Models;

namespace Neighborhood.GFXLoader;

/// <summary>
/// Implements IGfxLoader using SpriteCache for lazy decoded bitmap access.
/// Exposes the SpriteCache publicly so Editor.exe can use it for preview/export.
/// </summary>
public sealed class GfxLoader : IGfxLoader, IDisposable
{
    private SpriteCache? _cache;

    /// <summary>
    /// Direct access to the sprite cache -- used by Editor.exe for
    /// preview rendering and the built-in TGA->PNG/BMP converter.
    /// Null until LoadFromVfs is called.
    /// </summary>
    public SpriteCache? Cache => _cache;

    public void LoadFromVfs(VirtualFileSystem vfs, string ns)
    {
        _cache?.Dispose();

        // Collect all GFX entries for this namespace
        var gfxEntries = vfs.Entries
            .Where(kv => kv.Key.StartsWith($"{ns}:gfx:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var duplicateNames = gfxEntries.Values
            .Where(e => e.Extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (duplicateNames.Count > 0)
        {
            // Duplicate sprite names are expected -- sprites are indexed by filename
            // only (not full path), so objects in different folders with identical
            // filenames share the name. Later mounts (more specific) override earlier.
            Console.WriteLine(
                $"[GFX] {ns}: {duplicateNames.Count} shared sprite names (normal -- indexed by filename).");
        }

        _cache = new SpriteCache(gfxEntries);
        Console.WriteLine($"[GFX] Indexed namespace '{ns}': entries={gfxEntries.Count}, uniqueSprites={_cache.AvailableSprites.Count()}");
    }

    /// <inheritdoc/>
    public bool TryGetSprite(string spriteName, out byte[] spriteData)
    {
        spriteData = [];
        if (_cache == null) return false;

        var decoded = _cache.GetDecoded(spriteName);
        if (decoded == null) return false;

        spriteData = decoded.Argb32Data;
        return true;
    }

    /// <summary>
    /// Returns a GDI+ Bitmap for the given sprite filename.
    /// Bitmap is cached -- do NOT dispose it (owned by SpriteCache).
    /// </summary>
    public Bitmap? GetBitmap(string spriteName) =>
        _cache?.Get(spriteName);

    /// <summary>
    /// Returns a Bitmap using owner-qualified lookup with multiple fallback strategies:
    ///
    /// 1. ownerName/spriteName           (world objects: "topleft/buffet/ms_0000.tga")
    /// 2. ownerName/stripped_spriteName  (actors: "neighbor"+"n_ms0_0000.tga"
    ///                                    -> "neighbor/ms0_0000.tga")
    ///    This handles the NFH1/NFH2 naming convention where actor sprite files
    ///    use a short prefix (n_, w_, mo_, ol_) in anims.xml but are stored
    ///    in gfxdata.bnd under the actor folder without that prefix.
    /// 3. spriteName only (filename-only fallback)
    /// </summary>
    public Bitmap? GetBitmap(string ownerName, string spriteName)
    {
        if (_cache == null) return null;

        var owner = ownerName.TrimEnd('/');

        // Strategy 1: exact full path (world objects like "topleft/buffet/ms_0000.tga")
        var fullPath = owner + "/" + spriteName;
        var bmp = _cache.GetByPath(fullPath);
        if (bmp != null) return bmp;

        // Strategy 2: strip actor prefix before first underscore
        // "n_ms0_0000.tga" -> "ms0_0000.tga" -> "neighbor/ms0_0000.tga"
        var us = spriteName.IndexOf('_');
        if (us > 0 && us < 4)  // short prefix: n_, w_, mo_, ol_, fi_, ki_, t_
        {
            var stripped = spriteName.Substring(us + 1);
            bmp = _cache.GetByPath(owner + "/" + stripped);
            if (bmp != null) return bmp;
        }

        // Strategy 3: filename-only fallback
        return _cache.Get(spriteName);
    }

    /// <summary>
    /// Sets the active level folder so that sprites from that level are
    /// preferred when multiple levels share the same sprite filename.
    /// Call once after loading a level (e.g. SetLevelPriority("ship1")).
    /// Pass null to revert to default last-mounted behaviour.
    /// </summary>
    public void SetLevelPriority(string? levelFolder) =>
        _cache?.SetLevelPriority(levelFolder);

    public void Dispose() => _cache?.Dispose();
}
