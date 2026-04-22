using Neighborhood.Shared.Interfaces;
using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Core game logic for NFH1.
/// Owns and coordinates all subsystems:
///   WorldState, ActorController, InventorySystem,
///   TriggerSystem, ScoreSystem, NeighborBrain.
/// </summary>
public class GameLogic
{
    protected readonly IGfxLoader _gfxLoader;
    protected readonly ISfxLoader _sfxLoader;

    // --- Subsystems -----------------------------------------------------------
    public WorldState?       World      { get; private set; }
    public ActorController?  Actors     { get; private set; }
    public InventorySystem?  Inventory  { get; private set; }
    public TriggerSystem?    Triggers   { get; private set; }
    public ScoreSystem?      Score      { get; private set; }
    public NeighborBrain?    Brain      { get; private set; }
    public MotherBrain?      MotherBrain { get; private set; }
    public AnimationSystem?  Animations  { get; private set; }

    // --- Loaded data ----------------------------------------------------------
    protected IReadOnlyDictionary<string, LevelLayout>  Layouts     { get; private set; } = new Dictionary<string, LevelLayout>();
    protected IReadOnlyDictionary<string, CombineFile>  Combines    { get; private set; } = new Dictionary<string, CombineFile>();
    protected IReadOnlyDictionary<string, TricksFile>   Tricks      { get; private set; } = new Dictionary<string, TricksFile>();
    protected IReadOnlyDictionary<string, TriggersFile> TriggerData { get; private set; } = new Dictionary<string, TriggersFile>();
    protected IReadOnlyDictionary<string, RouteData>    Routes      { get; private set; } = new Dictionary<string, RouteData>();
    protected IReadOnlyDictionary<string, AnimsFile>    AnimsData   { get; private set; } = new Dictionary<string, AnimsFile>();
    protected LevelData? LevelData { get; private set; }

    // Settings (can be changed at runtime)
    public NeighborSettings NeighborSettings { get; } = new();

    public GameLogic(IGfxLoader gfxLoader, ISfxLoader sfxLoader)
    {
        _gfxLoader = gfxLoader;
        _sfxLoader = sfxLoader;
    }

    // --- Events --------------------------------------------------------------

    public event Action<string>?              LevelLoaded;
    public event Action<string, string>?      BehaviorTriggered;   // (actor, behavior)
    public event Action<InstalledTrick>?      TrickTriggered;
    public event Action<string>?              NeighborEnteredRoom; // roomName
    public event Action<InstalledTrick, string>? NeighborReacting; // (trick, behavior)
    public event Action?                      NeighborAlert;
    public event Action?                      WoodyCaught;
    public event Action<string>?              MotherEnteredRoom;
    public event Action?                      MotherAlert;
    public event Action?                      WoodyEscaped;
    public event Action?                      LevelWon;
    public event Action?                      LevelLost;

    // --- Initialise ----------------------------------------------------------

    public virtual void Initialise(
        IReadOnlyDictionary<string, LevelLayout>  layouts,
        IReadOnlyDictionary<string, CombineFile>  combines,
        IReadOnlyDictionary<string, TricksFile>   tricks,
        IReadOnlyDictionary<string, TriggersFile> triggers,
        IReadOnlyDictionary<string, RouteData>    routes,
        IReadOnlyDictionary<string, AnimsFile>    anims,
        LevelData                                 levelData)
    {
        Layouts     = layouts;
        Combines    = combines;
        Tricks      = tricks;
        TriggerData = triggers;
        Routes      = routes;
        AnimsData   = anims;
        LevelData   = levelData;
    }

    // --- LoadLevel -----------------------------------------------------------

    public virtual void LoadLevel(string levelName)
    {
        if (!Layouts.TryGetValue(levelName, out var layout))
            throw new KeyNotFoundException($"Level layout not found: '{levelName}'");

        var variant = LevelData?.Variant ?? GameVariant.NFH1;
        var combine = Combines.GetValueOrDefault(levelName)         ?? new CombineFile();
        var tricks  = Tricks.GetValueOrDefault(levelName)           ?? new TricksFile();
        var levelTriggers   = TriggerData.GetValueOrDefault(levelName)  ?? new TriggersFile();
        var genericTriggers = TriggerData.GetValueOrDefault("generic")  ?? new TriggersFile();

        var levelEntry = LevelData?.Sets
            .SelectMany(s => s.Levels)
            .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

        // Build world
        World = new WorldState();
        World.Initialise(layout, levelName);

        // Resolve route -- use fixed if available, otherwise generate random
        var routeData = Routes.GetValueOrDefault(levelName) ?? new RouteData();
        if (routeData.IsEmpty || NeighborSettings.PatrolMode == PatrolMode.Random)
        {
            var roomNames = layout.Rooms.Select(r => r.Name);
            routeData = RouteData.GenerateRandom(roomNames, levelName);
        }

        // Build subsystems
        var graph = RoomGraph.Build(layout);
        Actors    = new ActorController(World, graph);
        Inventory = new InventorySystem(World, combine, tricks, variant);
        Triggers  = new TriggerSystem(World, levelTriggers, genericTriggers);
        Brain     = new NeighborBrain(World, Actors, routeData, NeighborSettings);

        // NFH1: time is in seconds. NFH2: time is in minutes -> convert to seconds.
        int timeSecs = levelEntry?.Time ?? 0;
        if (variant == GameVariant.NFH2 && timeSecs > 0)
            timeSecs *= 60;

        Score = new ScoreSystem(
            variant:          variant,
            minProgress:      levelEntry?.MinProgress ?? 0,
            timeLimitSeconds: timeSecs);

        // Build MotherBrain if Mother is present on this level
        if (World.Mother != null)
        {
            var motherRouteName = levelName + "_mother";
            var motherRoute     = Routes.GetValueOrDefault(motherRouteName) ?? new RouteData();
            if (motherRoute.IsEmpty || NeighborSettings.PatrolMode == PatrolMode.Random)
            {
                var roomNames = layout.Rooms.Select(r => r.Name);
                motherRoute = RouteData.GenerateRandom(roomNames, motherRouteName);
            }
            MotherBrain = new MotherBrain(World, Actors, motherRoute, NeighborSettings);
        }
        else
        {
            MotherBrain = null;
        }

        // Initialise AnimationSystem
        Animations = new AnimationSystem(this);
        // Note: Animations.Initialise() is called by the rendering layer after
        // it has loaded the AnimsFile and ObjectsFile for this level from the VFS.
        // This keeps the animation system decoupled from the loader pipeline.

        // Wire brain references into TriggerSystem for blind-state checks
        // Brain may be null on tutorial levels without a neighbor
        if (Triggers != null)
        {
            Triggers.NeighborBrain = Brain;
            Triggers.MotherBrain   = MotherBrain;
        }

        // Wire events
        WireEvents();
        OnLevelLoaded(layout, levelName);

        // Start music.
        // NFH2: MusicPath set per-level in leveldata.xml.
        // NFH1: no MusicPath field -- pick ingame1/2_normal randomly (deterministic per level).
        {
            var player = (_sfxLoader as dynamic)?.Player;
            if (player != null)
            {
                if (!string.IsNullOrEmpty(levelEntry?.MusicPath))
                    player.PlayMusic(Path.GetFileName((string)levelEntry.MusicPath));
                else if (variant == GameVariant.NFH1)
                    player.PlayMusic(new Random(levelName.GetHashCode()).Next(2) == 0
                        ? "ingame1_normal.mp3" : "ingame2_normal.mp3");
            }
        }

        Score.Start();
        Brain?.StartPatrol();
        MotherBrain?.StartPatrol();
        LevelLoaded?.Invoke(levelName);
    }

    protected virtual void OnLevelLoaded(LevelLayout layout, string levelName) { }

    // --- Update --------------------------------------------------------------

    public virtual void Update(int deltaMs)
    {
        if (World == null) return;

        Actors!.Update(deltaMs);
        Brain?.Update(deltaMs);
        MotherBrain?.Update(deltaMs);
        Animations?.Update(deltaMs);
        Triggers!.Evaluate();
        Score!.Update(deltaMs);

        OnUpdate(deltaMs);
    }

    protected virtual void OnUpdate(int deltaMs) { }

    /// <summary>
    /// Called when any threat actor (neighbor or mother) spots Woody.
    /// Override in GameLogicWithMiniGames to route mother separately.
    /// </summary>
    protected virtual void OnWoodySpotted() => Brain?.OnWoodySpotted();

    // --- Input ---------------------------------------------------------------

    public CombineResult TryCombine(string a, string b) =>
        Inventory?.TryCombine(a, b) ?? CombineResult.NoMatch;

    public void ReportNoise(int level) =>
        Triggers?.ReportNoise(level);

    /// <summary>
    /// Called by rendering layer when Woody enters neighbor's line of sight.
    /// Switches neighbor to ALERT state.
    /// </summary>
    public void ReportWoodySpotted() =>
        Brain?.OnWoodySpotted();

    /// <summary>
    /// Called by rendering/animation layer when neighbor's reaction animation ends.
    /// </summary>
    public void FinishNeighborReaction() =>
        Brain?.FinishReaction();

    // --- Event wiring ---------------------------------------------------------

    private void WireEvents()
    {
        // TriggerSystem -> Brain
        Triggers!.TrickTriggered += trick =>
        {
            Score!.OnTrickTriggered(trick);
            TrickTriggered?.Invoke(trick);
        };

        Triggers!.BehaviorTriggered += (actor, behavior) =>
        {
            BehaviorTriggered?.Invoke(actor, behavior);

            // If neighbor triggers a behavior tied to an installed trick -> REACT
            if (actor.Equals("neighbor", StringComparison.OrdinalIgnoreCase))
            {
                var trick = World!.InstalledTricks.Values
                    .FirstOrDefault(t => !t.IsTriggered &&
                        World.Layout.Rooms.Any(r =>
                            r.Name.Equals(World.Neighbor.CurrentRoom, StringComparison.OrdinalIgnoreCase) &&
                            r.Objects.Any(o => o.Name.Equals(t.ResultObjectName, StringComparison.OrdinalIgnoreCase))));

                if (trick != null)
                    Brain?.OnTrickDiscovered(trick, behavior);
            }
        };

        // TriggerSystem -> Brain: Woody spotted by neighbor (or mother in NFH2)
        // In NFH2, GameLogicWithMiniGames overrides this to also handle mother
        Triggers!.WoodySpotted += OnWoodySpotted;

        // Brain events (only wire when Brain exists -- tutorials have no neighbor)
        if (Brain != null)
        {
            Brain.EnteredRoom     += room  => NeighborEnteredRoom?.Invoke(room);
            Brain.ReactingToTrick += (t,b) => NeighborReacting?.Invoke(t, b);
            Brain.AlertStarted    += ()    => NeighborAlert?.Invoke();
            Brain.WoodyCaught     += ()    =>
            {
                Score!.OnWoodyCaught();
                WoodyCaught?.Invoke();
            };
            Brain.WoodyEscaped    += ()    => WoodyEscaped?.Invoke();
        }

        // Mother brain events (only wired when Mother is present)
        if (MotherBrain != null)
        {
            MotherBrain.EnteredRoom  += room => MotherEnteredRoom?.Invoke(room);
            MotherBrain.AlertStarted += ()   => MotherAlert?.Invoke();
            MotherBrain.WoodyCaught  += ()   =>
            {
                Score!.OnWoodyCaught();
                WoodyCaught?.Invoke();
            };
            MotherBrain.WoodyEscaped += ()   => WoodyEscaped?.Invoke();
        }

        // Score events
        Score!.LevelWon  += () => LevelWon?.Invoke();
        Score!.LevelLost += () => LevelLost?.Invoke();
    }
}
