using System.Xml.Linq;
using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Parsed objects.xml -- runtime representation used by AnimationSystem.
///
/// This is separate from the Shared models because it's a heavier
/// in-memory structure needed specifically for animation logic.
///
/// Contains:
///   - Actor definitions with speed and action tables
///   - Object definitions with action tables
///   - Icon definitions
///   - Inventory item image references
/// </summary>
public class ObjectsFile
{
    public IReadOnlyList<ActorObjectDef> Actors  { get; init; } = [];
    public IReadOnlyList<WorldObjectDef> Objects { get; init; } = [];

    public static ObjectsFile Empty => new();
}

public class ActorObjectDef
{
    public string Name    { get; init; } = string.Empty;
    public string Gfx     { get; init; } = string.Empty;
    public NfhPoint Hotspot { get; init; }
    public IReadOnlyList<SpeedDef>  Speeds  { get; init; } = [];
    public IReadOnlyList<ActionDef> Actions { get; init; } = [];
}

public class WorldObjectDef
{
    public string Name { get; init; } = string.Empty;
    public string Gfx  { get; init; } = string.Empty;
    public IReadOnlyList<ActionDef> Actions { get; init; } = [];
}

public class SpeedDef
{
    public string Name  { get; init; } = string.Empty;
    public int    Speed { get; init; }
    public int    Start { get; init; }
    public int    Noise { get; init; }
}

/// <summary>
/// Parses objects.xml into ObjectsFile.
///
/// NFH1 objects.xml has no root element -- wrapped before parsing.
/// NFH2 objects.xml has root &lt;objects level="..."&gt;.
/// </summary>
public static class ObjectsFileParser
{
    public static ObjectsFile Parse(VfsEntry entry)
    {
        var text = entry.ReadAsText();

        // NFH1: no root element -- wrap
        var trimmed = text.TrimStart();
        bool hasRoot = trimmed.StartsWith("<objects", StringComparison.OrdinalIgnoreCase);
        bool hasDecl = trimmed.StartsWith("<?");

        if (!hasRoot)
        {
            if (hasDecl)
            {
                var afterDecl = text.IndexOf("?>") + 2;
                text = text[..afterDecl] + "<objects>" + text[afterDecl..] + "</objects>";
            }
            else
            {
                text = "<objects>" + text + "</objects>";
            }
        }

        var doc  = XDocument.Parse(text);
        var root = doc.Root!;

        var actors = root.Elements("actor").Select(ParseActor).ToList();
        var objects = root.Elements("object").Select(ParseObject).ToList();

        return new ObjectsFile { Actors = actors, Objects = objects };
    }

    private static ActorObjectDef ParseActor(XElement el) => new()
    {
        Name    = el.Attribute("name")?.Value ?? string.Empty,
        Gfx     = el.Attribute("gfx")?.Value  ?? string.Empty,
        Hotspot = NfhPoint.Parse(el.Attribute("hotspot")?.Value),
        Speeds  = el.Elements("speed").Select(s => new SpeedDef
        {
            Name  = s.Attribute("name")?.Value ?? string.Empty,
            Speed = int.TryParse(s.Attribute("speed")?.Value, out var sp) ? sp : 0,
            Start = int.TryParse(s.Attribute("start")?.Value, out var st) ? st : 0,
            Noise = int.TryParse(s.Attribute("noise")?.Value, out var n)  ? n  : 0,
        }).ToList(),
        Actions = el.Elements("action").Select(ParseAction).ToList(),
    };

    private static WorldObjectDef ParseObject(XElement el) => new()
    {
        Name    = el.Attribute("name")?.Value ?? string.Empty,
        Gfx     = el.Attribute("gfx")?.Value  ?? string.Empty,
        Actions = el.Elements("action").Select(ParseAction).ToList(),
    };

    private static ActionDef ParseAction(XElement el)
    {
        var timeAttr = el.Attribute("time")?.Value ?? "0";
        int timeFrames = timeAttr.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? 0
            : (int.TryParse(timeAttr, out var t) ? t : 0);

        return new ActionDef
        {
            Name          = el.Attribute("name")?.Value         ?? string.Empty,
            ActorName     = el.Attribute("actor")?.Value        ?? string.Empty,
            ActorAnim     = el.Attribute("actoranim")?.Value    ?? "inv",
            ActorNextAnim = el.Attribute("actornextanim")?.Value ?? string.Empty,
            ObjAnim       = el.Attribute("objanim")?.Value      ?? string.Empty,
            ObjNextAnim   = el.Attribute("objnextanim")?.Value  ?? string.Empty,
            TimeFrames    = timeFrames,
            Noise         = int.TryParse(el.Attribute("noise")?.Value, out var n) ? n : 0,
        };
    }
}
