namespace Neighborhood.Shared.Models;

/// <summary>
/// Parsed from leveldata.xml (root of gamedata.bnd).
///
/// NFH1: UTF-16 LE, progress by minquota (%), time in seconds.
/// NFH2: UTF-8, progress by mincoins (count), time in minutes,
///        each level carries its own gui and music path.
/// </summary>
public class LevelData
{
    public GameVariant Variant { get; init; }

    /// <summary>
    /// NFH2 only: respawn delay in seconds (<leveldata respawntime="60">).
    /// 0 for NFH1.
    /// </summary>
    public int RespawnTime { get; init; }

    public IReadOnlyList<LevelSet> Sets { get; init; } = [];
}

public class LevelSet
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Name of the next set to unlock after completing this one. Null for final set.</summary>
    public string? NextSet { get; init; }

    /// <summary>NFH2 only: minimum coins needed to unlock this set.</summary>
    public int MinCoins { get; init; }

    /// <summary>"playable", "locked", "activate"</summary>
    public string State { get; init; } = string.Empty;

    public IReadOnlyList<LevelEntry> Levels { get; init; } = [];
}

public class LevelEntry
{
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;

    /// <summary>How many levels must be completed to unlock this one.</summary>
    public int Reachable { get; init; }

    /// <summary>
    /// NFH1: minimum completion quota in percent (minquota).
    /// NFH2: minimum coins required (mincoins).
    /// </summary>
    public int MinProgress { get; init; }

    /// <summary>
    /// NFH1: time limit in seconds.
    /// NFH2: time limit in minutes.
    /// </summary>
    public int Time { get; init; }

    /// <summary>NFH2 only: path to GUI dialog folder (e.g. "dialogs/nomother").</summary>
    public string? GuiPath { get; init; }

    /// <summary>NFH2 only: path to background music (e.g. "music/ingame_china.mp3").</summary>
    public string? MusicPath { get; init; }
}
