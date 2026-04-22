namespace Neighborhood.Shared.Models;

// -----------------------------------------------------------------------------
// combine.xml
// NFH1: no root element -- parser wraps in <combines> before parsing.
// NFH2: root is <combines level="...">
// -----------------------------------------------------------------------------

public class CombineFile
{
    /// <summary>NFH2 only: level name this file belongs to (combines level="...").</summary>
    public string? Level { get; init; }

    public IReadOnlyList<Combination> Combinations { get; init; } = [];
}

public class Combination
{
    /// <summary>Name of the resulting object/state after combination.</summary>
    public string ResultName { get; init; } = string.Empty;

    /// <summary>True = this combination produces a prank (trick).</summary>
    public bool IsTrick { get; init; }

    /// <summary>True = invalid combination; engine shows WrongString feedback.</summary>
    public bool IsWrong { get; init; }

    /// <summary>
    /// NFH2 only: triggers a mini-game instead of directly placing result.
    /// Points to a minigame/*.xml path (e.g. "minigame/bamboo.xml").
    /// </summary>
    public string? MiniGamePath { get; init; }

    /// <summary>NFH2 only: mini-game difficulty start level.</summary>
    public int MiniGameStartLevel { get; init; }

    /// <summary>NFH2 only: mini-game difficulty end level.</summary>
    public int MiniGameEndLevel { get; init; }

    /// <summary>
    /// NFH2 only: result object layer override.
    /// -1 means use the object's default layer.
    /// </summary>
    public int Layer { get; init; } = -1;

    /// <summary>Optional animation to play on result object (e.g. anim="empty").</summary>
    public string? ResultAnim { get; init; }

    public IReadOnlyList<Ingredient> Ingredients { get; init; } = [];
}

public class Ingredient
{
    public string Name { get; init; } = string.Empty;

    /// <summary>If true, the ingredient is removed from the scene after use.</summary>
    public bool Remove { get; init; }

    /// <summary>NFH2 only: layer override for this ingredient.</summary>
    public int Layer { get; init; } = -1;
}

// -----------------------------------------------------------------------------
// tricks.xml
// NFH1: quota1..4 (percent, 4 difficulty levels) + optional angrytime (seconds)
// NFH2: coins (integer) + rage (milliseconds)
// -----------------------------------------------------------------------------

public class TricksFile
{
    public IReadOnlyList<Trick> Tricks { get; init; } = [];
}

public class Trick
{
    public string Name { get; init; } = string.Empty;

    // -- NFH1 fields ----------------------------------------------------------

    /// <summary>NFH1: score per difficulty level (quota1..quota4). Empty for NFH2.</summary>
    public IReadOnlyList<int> Quotas { get; init; } = [];

    /// <summary>
    /// NFH1: additional angry time in seconds this trick adds to the level timer.
    /// 0 = not set.
    /// </summary>
    public int AngryTime { get; init; }

    // -- NFH2 fields ----------------------------------------------------------

    /// <summary>NFH2: coins awarded for executing this trick.</summary>
    public int Coins { get; init; }

    /// <summary>NFH2: rage added to neighbor's rage meter in milliseconds.</summary>
    public int Rage { get; init; }
}

// -----------------------------------------------------------------------------
// trigger.xml
// Both variants share the same structure; NFH2 usually has empty <triggers/>.
// -----------------------------------------------------------------------------

public class TriggersFile
{
    public IReadOnlyList<ActorTriggerGroup> Actors { get; init; } = [];
}

public class ActorTriggerGroup
{
    public string ActorName { get; init; } = string.Empty;
    public IReadOnlyList<BehaviorTrigger> Behaviors { get; init; } = [];
}

public class BehaviorTrigger
{
    public string BehaviorName { get; init; } = string.Empty;
    public IReadOnlyList<TriggerCondition> Conditions { get; init; } = [];
}

public class TriggerCondition
{
    /// <summary>Object name that must exist in its triggered state. Null if noise-based.</summary>
    public string? ObjectName { get; init; }

    /// <summary>Noise level required (NFH1 generic triggers). 0 = not used.</summary>
    public int Noise { get; init; }

    /// <summary>"nearobj", "room", "house"</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>"once" or "always"</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Optional actor filter on the trigger element itself (NFH1 only).
    /// e.g. &lt;trigger object="lir/dirtycarpet" position="room" type="always" actor="neighbor"/&gt;
    /// When set, condition only applies to this specific actor.
    /// </summary>
    public string? Actor { get; init; }
}

// -----------------------------------------------------------------------------
// wrongstrings.xml  (NFH2 only)
// -----------------------------------------------------------------------------

public class WrongStringsFile
{
    /// <summary>Key: combination name -> wrong-feedback text shown to player.</summary>
    public IReadOnlyDictionary<string, string> Entries { get; init; }
        = new Dictionary<string, string>();
}

// -----------------------------------------------------------------------------
// minigame/*.xml  (NFH2 only)
// -----------------------------------------------------------------------------

public class MiniGameDef
{
    public string BackgroundFile { get; init; } = string.Empty;
    public string AlarmFile { get; init; } = string.Empty;
    public string ThumbFile { get; init; } = string.Empty;
    public string IconFile { get; init; } = string.Empty;
    public bool ProgressBarVertical { get; init; }
    public string ProgressBarFront { get; init; } = string.Empty;
    public NfhPoint ProgressBarOffset { get; init; }
}
