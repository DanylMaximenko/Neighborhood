using Neighborhood.NFH1;
using Neighborhood.Shared.Interfaces;
using Neighborhood.Shared.Models;

namespace Neighborhood.NFH2;

/// <summary>
/// Extends NFH1 GameLogic with NFH2-specific features:
///
///   1. Mini-game support -- certain combinations trigger a mini-game.
///   2. Respawn system -- neighbor/mother respawn after catching Woody.
///   3. Mother (Neighbor's Mother) -- secondary threat actor present from ship2.
///      Has her own simplified brain: patrols and beats Woody like neighbor,
///      but uses a different route and animation set.
///   4. Olga -- passive NPC, not a threat, no brain needed.
///   5. Rage meter -- neighbor rage builds from tricks.
/// </summary>
public class GameLogicWithMiniGames : GameLogic
{
    private int  _respawnTimeMs;
    private int  _respawnElapsedMs;
    private bool _neighborRespawning;

    // Mother has her own simplified brain (same NeighborBrain class, different instance)
    private NeighborBrain? _motherBrain;

    private IReadOnlyDictionary<string, MiniGameDef> _miniGames =
        new Dictionary<string, MiniGameDef>();

    public ActiveMiniGame? CurrentMiniGame { get; private set; }

    public GameLogicWithMiniGames(IGfxLoader gfxLoader, ISfxLoader sfxLoader)
        : base(gfxLoader, sfxLoader) { }

    // --- Events --------------------------------------------------------------

    public event Action<ActiveMiniGame>? MiniGameStarted;
    public event Action<ActiveMiniGame, bool>? MiniGameEnded;
    public event Action<int>? NeighborRespawning;

    /// <summary>
    /// Fired when Neighbor is in the same room as Mother while she beats Woody.
    /// Rendering layer plays Neighbor's laugh animation and sound.
    /// </summary>
    public event Action? NeighborLaughing;

    // --- Initialise ----------------------------------------------------------

    public void InitialiseMiniGames(IReadOnlyDictionary<string, MiniGameDef> miniGames) =>
        _miniGames = miniGames;

    protected override void OnLevelLoaded(LevelLayout layout, string levelName)
    {
        base.OnLevelLoaded(layout, levelName);
        WireMotherLaughEvents();
        _respawnTimeMs      = LevelData?.RespawnTime * 1000 ?? 60_000;
        _respawnElapsedMs   = 0;
        _neighborRespawning = false;
        CurrentMiniGame     = null;

        // Build Mother's brain if she's present on this level
        _motherBrain = null;
        if (World?.Mother != null)
        {
            // Mother uses a random route derived from the same room graph
            var motherRoute = RouteData.GenerateRandom(
                layout.Rooms.Select(r => r.Name), levelName + "_mother");

            _motherBrain = new NeighborBrain(World, Actors!, motherRoute, NeighborSettings);

            // Mother also triggers WoodySpotted -- she beats Woody on sight
            _motherBrain.WoodyCaught += () =>
            {
                Score?.OnWoodyCaught();
                // Reuse WoodyCaught event from base
            };

            _motherBrain.StartPatrol();
        }
    }

    // --- Update --------------------------------------------------------------

    protected override void OnUpdate(int deltaMs)
    {
        // Respawn timer
        if (_neighborRespawning)
        {
            _respawnElapsedMs += deltaMs;
            if (_respawnElapsedMs >= _respawnTimeMs)
            {
                _neighborRespawning = false;
                _respawnElapsedMs   = 0;
                RespawnNeighbor();
            }
        }

        // Mother brain update
        _motherBrain?.Update(deltaMs);

        // Mini-game timer
        if (CurrentMiniGame != null)
        {
            CurrentMiniGame.ElapsedMs += deltaMs;
            if (CurrentMiniGame.ElapsedMs >= CurrentMiniGame.TimeLimitMs)
                EndMiniGame(success: false);
        }
    }

    // --- Mini-game ------------------------------------------------------------

    public void StartMiniGame(string resultObjectName, string miniGamePath,
                               int startLevel, int endLevel)
    {
        if (!_miniGames.TryGetValue(miniGamePath, out var def)) return;

        CurrentMiniGame = new ActiveMiniGame(
            resultObjectName: resultObjectName,
            definition:       def,
            currentLevel:     startLevel,
            maxLevel:         endLevel,
            timeLimitMs:      30_000);

        MiniGameStarted?.Invoke(CurrentMiniGame);
    }

    public void EndMiniGame(bool success)
    {
        if (CurrentMiniGame == null) return;
        var mg = CurrentMiniGame;
        CurrentMiniGame = null;

        if (success)
            World?.SetObjectVisible(mg.ResultObjectName, true);

        MiniGameEnded?.Invoke(mg, success);
    }

    // --- Respawn -------------------------------------------------------------

    public void OnNeighborCaughtWoody()
    {
        _neighborRespawning = true;
        _respawnElapsedMs   = 0;
        NeighborRespawning?.Invoke(_respawnTimeMs);
    }

    private void RespawnNeighbor()
    {
        if (World?.Neighbor == null) return;
        var startRoom = World.Layout.Rooms
            .FirstOrDefault(r => r.Actors.Any(a =>
                a.Name.Equals("neighbor", StringComparison.OrdinalIgnoreCase)));

        if (startRoom != null)
        {
            World.Neighbor.CurrentRoom = startRoom.Name;
            Actors?.NavigateTo("neighbor", startRoom.Name);
        }
    }

    // --- Mother alert ---------------------------------------------------------

    /// <summary>
    /// Called when Mother spots Woody (from TriggerSystem.WoodySpotted
    /// when the trigger actor is "mother"). Delegates to mother's brain.
    /// </summary>
    public void OnMotherSpottedWoody() =>
        _motherBrain?.OnWoodySpotted();

    public void FinishMotherReaction() =>
        _motherBrain?.FinishReaction();

    /// <summary>
    /// Routes WoodySpotted to the correct brain:
    /// If mother is present and in same room as Woody -> mother spotted him.
    /// Otherwise -> neighbor spotted him.
    /// </summary>
    protected override void OnWoodySpotted()
    {
        // Check if mother is the one in Woody's room
        if (World?.Mother != null && _motherBrain != null &&
            World.Mother.CurrentRoom.Equals(
                World.Woody.CurrentRoom, StringComparison.OrdinalIgnoreCase))
        {
            _motherBrain.OnWoodySpotted();
        }
        else
        {
            Brain?.OnWoodySpotted();
        }
    }
    private void WireMotherLaughEvents()
    {
        // When Mother starts beating Woody and Neighbor is in the same room -> Neighbor laughs
        if (MotherBrain != null)
        {
            MotherBrain.CatchSequenceStarted += () =>
            {
                if (World?.Neighbor != null && World.Mother != null &&
                    World.Neighbor.CurrentRoom.Equals(
                        World.Mother.CurrentRoom, StringComparison.OrdinalIgnoreCase))
                {
                    NeighborLaughing?.Invoke();
                }
            };
        }
    }
}

// --- Mini-game state ----------------------------------------------------------

public class ActiveMiniGame
{
    public string      ResultObjectName { get; }
    public MiniGameDef Definition       { get; }
    public int         CurrentLevel     { get; set; }
    public int         MaxLevel         { get; }
    public int         TimeLimitMs      { get; }
    public int         ElapsedMs        { get; set; }
    public int         RemainingMs      => Math.Max(0, TimeLimitMs - ElapsedMs);

    public ActiveMiniGame(string resultObjectName, MiniGameDef definition,
                           int currentLevel, int maxLevel, int timeLimitMs)
    {
        ResultObjectName = resultObjectName;
        Definition       = definition;
        CurrentLevel     = currentLevel;
        MaxLevel         = maxLevel;
        TimeLimitMs      = timeLimitMs;
    }
}
