using System.Xml.Serialization;

namespace Neighborhood.Shared.Models;

/// <summary>
/// Deserialized from mod.xml in the root of a mod folder.
///
/// Mod folder structure:
///   Mods/
///   +-- my_mod/
///       +-- mod.xml
///       +-- level_bath/        <- only files the mod changes
///           +-- combine.xml
///           +-- tricks.xml
///
/// mod.xml example:
///   &lt;Mod&gt;
///     &lt;Id&gt;cool_pranks&lt;/Id&gt;
///     &lt;Name&gt;Cool Pranks Pack&lt;/Name&gt;
///     &lt;Author&gt;SomeAuthor&lt;/Author&gt;
///     &lt;Version&gt;1.0&lt;/Version&gt;
///     &lt;Description&gt;Adds 5 new pranks.&lt;/Description&gt;
///     &lt;Compatibility&gt;NFH1 NFH2&lt;/Compatibility&gt;
///   &lt;/Mod&gt;
/// </summary>
[XmlRoot("Mod")]
public class ModManifest
{
    [XmlElement("Id")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Author")]
    public string Author { get; set; } = string.Empty;

    [XmlElement("Version")]
    public string Version { get; set; } = "1.0";

    [XmlElement("Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Space-separated: "NFH1", "NFH2", or "NFH1 NFH2".</summary>
    [XmlElement("Compatibility")]
    public string CompatibilityRaw { get; set; } = "NFH1 NFH2";

    public IReadOnlyList<GameVariant> CompatibleWith =>
        CompatibilityRaw
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<GameVariant>(s, true, out var v) ? v : (GameVariant?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

    public bool IsCompatibleWith(GameVariant variant) =>
        CompatibleWith.Contains(variant);
}

/// <summary>
/// A discovered mod folder -- manifest + resolved folder path.
/// The VFS mounts files from FolderPath on top of the base variant.
/// </summary>
public class ModInfo
{
    public string FolderPath { get; init; } = string.Empty;
    public ModManifest Manifest { get; init; } = null!;

    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name)
        ? Path.GetFileName(FolderPath)
        : Manifest.Name;
}
