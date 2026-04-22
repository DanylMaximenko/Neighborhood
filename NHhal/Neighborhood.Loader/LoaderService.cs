using Neighborhood.Shared.Interfaces;
using Neighborhood.Shared.Models;

namespace Neighborhood.Loader;

/// <summary>
/// Orchestrates the full loading pipeline.
///
/// Archive layout on disk:
///   Data/
///     routes.bnd          <- shared, contains all route.xml files for both NFH1 and NFH2
///     NFH1/  gamedata.bnd  gfxdata.bnd  sfxdata.bnd  sfxdatahigh.bnd
///     NFH2/  gamedata.bnd  gfxdata.bnd  sfxdata.bnd  sfxdatahigh.bnd
///   Mods/
///     my_mod/  mod.xml  [optional .bnd overrides or loose files]
///
/// VFS namespaces:
///   "nfh1:{path}"      gamedata entries for NFH1
///   "nfh2:{path}"      gamedata entries for NFH2
///   "routes:{path}"    route.xml entries (shared, e.g. "routes:level_bath/route.xml")
///   "nfh1:gfx:{path}"  gfxdata entries, etc.
///
/// NFH1 briefing paths differ from NFH2:
///   NFH1: "nfh1:dialogs/briefing/{levelName}.xml"
///   NFH2: "nfh2:{levelName}/briefing.xml"
/// </summary>
public class LoaderService
{
    private readonly BndLoader   _parser;
    private readonly IGfxLoader  _gfxLoader;
    private readonly ISfxLoader  _sfxLoader;

    public LoaderService(BndLoader parser, IGfxLoader gfxLoader, ISfxLoader sfxLoader)
    {
        _parser    = parser;
        _gfxLoader = gfxLoader;
        _sfxLoader = sfxLoader;
    }

    // --- Public state after Load() --------------------------------------------

    public VirtualFileSystem Vfs { get; } = new();
    public GameVariant ActiveVariant { get; private set; }
    public LevelData LevelData { get; private set; } = null!;

    public IReadOnlyDictionary<string, LevelLayout>  Layouts  { get; private set; } = new Dictionary<string, LevelLayout>();
    public IReadOnlyDictionary<string, CombineFile>  Combines { get; private set; } = new Dictionary<string, CombineFile>();
    public IReadOnlyDictionary<string, TricksFile>   Tricks   { get; private set; } = new Dictionary<string, TricksFile>();
    public IReadOnlyDictionary<string, TriggersFile> Triggers { get; private set; } = new Dictionary<string, TriggersFile>();

    /// <summary>
    /// Patrol routes keyed by level folder name.
    /// Loaded from Data/routes.bnd (shared archive, not variant-specific).
    /// Empty RouteData if route.xml absent -> GameLogic generates random route.
    /// </summary>
    public IReadOnlyDictionary<string, RouteData> Routes { get; private set; } = new Dictionary<string, RouteData>();

    public ModInfo? ActiveMod { get; private set; }

    // --- Load -----------------------------------------------------------------

    public void Load(string dataPath, GameVariant variant, ModInfo? mod = null)
    {
        ActiveVariant = variant;
        ActiveMod     = mod;

        // 1. Mount both variants
        MountVariant(dataPath, "nfh1");
        MountVariant(dataPath, "nfh2");

        // 2. Mount shared routes.bnd (if present -- not required)
        var routesBndPath = Path.Combine(dataPath, "routes.bnd");
        if (File.Exists(routesBndPath))
            Vfs.MountBnd(routesBndPath, "routes");

        // 3. Apply mod on top of active variant's namespace
        if (mod != null)
            Vfs.MountMod(mod.FolderPath, variant == GameVariant.NFH1 ? "nfh1" : "nfh2");

        var ns = variant == GameVariant.NFH1 ? "nfh1" : "nfh2";

        // 4. Parse leveldata.xml
        LevelData = _parser.ParseLevelData(Vfs.Get($"{ns}:leveldata.xml"), variant);

        // 5. Parse per-level XML files
        var layouts  = new Dictionary<string, LevelLayout>(StringComparer.OrdinalIgnoreCase);
        var combines = new Dictionary<string, CombineFile>(StringComparer.OrdinalIgnoreCase);
        var tricks   = new Dictionary<string, TricksFile>(StringComparer.OrdinalIgnoreCase);
        var triggers = new Dictionary<string, TriggersFile>(StringComparer.OrdinalIgnoreCase);
        var routes   = new Dictionary<string, RouteData>(StringComparer.OrdinalIgnoreCase);

        var levelNames = LevelData.Sets
            .SelectMany(s => s.Levels)
            .Select(l => l.Name)
            .Append("generic")
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var levelName in levelNames)
        {
            TryParse(ns, levelName, "level.xml",
                e => layouts[levelName]  = _parser.ParseLevelLayout(e));

            TryParse(ns, levelName, "combine.xml",
                e => combines[levelName] = _parser.ParseCombines(e));

            TryParse(ns, levelName, "tricks.xml",
                e => tricks[levelName]   = _parser.ParseTricks(e, variant));

            TryParse(ns, levelName, "trigger.xml",
                e => triggers[levelName] = _parser.ParseTriggers(e));

            // Route from shared routes.bnd -- keyed as "routes:{levelName}/route.xml"
            var routeKey = $"routes:{levelName}/route.xml";
            if (Vfs.TryGet(routeKey, out var routeEntry))
            {
                try { routes[levelName] = _parser.ParseRoute(routeEntry); }
                catch (Exception ex)
                { Console.Error.WriteLine($"[Loader] Failed to parse {routeKey}: {ex.Message}"); }
            }
        }

        Layouts  = layouts;
        Combines = combines;
        Tricks   = tricks;
        Triggers = triggers;
        Routes   = routes;

        // 6. Dispatch GFX/SFX to sub-loaders
        _gfxLoader.LoadFromVfs(Vfs, ns);
        _sfxLoader.LoadFromVfs(Vfs, ns);
    }

    // --- Briefing path helper -------------------------------------------------

    /// <summary>
    /// Returns the VFS key for a level's briefing XML.
    /// NFH1: "nfh1:dialogs/briefing/{levelName}.xml"
    /// NFH2: "nfh2:{levelName}/briefing.xml"
    /// </summary>
    public string GetBriefingKey(string levelName)
    {
        return ActiveVariant == GameVariant.NFH1
            ? $"nfh1:dialogs/briefing/{levelName}.xml"
            : $"nfh2:{levelName}/briefing.xml";
    }

    /// <summary>
    /// Returns the VFS key for the wrong-strings file (NFH2 only).
    /// Returns null if variant is NFH1.
    /// </summary>
    public string? GetWrongStringsKey(string levelName) =>
        ActiveVariant == GameVariant.NFH2
            ? $"nfh2:{levelName}/wrongstrings.xml"
            : null;

    // --- Helpers -------------------------------------------------------------

    private void MountVariant(string dataPath, string ns)
    {
        var variantDir = Path.Combine(dataPath, ns.ToUpperInvariant());
        MountIfExists(Path.Combine(variantDir, "gamedata.bnd"),    ns, "");
        MountIfExists(Path.Combine(variantDir, "gfxdata.bnd"),     ns, "gfx");
        MountIfExists(Path.Combine(variantDir, "sfxdata.bnd"),     ns, "sfx");
        MountIfExists(Path.Combine(variantDir, "sfxdatahigh.bnd"), ns, "sfxhi");
    }

    private void MountIfExists(string path, string ns, string role)
    {
        if (File.Exists(path)) Vfs.MountBnd(path, ns, role);
    }

    private void TryParse(string ns, string levelName, string fileName, Action<VfsEntry> parse)
    {
        var key = $"{ns}:{levelName}/{fileName}";
        if (Vfs.TryGet(key, out var entry))
        {
            try { parse(entry); }
            catch (Exception ex)
            { Console.Error.WriteLine($"[Loader] Failed to parse {key}: {ex.Message}"); }
        }
    }
}

    // AnimsFile and ObjectsFile are parsed on-demand per level by GameLogic
    // using BndLoader.ParseAnims() and ObjectsFileParser.Parse() respectively.
    // They are not pre-loaded here to avoid memory overhead for all levels.
