using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Evaluates trigger conditions from trigger.xml and fires actor behaviors.
///
/// Two types of triggers from the XML analysis:
///
///   Object triggers (level-specific):
///     &lt;trigger object="toi/groundsoap" position="nearobj" type="once"/&gt;
///     -> fires when neighbor is in the same room as the installed trick object
///
///   Noise triggers (generic):
///     &lt;trigger noise="2" position="house" type="always"/&gt;
///     -> fires when noise level exceeds threshold anywhere in the house
///
/// Position values:
///   "nearobj" -- neighbor must be in same room as the object
///   "room"    -- neighbor must be in same room (noise-based)
///   "house"   -- anywhere in the level
///
/// Type values:
///   "once"   -- trigger fires once, then deactivates
///   "always" -- fires every time condition is met
/// </summary>
public class TriggerSystem
{
    private readonly WorldState     _world;
    private readonly TriggersFile   _levelTriggers;
    private readonly TriggersFile   _genericTriggers;

    // Optional brain references for blind-state checking
    // Set by GameLogic after brains are created
    public NeighborBrain? NeighborBrain { get; set; }
    public MotherBrain?   MotherBrain   { get; set; }

    /// <summary>Tracks which "once" triggers have already fired. Key: behaviorName.</summary>
    private readonly HashSet<string> _firedOnce = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Current noise level (set by game logic when Woody does noisy actions).</summary>
    public int CurrentNoiseLevel { get; set; }

    public TriggerSystem(
        WorldState   world,
        TriggersFile levelTriggers,
        TriggersFile genericTriggers)
    {
        _world           = world;
        _levelTriggers   = levelTriggers;
        _genericTriggers = genericTriggers;
    }

    // --- Events --------------------------------------------------------------

    /// <summary>
    /// Fired when a trigger condition is met.
    /// Args: (actorName, behaviorName) -- e.g. ("neighbor", "soap_on_floor").
    /// The behavior name maps to an animation/action in objects.xml.
    /// </summary>
    public event Action<string, string>? BehaviorTriggered;

    /// <summary>
    /// Fired when a trick trigger fires (neighbor discovers the prank).
    /// Args: trick that was triggered.
    /// </summary>
    public event Action<InstalledTrick>? TrickTriggered;

    /// <summary>
    /// Fired when Neighbor spots Woody.
    /// NFH1: noise="2" behavior="alarm" (sound-based).
    /// NFH2: object="woody" behavior="fight" (sight-based).
    /// -> Brain.OnWoodySpotted()
    /// </summary>
    public event Action? WoodySpottedByNeighbor;

    /// <summary>
    /// Fired when Mother spots Woody (NFH2 only, behavior="fight").
    /// -> MotherBrain.OnWoodySpotted()
    /// Mother does NOT react to noise -- only to visual contact.
    /// </summary>
    public event Action? WoodySpottedByMother;

    /// <summary>Convenience: fires both events. Kept for backward compat.</summary>
    public event Action? WoodySpotted;

    // --- Evaluation ----------------------------------------------------------

    /// <summary>
    /// Evaluates all trigger conditions for the current world state.
    /// Called once per game tick by GameLogic.Update().
    /// </summary>
    public void Evaluate()
    {
        EvaluateTriggerFile(_levelTriggers);
        EvaluateTriggerFile(_genericTriggers);
    }

    /// <summary>
    /// Called when Woody performs a noisy action.
    /// Sets noise level and immediately evaluates noise-based triggers.
    /// </summary>
    public void ReportNoise(int noiseLevel)
    {
        CurrentNoiseLevel = noiseLevel;
        Evaluate();
        // Noise decays after evaluation (one-shot per action)
        CurrentNoiseLevel = 0;
    }

    // --- Private -------------------------------------------------------------

    private void EvaluateTriggerFile(TriggersFile triggers)
    {
        foreach (var actorGroup in triggers.Actors)
        {
            var actor = GetActor(actorGroup.ActorName);
            if (actor == null) continue;

            foreach (var behavior in actorGroup.Behaviors)
            {
                if (EvaluateBehavior(actor, behavior))
                {
                    BehaviorTriggered?.Invoke(actorGroup.ActorName, behavior.BehaviorName);

                    // Determine alert type:
                    // "alarm" = NFH1 noise trigger (Neighbor only)
                    // "fight" = NFH2 visual trigger (Neighbor or Mother)
                    bool isAlarm = behavior.BehaviorName.Equals("alarm", StringComparison.OrdinalIgnoreCase);
                    bool isFight = behavior.BehaviorName.Equals("fight", StringComparison.OrdinalIgnoreCase);
                    bool isNeighbor = actorGroup.ActorName.Equals("neighbor", StringComparison.OrdinalIgnoreCase);
                    bool isMother   = actorGroup.ActorName.Equals("mother",   StringComparison.OrdinalIgnoreCase);

                    // Neighbor: alarm (NFH1) or fight (NFH2)
                    // Suppressed when neighbor is in a blind step
                    if ((isAlarm || isFight) && isNeighbor &&
                        NeighborBrain?.IsBlind != true)
                    {
                        WoodySpottedByNeighbor?.Invoke();
                        WoodySpotted?.Invoke();
                    }
                    // Mother: fight only (NFH2), NOT alarm
                    // Suppressed when mother is in a blind step
                    else if (isFight && isMother &&
                             MotherBrain?.IsBlind != true)
                    {
                        WoodySpottedByMother?.Invoke();
                        WoodySpotted?.Invoke();
                    }

                    // If this is a trick trigger, also fire TrickTriggered
                    foreach (var cond in behavior.Conditions.Where(c => c.ObjectName != null))
                    {
                        if (_world.TryGetTrick(cond.ObjectName!, out var trick) && !trick.IsTriggered)
                        {
                            trick.IsTriggered = true;
                            TrickTriggered?.Invoke(trick);
                        }
                    }
                }
            }
        }
    }

    private bool EvaluateBehavior(ActorInstance actor, BehaviorTrigger behavior)
    {
        // All conditions in a behavior must be satisfied (AND logic)
        foreach (var condition in behavior.Conditions)
        {
            if (!EvaluateCondition(actor, behavior.BehaviorName, condition))
                return false;
        }
        return behavior.Conditions.Count > 0;
    }

    private bool EvaluateCondition(ActorInstance actor, string behaviorName, TriggerCondition condition)
    {
        // Optional actor filter -- if specified, only apply to that actor
        if (condition.Actor != null &&
            !condition.Actor.Equals(actor.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        // "once" type: skip if already fired
        if (condition.Type.Equals("once", StringComparison.OrdinalIgnoreCase) &&
            _firedOnce.Contains(behaviorName))
            return false;

        bool conditionMet;

        if (condition.ObjectName != null)
        {
            // Object-based trigger: is neighbor near the trick object?
            conditionMet = EvaluateObjectCondition(actor, condition);
        }
        else
        {
            // Noise-based trigger
            conditionMet = EvaluateNoiseCondition(actor, condition);
        }

        if (conditionMet && condition.Type.Equals("once", StringComparison.OrdinalIgnoreCase))
            _firedOnce.Add(behaviorName);

        return conditionMet;
    }

    private bool EvaluateObjectCondition(ActorInstance actor, TriggerCondition condition)
    {
        var objectName = condition.ObjectName!;

        // Special case: object is an actor name (NFH2 fight/die triggers)
        // object="woody"    -> check if Woody is in range
        // object="neighbor" -> check if neighbor is in range
        // object="mother"   -> check if mother is in range
        // object="olga"     -> olga does NOT trigger fight behaviors; skip
        var knownActors = new[] { "woody", "neighbor", "mother", "olga", "kid", "aux" };
        if (knownActors.Any(a => a.Equals(objectName, StringComparison.OrdinalIgnoreCase)))
        {
            // Olga is not a threat -- she never triggers fight/alarm on neighbor/mother
            if (objectName.Equals("olga", StringComparison.OrdinalIgnoreCase))
                return false;

            // includeOlga=true here because we ARE looking for olga as a target object
            // but we already returned false above, so this path never hits olga
            var targetActor = _world.GetActor(objectName, includeOlga: true);
            if (targetActor == null) return false;

            return condition.Position switch
            {
                "room"  => IsActorInSameRoomAs(actor, targetActor),
                "house" => true,
                _       => IsActorInSameRoomAs(actor, targetActor)
            };
        }

        // Normal case: object must exist and be visible (installed trick or world object)
        if (!_world.TryGetObject(objectName, out var obj) || !obj.Visible)
            return false;

        return condition.Position switch
        {
            "nearobj" => IsActorInSameRoomAsObject(actor, objectName),
            "room"    => IsActorInSameRoomAsObject(actor, objectName),
            "house"   => true,
            _         => false
        };
    }

    private bool EvaluateNoiseCondition(ActorInstance actor, TriggerCondition condition)
    {
        if (CurrentNoiseLevel < condition.Noise)
            return false;

        return condition.Position switch
        {
            "house" => true,
            "room"  => IsActorInSameRoomAs(actor, _world.Woody),
            _       => false
        };
    }

    private bool IsActorInSameRoomAsObject(ActorInstance actor, string objectName)
    {
        var objectRoom = _world.Layout.Rooms.FirstOrDefault(r =>
            r.Objects.Any(o => o.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase)));

        return objectRoom != null &&
               objectRoom.Name.Equals(actor.CurrentRoom, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActorInSameRoomAs(ActorInstance a, ActorInstance b) =>
        a.CurrentRoom.Equals(b.CurrentRoom, StringComparison.OrdinalIgnoreCase);

    private ActorInstance? GetActor(string name) =>
        // Olga excluded -- she is not a threat actor and should not evaluate triggers
        _world.GetActor(name, includeOlga: false);

    /// <summary>Resets all one-shot trigger history (call on level restart).</summary>
    public void Reset() => _firedOnce.Clear();
}
