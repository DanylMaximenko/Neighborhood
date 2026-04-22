using System.Xml;
using System.Xml.Linq;
using Neighborhood.Shared.Exceptions;
using Neighborhood.Shared.Models;

namespace Neighborhood.Loader;

/// <summary>
/// Parses XML entries from the VirtualFileSystem into typed model objects.
///
/// Key design decisions based on real file analysis:
///
///   Encoding:
///     - Most NFH2 XMLs and some NFH1 XMLs are UTF-16 LE (BOM FF FE).
///     - VfsEntry.ReadAsText() handles detection automatically.
///
///   combine.xml (NFH1):
///     - No root element -- multiple &lt;combination&gt; tags at top level.
///     - We wrap in &lt;combines&gt; before parsing.
///
///   gfxdata.xml (NFH1):
///     - No root element -- multiple &lt;object&gt; tags at top level.
///     - We wrap in &lt;gfxfiles&gt; before parsing.
///
///   NFH2 roots carry a level attribute:
///     &lt;combines level="china_beach1"&gt;, &lt;gfxfiles level="generic"&gt;, etc.
/// </summary>
public class BndLoader
{
    // --- leveldata.xml -------------------------------------------------------

    public LevelData ParseLevelData(VfsEntry entry, GameVariant variant)
    {
        var xml = LoadXml(entry);
        var root = xml.Root!;

        var sets = root.Elements("set").Select(set => new LevelSet
        {
            Name     = Attr(set, "name"),
            NextSet  = set.Attribute("nextset")?.Value,
            State    = Attr(set, "state"),
            MinCoins = AttrInt(set, "mincoins"),
            Levels   = set.Elements("level").Select(lv => new LevelEntry
            {
                Name        = Attr(lv, "name"),
                State       = Attr(lv, "state"),
                Reachable   = AttrInt(lv, "reachable"),
                MinProgress = AttrInt(lv, variant == GameVariant.NFH2 ? "mincoins" : "minquota"),
                Time        = AttrInt(lv, "time"),
                GuiPath     = lv.Attribute("gui")?.Value,
                MusicPath   = lv.Attribute("music")?.Value,
            }).ToList()
        }).ToList();

        return new LevelData
        {
            Variant     = variant,
            RespawnTime = AttrInt(root, "respawntime"),
            Sets        = sets
        };
    }

    // --- level.xml -----------------------------------------------------------

    public LevelLayout ParseLevelLayout(VfsEntry entry)
    {
        var xml = LoadXml(entry);
        var root = xml.Root!;

        return new LevelLayout
        {
            Name      = Attr(root, "name"),
            Size      = NfhPoint.Parse(root.Attribute("size")?.Value),
            AngryTime = AttrInt(root, "angrytime"),
            Objects   = root.Elements("object").Select(ParseLevelObject).ToList(),
            Rooms     = root.Elements("room").Select(ParseRoom).ToList()
        };
    }

    private static Room ParseRoom(XElement el) => new()
    {
        Name      = Attr(el, "name"),
        Offset    = NfhPoint.Parse(el.Attribute("offset")?.Value),
        Path1     = NfhPoint.Parse(el.Attribute("path1")?.Value),
        Path2     = NfhPoint.Parse(el.Attribute("path2")?.Value),
        Floors    = el.Elements("floor").Select(f => new Floor
        {
            Offset  = NfhPoint.Parse(f.Attribute("offset")?.Value),
            Size    = NfhPoint.Parse(f.Attribute("size")?.Value),
            IsWall  = f.Attribute("wall")?.Value == "true",
            Hotspot = NfhPoint.Parse(f.Attribute("hotspot")?.Value),
        }).ToList(),
        Objects   = el.Elements("object").Select(ParseLevelObject).ToList(),
        Doors     = el.Elements("door").Select(d => new LevelDoor
        {
            Name     = Attr(d, "name"),
            Layer    = AttrInt(d, "layer"),
            Position = NfhPoint.Parse(d.Attribute("position")?.Value),
            Visible  = d.Attribute("visible")?.Value != "false",
        }).ToList(),
        Actors    = el.Elements("actor").Select(a => new LevelActor
        {
            Name      = Attr(a, "name"),
            Layer     = AttrInt(a, "layer"),
            Position  = NfhPoint.Parse(a.Attribute("position")?.Value),
            Visible   = a.Attribute("visible")?.Value != "false",
            Animation = a.Attribute("animation")?.Value,
            Focussed  = a.Attribute("focussed")?.Value == "true",
        }).ToList(),
        Neighbors = el.Elements("neighbor").Select(n => new RoomNeighbor
        {
            RoomName = Attr(n, "name"),
            Costs    = AttrInt(n, "costs"),
            DoorIn   = Attr(n, "doorin"),
            DoorOut  = Attr(n, "doorout"),
        }).ToList()
    };

    private static LevelObject ParseLevelObject(XElement el) => new()
    {
        Name     = Attr(el, "name"),
        Layer    = AttrInt(el, "layer"),
        Position = NfhPoint.Parse(el.Attribute("position")?.Value),
        Visible  = el.Attribute("visible")?.Value != "false",
    };

    // --- combine.xml ---------------------------------------------------------

    public CombineFile ParseCombines(VfsEntry entry)
    {
        var text = entry.ReadAsText();

        // NFH1: no root element -- wrap so XDocument can parse it
        if (!text.TrimStart().StartsWith("<combines", StringComparison.OrdinalIgnoreCase)
            && !text.TrimStart().StartsWith("<?"))
        {
            text = $"<combines>{text}</combines>";
        }
        else if (text.TrimStart().StartsWith("<?"))
        {
            // Has XML declaration but root might not be <combines>
            var afterDecl = text.IndexOf("?>") + 2;
            var rest = text[afterDecl..].TrimStart();
            if (!rest.StartsWith("<combines", StringComparison.OrdinalIgnoreCase))
                text = text[..(afterDecl)] + "<combines>" + rest + "</combines>";
        }

        var xml = XDocument.Parse(text);
        var root = xml.Root!;

        return new CombineFile
        {
            Level = root.Attribute("level")?.Value,
            Combinations = root.Elements("combination").Select(c => new Combination
            {
                ResultName        = Attr(c, "name"),
                IsTrick           = c.Attribute("trick")?.Value == "true",
                IsWrong           = c.Attribute("wrong")?.Value == "true",
                MiniGamePath      = c.Attribute("game")?.Value,
                MiniGameStartLevel = AttrInt(c, "startlevel"),
                MiniGameEndLevel  = AttrInt(c, "endlevel"),
                Layer             = c.Attribute("layer") != null ? AttrInt(c, "layer") : -1,
                ResultAnim        = c.Attribute("anim")?.Value,
                Ingredients       = c.Elements("ingredient").Select(i => new Ingredient
                {
                    Name   = Attr(i, "name"),
                    Remove = i.Attribute("remove")?.Value == "true",
                    Layer  = i.Attribute("layer") != null ? AttrInt(i, "layer") : -1,
                }).ToList()
            }).ToList()
        };
    }

    // --- tricks.xml ----------------------------------------------------------

    public TricksFile ParseTricks(VfsEntry entry, GameVariant variant)
    {
        var xml = LoadXml(entry);

        return new TricksFile
        {
            Tricks = xml.Root!.Elements("trick").Select(t => new Trick
            {
                Name      = Attr(t, "name"),
                // NFH1
                Quotas    = variant == GameVariant.NFH1
                    ? new[] { "quota1", "quota2", "quota3", "quota4" }
                        .Select(q => AttrInt(t, q)).ToList()
                    : [],
                AngryTime = variant == GameVariant.NFH1 ? AttrInt(t, "angrytime") : 0,
                // NFH2
                Coins     = variant == GameVariant.NFH2 ? AttrInt(t, "coins") : 0,
                Rage      = variant == GameVariant.NFH2 ? AttrInt(t, "rage")  : 0,
            }).ToList()
        };
    }

    // --- trigger.xml ---------------------------------------------------------

    public TriggersFile ParseTriggers(VfsEntry entry)
    {
        var xml = LoadXml(entry);

        return new TriggersFile
        {
            Actors = xml.Root!.Elements("actor").Select(a => new ActorTriggerGroup
            {
                ActorName = Attr(a, "name"),
                Behaviors = a.Elements("behavior").Select(b => new BehaviorTrigger
                {
                    BehaviorName = Attr(b, "name"),
                    Conditions   = b.Elements("trigger").Select(t => new TriggerCondition
                    {
                        ObjectName = t.Attribute("object")?.Value,
                        Noise      = AttrInt(t, "noise"),
                        Position   = Attr(t, "position"),
                        Type       = Attr(t, "type"),
                        Actor      = t.Attribute("actor")?.Value,
                    }).ToList()
                }).ToList()
            }).ToList()
        };
    }

    // --- wrongstrings.xml (NFH2) ---------------------------------------------

    public WrongStringsFile ParseWrongStrings(VfsEntry entry)
    {
        var xml = LoadXml(entry);
        var dict = xml.Root!.Elements("string")
            .Where(s => s.Attribute("category")?.Value == "wrong")
            .ToDictionary(
                s => Attr(s, "name"),
                s => s.Attribute("text")?.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return new WrongStringsFile { Entries = dict };
    }

    // --- anims.xml -----------------------------------------------------------

    public AnimsFile ParseAnims(VfsEntry entry)
    {
        var xml = LoadXml(entry);
        var root = xml.Root!;

        // NFH1 root: <all_objects>   NFH2 root: <anims level="...">
        return new AnimsFile
        {
            Level   = root.Attribute("level")?.Value,
            Objects = root.Elements("object").Select(o => new AnimObject
            {
                Name       = Attr(o, "name"),
                Regions    = o.Elements("region").Select(r => new AnimRegion
                {
                    Position = NfhPoint.Parse(r.Attribute("position")?.Value),
                    Size     = NfhPoint.Parse(r.Attribute("size")?.Value),
                    Type     = r.Attribute("type")?.Value,
                }).ToList(),
                Animations = o.Elements("animation").Select(a => new Animation
                {
                    Name     = Attr(a, "name"),
                    AnimType = Attr(a, "type"),
                    State    = a.Attribute("state")?.Value,
                    Frames   = a.Elements("frame").Select(f => new AnimFrame
                    {
                        Gfx = Attr(f, "gfx"),
                        Sfx = f.Attribute("sfx")?.Value,
                    }).ToList()
                }).ToList()
            }).ToList()
        };
    }

    // --- gfxdata.xml ---------------------------------------------------------

    public GfxDataFile ParseGfxData(VfsEntry entry)
    {
        var text = entry.ReadAsText();

        // NFH1: no root element -- multiple <object> at top level
        var trimmed = text.TrimStart();
        bool hasDecl = trimmed.StartsWith("<?");
        bool hasRoot = trimmed.Contains("<gfxfiles") || trimmed.Contains("<objects");

        if (!hasRoot)
        {
            if (hasDecl)
            {
                var afterDecl = text.IndexOf("?>") + 2;
                text = text[..afterDecl] + "<gfxfiles>" + text[afterDecl..] + "</gfxfiles>";
            }
            else
            {
                text = "<gfxfiles>" + text + "</gfxfiles>";
            }
        }

        var xml = XDocument.Parse(text);
        var root = xml.Root!;

        return new GfxDataFile
        {
            Level   = root.Attribute("level")?.Value,
            Objects = root.Elements("object").Select(o => new GfxObject
            {
                Name  = Attr(o, "name"),
                Files = o.Element("gfxdata")?.Elements("file").Select(f => new GfxFile
                {
                    Image  = Attr(f, "image"),
                    Offset = NfhPoint.Parse(f.Attribute("offset")?.Value),
                }).ToList() ?? []
            }).ToList()
        };
    }

    // --- sfxdata.xml ---------------------------------------------------------

    public SfxDataFile ParseSfxData(VfsEntry entry)
    {
        var xml = LoadXml(entry);
        return new SfxDataFile
        {
            Entries = xml.Root!.Elements("sfx").Select(s => new SfxEntry
            {
                File   = Attr(s, "file"),
                Volume = s.Attribute("volume") != null ? AttrInt(s, "volume") : 100,
            }).ToList()
        };
    }

    // --- minigame/*.xml (NFH2) -----------------------------------------------

    public MiniGameDef ParseMiniGame(VfsEntry entry)
    {
        var xml = LoadXml(entry);
        var root = xml.Root!;
        var pb   = root.Element("progressbar");

        return new MiniGameDef
        {
            BackgroundFile      = root.Element("background")?.Attribute("file")?.Value ?? "",
            AlarmFile           = root.Element("alarm")?.Attribute("file")?.Value ?? "",
            ThumbFile           = root.Element("thumb")?.Attribute("file")?.Value ?? "",
            IconFile            = root.Element("icon")?.Attribute("file")?.Value ?? "",
            ProgressBarVertical = pb?.Attribute("vertical")?.Value == "true",
            ProgressBarFront    = pb?.Attribute("front")?.Value ?? "",
            ProgressBarOffset   = NfhPoint.Parse(pb?.Attribute("offset")?.Value),
        };
    }

    // --- route.xml -----------------------------------------------------------

    public RouteData ParseRoute(VfsEntry entry) =>
        RouteData.Parse(entry.ReadAsText());

    // --- Shared helpers ------------------------------------------------------

    private static XDocument LoadXml(VfsEntry entry)
    {
        try
        {
            return XDocument.Parse(entry.ReadAsText());
        }
        catch (XmlException ex)
        {
            throw new LayoutParseException(entry.InternalPath, ex.Message, ex);
        }
    }

    private static string Attr(XElement el, string name) =>
        el.Attribute(name)?.Value ?? string.Empty;

    private static int AttrInt(XElement el, string name) =>
        int.TryParse(el.Attribute(name)?.Value, out var v) ? v : 0;
}

    // Note: ObjectsFile parsing is handled by ObjectsFileParser in Neighborhood.NFH1
    // since it requires the runtime ActionDef types, not the shared XML models.
