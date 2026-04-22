using Neighborhood.Shared.Models;
namespace Neighborhood.NFH1;

/// <summary>
/// Tracks score and determines win/loss conditions.
///
/// NFH1 system:
///   - Level has a time limit (angrytime in seconds from level.xml)
///   - Each trick adds to quota (percentage points)
///   - Level won when time runs out if quota >= minquota
///   - Level lost if neighbor catches Woody (quota = 0 regardless)
///
/// NFH2 system:
///   - Level has a time limit (time in minutes from leveldata.xml)
///   - Each trick adds coins and rage to neighbor's meter
///   - Neighbor has a respawntime before reappearing after being caught
///   - Level won when time runs out if coins >= mincoins
/// </summary>
public class ScoreSystem
{
    private readonly GameVariant _variant;
    private readonly int         _minProgress;  // minquota (NFH1) or mincoins (NFH2)
    private readonly int         _timeLimitMs;  // total level time in ms

    private int _elapsedMs;
    private int _progress;    // current quota % (NFH1) or coins (NFH2)
    private int _rage;        // NFH2 only: neighbor rage meter (ms until alarm)
    private bool _isRunning;

    public ScoreSystem(GameVariant variant, int minProgress, int timeLimitSeconds)
    {
        _variant     = variant;
        _minProgress = minProgress;
        _timeLimitMs = timeLimitSeconds * 1000;
    }

    // --- Events --------------------------------------------------------------

    public event Action<int>? ProgressChanged;   // current progress value
    public event Action<int>? TimeUpdated;        // remaining ms
    public event Action?      LevelWon;
    public event Action?      LevelLost;

    // --- State ---------------------------------------------------------------

    public int  Progress       => _progress;
    public int  RemainingMs    => Math.Max(0, _timeLimitMs - _elapsedMs);
    public int  RemainingSeconds => RemainingMs / 1000;
    public bool IsRunning      => _isRunning;

    /// <summary>NFH2: neighbor rage (ms). When full -> neighbor goes to alarm state.</summary>
    public int  Rage           => _rage;

    public bool HasWon  { get; private set; }
    public bool HasLost { get; private set; }

    // --- Control -------------------------------------------------------------

    public void Start()
    {
        _isRunning = true;
        HasWon  = false;
        HasLost = false;
        _elapsedMs = 0;
        _progress  = 0;
        _rage      = 0;
    }

    public void Pause()  => _isRunning = false;
    public void Resume() => _isRunning = true;

    // --- Update --------------------------------------------------------------

    /// <summary>Advance time by deltaMs. Call once per game tick.</summary>
    public void Update(int deltaMs)
    {
        if (!_isRunning || HasWon || HasLost) return;

        _elapsedMs += deltaMs;
        TimeUpdated?.Invoke(RemainingMs);

        // NFH1: time runs out -> evaluate result
        if (_timeLimitMs > 0 && _elapsedMs >= _timeLimitMs)
        {
            _isRunning = false;
            if (_progress >= _minProgress)
                Win();
            else
                Lose();
        }
    }

    // --- Trick scored --------------------------------------------------------

    /// <summary>
    /// Called by TriggerSystem when a trick fires.
    /// Adds progress and anger values from the trick.
    /// </summary>
    public void OnTrickTriggered(InstalledTrick trick)
    {
        _progress += trick.ProgressValue;
        ProgressChanged?.Invoke(_progress);

        if (_variant == GameVariant.NFH2)
        {
            _rage += trick.AngerValue;
            // NFH1: angerValue extends the level timer
        }
        else if (trick.AngerValue > 0)
        {
            // NFH1: angrytime tricks extend the level's remaining time
            // (original game does this for certain tricks like foambottle)
            _elapsedMs = Math.Max(0, _elapsedMs - trick.AngerValue * 1000);
        }
    }

    // --- Woody caught --------------------------------------------------------

    /// <summary>
    /// Called when neighbor catches Woody.
    /// NFH1: immediate loss.
    /// NFH2: Woody respawns, score not reset.
    /// </summary>
    public void OnWoodyCaught()
    {
        if (_variant == GameVariant.NFH1)
            Lose();
        // NFH2: handled by GameLogicWithMiniGames respawn logic
    }

    // --- Win / Lose ----------------------------------------------------------

    private void Win()
    {
        HasWon = true;
        LevelWon?.Invoke();
    }

    private void Lose()
    {
        HasLost = true;
        LevelLost?.Invoke();
    }
}
