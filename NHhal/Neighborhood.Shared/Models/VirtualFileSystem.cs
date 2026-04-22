using System.IO.Compression;

namespace Neighborhood.Shared.Models;

/// <summary>
/// Virtual file system that mounts .bnd archives and mod folders under namespaced keys.
///
/// Physical layout:
///   Data/
///     NFH1/  gamedata.bnd  gfxdata.bnd  sfxdata.bnd  sfxdatahigh.bnd
///     NFH2/  gamedata.bnd  gfxdata.bnd  sfxdata.bnd  sfxdatahigh.bnd
///   Mods/
///     my_mod/               <- Format A: .bnd archives (original game mods, backward compat)
///       mod.xml
///       gamedata.bnd
///       gfxdata.bnd
///       sfxdata.bnd
///       sfxdatahigh.bnd
///
///     my_mod2/              <- Format B: loose files (new format)
///       mod.xml
///       level_bath/combine.xml
///       gfx/neighbor/idle.tga
///
///     my_mod3/              <- Format A+B combined -- both work simultaneously
///       mod.xml             <- .bnd archives mount first, loose files override on top
///       gamedata.bnd
///       level_bath/strings.xml
///
/// Virtual key format:
///   "{ns}:{internalPath}"          gamedata    e.g. "nfh1:level_bath/level.xml"
///   "{ns}:gfx:{internalPath}"      gfxdata     e.g. "nfh1:gfx:neighbor/idle.tga"
///   "{ns}:sfx:{internalPath}"      sfxdata     e.g. "nfh2:sfx:sfx/splash.wav"
///   "{ns}:sfxhi:{internalPath}"    sfxdatahigh e.g. "nfh1:sfxhi:music/theme.mp3"
///
/// Last-registered entry wins -- mods always mount after base data.
/// Within a mod folder: .bnd archives mount first, loose files on top.
/// </summary>
public class VirtualFileSystem
{
    private readonly Dictionary<string, VfsEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, VfsEntry> Entries => _entries;

    // --- Mount: base game .bnd archives --------------------------------------

    /// <summary>
    /// Mounts a .bnd ZIP file under the given namespace and role prefix.
    /// </summary>
    /// <param name="bndPath">Physical path to the .bnd file.</param>
    /// <param name="ns">"nfh1" or "nfh2"</param>
    /// <param name="role">"" (gamedata) | "gfx" | "sfx" | "sfxhi"</param>
    public void MountBnd(string bndPath, string ns, string role = "")
    {
        if (!File.Exists(bndPath))
            throw new FileNotFoundException($"BND archive not found: {bndPath}");

        using var zip = ZipFile.OpenRead(bndPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            Register(ns, role, entry.FullName.Replace('\\', '/'), ms.ToArray());
        }
    }

    // --- Mount: mod folder (both formats) ------------------------------------

    /// <summary>
    /// Mounts a mod folder supporting both Format A (.bnd archives) and
    /// Format B (loose files), or any combination of both.
    ///
    /// Mount order within the folder:
    ///   1. gamedata.bnd   -> ns:...
    ///   2. gfxdata.bnd    -> ns:gfx:...
    ///   3. sfxdata.bnd    -> ns:sfx:...
    ///   4. sfxdatahigh.bnd -> ns:sfxhi:...
    ///   5. Loose files    -> mapped by top-level subfolder convention
    ///      gfx/...        -> ns:gfx:...
    ///      sfx/...        -> ns:sfx:...
    ///      sfxhi/...      -> ns:sfxhi:...
    ///      anything else  -> ns:...  (gamedata namespace)
    ///
    /// mod.xml is always skipped (metadata only).
    /// </summary>
    public void MountMod(string modFolderPath, string ns)
    {
        if (!Directory.Exists(modFolderPath))
            throw new DirectoryNotFoundException($"Mod folder not found: {modFolderPath}");

        // Step 1 -- Format A: mount .bnd archives if present
        MountModBndIfExists(modFolderPath, ns, "gamedata.bnd",    "");
        MountModBndIfExists(modFolderPath, ns, "gfxdata.bnd",     "gfx");
        MountModBndIfExists(modFolderPath, ns, "sfxdata.bnd",     "sfx");
        MountModBndIfExists(modFolderPath, ns, "sfxdatahigh.bnd", "sfxhi");

        // Step 2 -- Format B: mount loose files on top (skipping .bnd and mod.xml)
        foreach (var filePath in Directory.EnumerateFiles(modFolderPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(modFolderPath, filePath)
                               .Replace('\\', '/');

            // Skip metadata and .bnd archives (already handled above)
            if (relative.Equals("mod.xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.EndsWith(".bnd", StringComparison.OrdinalIgnoreCase)) continue;

            // Determine role from top-level subfolder
            string role = "";
            string internalPath = relative;

            var slash = relative.IndexOf('/');
            if (slash > 0)
            {
                var topFolder = relative[..slash];
                if (topFolder is "gfx" or "sfx" or "sfxhi")
                {
                    role = topFolder;
                    internalPath = relative[(slash + 1)..];
                }
            }

            Register(ns, role, internalPath, File.ReadAllBytes(filePath));
        }
    }

    // --- Lookup ---------------------------------------------------------------

    public bool TryGet(string vfsKey, out VfsEntry entry) =>
        _entries.TryGetValue(vfsKey, out entry!);

    public VfsEntry Get(string vfsKey) =>
        _entries.TryGetValue(vfsKey, out var e)
            ? e
            : throw new KeyNotFoundException($"VFS entry not found: '{vfsKey}'");

    /// <summary>All keys starting with the given prefix.</summary>
    public IEnumerable<string> ListKeys(string prefix) =>
        _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>All XML entries for a given variant namespace (excludes gfx/sfx roles).</summary>
    public IEnumerable<VfsEntry> ListXml(string ns) =>
        _entries.Values.Where(e =>
            e.VfsKey.StartsWith($"{ns}:", StringComparison.OrdinalIgnoreCase) &&
            !e.VfsKey.StartsWith($"{ns}:gfx:", StringComparison.OrdinalIgnoreCase) &&
            !e.VfsKey.StartsWith($"{ns}:sfx:", StringComparison.OrdinalIgnoreCase) &&
            !e.VfsKey.StartsWith($"{ns}:sfxhi:", StringComparison.OrdinalIgnoreCase) &&
            e.Extension == ".xml");

    // --- Private helpers -----------------------------------------------------

    private void MountModBndIfExists(string modFolder, string ns, string bndName, string role)
    {
        var path = Path.Combine(modFolder, bndName);
        if (!File.Exists(path)) return;

        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Register(ns, role, entry.FullName.Replace('\\', '/'), ms.ToArray());
        }
    }

    private void Register(string ns, string role, string internalPath, byte[] data)
    {
        var key = string.IsNullOrEmpty(role)
            ? $"{ns}:{internalPath}"
            : $"{ns}:{role}:{internalPath}";

        _entries[key] = new VfsEntry(key, internalPath, data);
    }
}

/// <summary>A single file entry in the VFS.</summary>
public sealed class VfsEntry
{
    public string VfsKey       { get; }
    public string InternalPath { get; }
    public byte[] Data         { get; }

    public VfsEntry(string vfsKey, string internalPath, byte[] data)
    {
        VfsKey       = vfsKey;
        InternalPath = internalPath;
        Data         = data;
    }

    public string FileName  => Path.GetFileName(InternalPath);
    public string Extension => Path.GetExtension(InternalPath).ToLowerInvariant();

    /// <summary>
    /// Reads as string, auto-detecting UTF-16 LE BOM (FF FE).
    /// Falls back to UTF-8. Handles both NFH1 (mixed) and NFH2 (mostly UTF-16).
    /// </summary>
    public string ReadAsText()
    {
        if (Data.Length >= 2 && Data[0] == 0xFF && Data[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(Data, 2, Data.Length - 2);
        return System.Text.Encoding.UTF8.GetString(Data);
    }

    public ReadOnlySpan<byte> AsSpan() => Data;
}
