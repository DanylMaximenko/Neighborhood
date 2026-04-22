namespace Neighborhood.Shared.Models;

// -----------------------------------------------------------------------------
// anims.xml
// NFH1: root is <all_objects>, no level attribute.
// NFH2: root is <anims level="cn_b1">, UTF-16 LE.
// Both share the same object/animation/frame structure inside.
// -----------------------------------------------------------------------------

public class AnimsFile
{
    /// <summary>NFH2 only: level this file belongs to (anims level="...").</summary>
    public string? Level { get; init; }

    public IReadOnlyList<AnimObject> Objects { get; init; } = [];
}

public class AnimObject
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Click/interaction region for this object.</summary>
    public IReadOnlyList<AnimRegion> Regions { get; init; } = [];

    public IReadOnlyList<Animation> Animations { get; init; } = [];
}

public class AnimRegion
{
    public NfhPoint Position { get; init; }
    public NfhPoint Size { get; init; }

    /// <summary>Optional type hint (e.g. "text" for text display regions).</summary>
    public string? Type { get; init; }
}

public class Animation
{
    public string Name { get; init; } = string.Empty;

    /// <summary>"oneshot", "loop", etc.</summary>
    public string AnimType { get; init; } = string.Empty;

    /// <summary>NFH2 only: initial state (e.g. "done").</summary>
    public string? State { get; init; }

    public IReadOnlyList<AnimFrame> Frames { get; init; } = [];
}

public class AnimFrame
{
    /// <summary>Sprite filename (e.g. "ark_open.tga"). Relative to gfxdata.bnd root.</summary>
    public string Gfx { get; init; } = string.Empty;

    /// <summary>Optional sound to play on this frame (e.g. "sfx/obj_open1.wav").</summary>
    public string? Sfx { get; init; }
}

// -----------------------------------------------------------------------------
// gfxdata.xml
// NFH1: no root element (multiple <object> tags at top level).
// NFH2: root is <gfxfiles level="...">, UTF-16 LE.
// Maps object names to their sprite sheet files + offsets.
// -----------------------------------------------------------------------------

public class GfxDataFile
{
    /// <summary>NFH2 only: level attribute from root element.</summary>
    public string? Level { get; init; }

    public IReadOnlyList<GfxObject> Objects { get; init; } = [];
}

public class GfxObject
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<GfxFile> Files { get; init; } = [];
}

public class GfxFile
{
    /// <summary>Sprite filename to look up in gfxdata.bnd (e.g. "ark_open.tga").</summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>Pixel offset for rendering this sprite relative to the object's origin.</summary>
    public NfhPoint Offset { get; init; }
}

// -----------------------------------------------------------------------------
// sfxdata.xml
// Same structure in both variants: list of <sfx file="..." [volume="..."]/>
// -----------------------------------------------------------------------------

public class SfxDataFile
{
    public IReadOnlyList<SfxEntry> Entries { get; init; } = [];
}

public class SfxEntry
{
    /// <summary>Path to the audio file (e.g. "sfx/door_open1.wav").</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Volume 0-100. 100 = default (attribute absent in XML).</summary>
    public int Volume { get; init; } = 100;
}
