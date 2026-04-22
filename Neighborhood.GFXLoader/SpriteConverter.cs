using System.Drawing;
using System.Drawing.Imaging;

namespace Neighborhood.GFXLoader;

/// <summary>
/// Built-in TGA -> standard format converter for Editor.exe.
///
/// Converts decoded sprites to PNG or BMP for use in external
/// graphics editors. Uses the same TgaDecoder pipeline as the
/// game renderer -- no separate code path.
///
/// Usage in Editor.exe:
///   var converter = new SpriteConverter(gfxLoader.Cache!);
///   converter.ExportToPng("idle_0001.tga", "C:/output/idle_0001.png");
///   var bmp = converter.ToBitmap("idle_0001.tga"); // for in-editor preview
/// </summary>
public sealed class SpriteConverter
{
    private readonly SpriteCache _cache;

    public SpriteConverter(SpriteCache cache)
    {
        _cache = cache;
    }

    // --- Single file export ---------------------------------------------------

    /// <summary>
    /// Exports a single sprite to PNG.
    /// PNG preserves alpha channel -- recommended for ARGB4444 sprites.
    /// </summary>
    public void ExportToPng(string spriteName, string outputPath)
    {
        var bitmap = GetOrThrow(spriteName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        bitmap.Save(outputPath, ImageFormat.Png);
    }

    /// <summary>
    /// Exports a single sprite to BMP.
    /// BMP loses alpha channel -- use for RGB565 GUI elements or when
    /// the target editor doesn't support PNG transparency.
    /// </summary>
    public void ExportToBmp(string spriteName, string outputPath)
    {
        var bitmap = GetOrThrow(spriteName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        bitmap.Save(outputPath, ImageFormat.Bmp);
    }

    // --- Batch export ---------------------------------------------------------

    /// <summary>
    /// Exports all available sprites to a folder, preserving subfolder structure.
    /// Reports progress via optional callback: (current, total, spriteName).
    /// </summary>
    public void ExportAll(
        string outputFolder,
        ExportFormat format = ExportFormat.Png,
        Action<int, int, string>? progress = null)
    {
        var sprites = _cache.AvailableSprites.ToList();
        var ext     = format == ExportFormat.Png ? ".png" : ".bmp";
        var imgFmt  = format == ExportFormat.Png ? ImageFormat.Png : ImageFormat.Bmp;

        for (int i = 0; i < sprites.Count; i++)
        {
            var name   = sprites[i];
            var outPath = Path.Combine(outputFolder, Path.ChangeExtension(name, ext));

            progress?.Invoke(i + 1, sprites.Count, name);

            try
            {
                var bitmap = _cache.Get(name);
                if (bitmap == null) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                bitmap.Save(outPath, imgFmt);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SpriteConverter] Failed to export '{name}': {ex.Message}");
            }
        }
    }

    // --- In-editor preview ----------------------------------------------------

    /// <summary>
    /// Returns a Bitmap suitable for display in a PictureBox or similar control.
    /// The returned Bitmap is owned by the cache -- do NOT dispose it.
    /// Returns null if sprite not found.
    /// </summary>
    public Bitmap? ToBitmap(string spriteName) =>
        _cache.Get(spriteName);

    /// <summary>
    /// Returns sprite info without decoding (fast metadata query).
    /// </summary>
    public SpriteInfo? GetInfo(string spriteName)
    {
        var decoded = _cache.GetDecoded(spriteName);
        if (decoded == null) return null;
        return new SpriteInfo(spriteName, decoded.Width, decoded.Height, decoded.HasAlpha);
    }

    /// <summary>All sprite names available for conversion.</summary>
    public IEnumerable<string> AvailableSprites => _cache.AvailableSprites;

    // --- Helpers -------------------------------------------------------------

    private Bitmap GetOrThrow(string spriteName) =>
        _cache.Get(spriteName)
        ?? throw new FileNotFoundException($"Sprite not found in cache: '{spriteName}'");
}

public enum ExportFormat { Png, Bmp }

/// <summary>Lightweight sprite metadata -- no pixel data loaded.</summary>
public record SpriteInfo(string Name, int Width, int Height, bool HasAlpha);
