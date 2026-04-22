namespace Neighborhood.Shared.Exceptions;

/// <summary>
/// Thrown when a .bnd archive cannot be opened or is malformed.
/// </summary>
public class BndLoadException : Exception
{
    public string ArchivePath { get; }

    public BndLoadException(string archivePath, string message, Exception? inner = null)
        : base($"[BND] Failed to load '{archivePath}': {message}", inner)
    {
        ArchivePath = archivePath;
    }
}

/// <summary>
/// Thrown when an .xml layout file fails to deserialize.
/// </summary>
public class LayoutParseException : Exception
{
    public string EntryPath { get; }

    public LayoutParseException(string entryPath, string message, Exception? inner = null)
        : base($"[Layout] Failed to parse '{entryPath}': {message}", inner)
    {
        EntryPath = entryPath;
    }
}

/// <summary>
/// Thrown when a required asset (sprite, sound, etc.) is not found.
/// </summary>
public class AssetNotFoundException : Exception
{
    public string AssetName { get; }

    public AssetNotFoundException(string assetName)
        : base($"[Asset] Asset not found: '{assetName}'")
    {
        AssetName = assetName;
    }
}
