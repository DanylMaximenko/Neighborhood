using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Top-level animation coordinator for a level session.
///
/// Owns:
///   - AnimationRegistry (merged anims.xml)
///   - One ActorAnimator per actor
///   - One ObjectAnimator per world object
///
/// Bridges between game logic events and animation playback:
///   GameLogic.BehaviorTriggered   -> ActorAnimator.PlayAction()
///   NeighborBrain.ObjectInteraction -> ActorAnimator + ObjectAnimator in sync
///   NeighborBrain.OnAnimationComplete() <- AnimationSystem notifies when done
///
/// Also updates ActorController movement animations:
///   ActorController.ActorEnteredRoom -> SetState("mg0") idle
///   During transition -> SetState("mr0") run
/// </summary>
public class AnimationSystem
{
    private readonly AnimationRegistry _registry = new();
    private readonly GameLogic         _logic;

    private readonly Dictionary<string, ActorAnimator>  _actorAnimators  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ObjectAnimator> _objectAnimators = new(StringComparer.OrdinalIgnoreCase);

    // Parsed action defs from objects.xml keyed by object name
    private readonly Dictionary<string, ObjectDef> _objectDefs = new(StringComparer.OrdinalIgnoreCase);

    public AnimationSystem(GameLogic logic)
    {
        _logic = logic;
    }

    // --- Events (for renderer) ------------------------------------------------

    /// <summary>An actor's visible sprite changed. Args: (actorName, spriteName).</summary>
    public event Action<string, string>? ActorSpriteChanged;

    /// <summary>A world object's visible sprite changed. Args: (objectName, spriteName).</summary>
    public event Action<string, string>? ObjectSpriteChanged;

    /// <summary>A sound should be played. Arg: sfx path (e.g. "sfx/obj_open1.wav").</summary>
    public event Action<string>? SoundTriggered;

    // --- Initialise ----------------------------------------------------------

    /// <summary>
    /// Initialises for a level. Call after GameLogic.LoadLevel().
    /// </summary>
    public void Initialise(
        AnimsFile?  genericAnims,
        AnimsFile?  levelAnims,
        ObjectsFile genericObjects,
        ObjectsFile levelObjects)
    {
        _registry.Load(genericAnims, levelAnims);
        _actorAnimators.Clear();
        _objectAnimators.Clear();
        _objectDefs.Clear();

        // Parse action definitions from objects.xml files
        ParseObjectDefs(genericObjects);
        ParseObjectDefs(levelObjects);

        // Create animators for all actors in the world
        CreateActorAnimators();

        // Create animators for all world objects
        CreateObjectAnimators();

        // Wire up game logic events
        WireEvents();
    }

    // --- Update --------------------------------------------------------------

    /// <summary>Advance all animations. Call every game tick with deltaMs.</summary>
    public void Update(int deltaMs)
    {
        foreach (var animator in _actorAnimators.Values)
            animator.Update(deltaMs);

        foreach (var animator in _objectAnimators.Values)
            animator.Update(deltaMs);
    }

    // --- Direct access --------------------------------------------------------

    public ActorAnimator?  GetActorAnimator(string  actorName)  => _actorAnimators.GetValueOrDefault(actorName);
    public ObjectAnimator? GetObjectAnimator(string objectName) => _objectAnimators.GetValueOrDefault(objectName);

    // --- Setup helpers --------------------------------------------------------

    private void CreateActorAnimators()
    {
        var world = _logic.World!;
        foreach (var actorName in new[]
            { "neighbor", "woody", "mother", "olga", "dog", "chili", "aux", "kid" }
            .Concat(world.Actors.Keys))
        {
            if (!_registry.HasObject(actorName)) continue;

            var def      = _objectDefs.GetValueOrDefault(actorName);
            var animator = new ActorAnimator(actorName, _registry, def);

            animator.SpriteChanged += sprite => ActorSpriteChanged?.Invoke(actorName, sprite);
            animator.SoundTriggered += sfx => SoundTriggered?.Invoke(sfx);
            animator.ActionCompleted += actionName => OnActorActionCompleted(actorName, actionName);

            _actorAnimators[actorName] = animator;

            // Set initial animation: use level.xml value if present (NFH2),
            // otherwise pick a suitable default per actor, falling back to
            // the first available "ms" animation in the registry.
            var levelActor = world.Layout.Rooms
                .SelectMany(r => r.Actors)
                .FirstOrDefault(a => a.Name.Equals(actorName, StringComparison.OrdinalIgnoreCase));

            string initialAnim;
            if (!string.IsNullOrEmpty(levelActor?.Animation))
            {
                initialAnim = levelActor.Animation;
            }
            else
            {
                // Pick first available idle state: prefer ms2 -> ms0 -> ms1 -> ms3 -> first ms*
                var candidates = new[] { "ms2", "ms0", "ms1", "ms3" };
                initialAnim = candidates.FirstOrDefault(c => _registry.GetAnimation(actorName, c) != null)
                    ?? _registry.GetAnimationNames(actorName)
                        .FirstOrDefault(n => n.StartsWith("ms", StringComparison.OrdinalIgnoreCase))
                    ?? "ms0";
            }
            animator.ForceState(initialAnim);
        }
    }

    private void CreateObjectAnimators()
    {
        var world = _logic.World!;
        foreach (var objectName in world.Objects.Keys)
        {
            if (!_registry.HasObject(objectName)) continue;

            var animator = new ObjectAnimator(objectName, _registry);
            animator.SpriteChanged    += sprite => ObjectSpriteChanged?.Invoke(objectName, sprite);
            animator.SoundTriggered   += sfx => SoundTriggered?.Invoke(sfx);
            animator.AnimationCompleted += anim => OnObjectAnimCompleted(objectName, anim);

            _objectAnimators[objectName] = animator;
            animator.SetState("ms"); // default idle state
        }
    }

    private void ParseObjectDefs(ObjectsFile file)
    {
        foreach (var obj in file.Objects)
        {
            var actions = obj.Actions.Select(a => new ActionDef
            {
                Name          = a.Name,
                ActorName     = a.ActorName,
                ActorAnim     = a.ActorAnim,
                ActorNextAnim = a.ActorNextAnim,
                ObjAnim       = a.ObjAnim,
                ObjNextAnim   = a.ObjNextAnim,
                TimeFrames    = a.TimeFrames,
                Noise         = a.Noise,
            }).ToList();

            _objectDefs[obj.Name] = new ObjectDef { Name = obj.Name, Actions = actions };
        }
    }

    // --- Event wiring ---------------------------------------------------------

    private void WireEvents()
    {
        // Behavior triggers -> actor animations
        _logic.BehaviorTriggered += (actorName, behaviorName) =>
        {
            if (_actorAnimators.TryGetValue(actorName, out var animator))
                animator.PlayAction(behaviorName);
        };

        // Neighbor/Mother object interactions (from route steps)
        if (_logic.Brain != null)
        {
            _logic.Brain.ObjectInteraction += (objectName, actionName) =>
                PlayObjectAction("neighbor", objectName, actionName);

            // Notify Brain when action-driven animation completes
            _actorAnimators.GetValueOrDefault("neighbor")!
                .ActionCompleted += _ => _logic.FinishNeighborReaction();
        }

        if (_logic.MotherBrain != null)
        {
            _logic.MotherBrain.ObjectInteraction += (objectName, actionName) =>
                PlayObjectAction("mother", objectName, actionName);
        }

        // ActorController movement state -> animator
        if (_logic.Actors != null)
        {
            _logic.Actors.ActorEnteredRoom += (actorName, _) =>
            {
                if (_actorAnimators.TryGetValue(actorName, out var a))
                    a.SetState(GetIdleAnim(actorName));
            };
        }
    }

    /// <summary>
    /// Plays a coordinated action: actor animation + object animation together.
    /// Looks up action def from the object's ObjectDef.
    /// </summary>
    private void PlayObjectAction(string actorName, string objectName, string actionName)
    {
        var def = _objectDefs.GetValueOrDefault(objectName);
        var action = def?.Actions.FirstOrDefault(a =>
            a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase) &&
            a.ActorName.Equals(actorName, StringComparison.OrdinalIgnoreCase));

        if (action == null) return;

        // Actor animation
        if (_actorAnimators.TryGetValue(actorName, out var actorAnim))
            actorAnim.PlayAction(actionName, action.TimeFrames);

        // Object animation (if objanim specified and not "inv")
        if (!string.IsNullOrEmpty(action.ObjAnim) &&
            !action.ObjAnim.Equals("inv", StringComparison.OrdinalIgnoreCase) &&
            _objectAnimators.TryGetValue(objectName, out var objAnim))
        {
            objAnim.PlayAnim(action.ObjAnim, action.ObjNextAnim, action.TimeFrames);
        }
    }

    private void OnActorActionCompleted(string actorName, string actionName)
    {
        // NeighborBrain / MotherBrain need to know when scripted action finishes
        if (actorName.Equals("neighbor", StringComparison.OrdinalIgnoreCase))
            _logic.Brain?.OnAnimationComplete();
        else if (actorName.Equals("mother", StringComparison.OrdinalIgnoreCase))
            _logic.MotherBrain?.OnAnimationComplete();
    }

    private void OnObjectAnimCompleted(string objectName, string animName)
    {
        // No special logic needed -- ObjectAnimator handles its own state transition
    }

    private string GetIdleAnim(string actorName)
    {
        // Try variants in preference order; fall back to "ms" if none found
        var candidates = actorName switch
        {
            "neighbor" => new[] { "ms0", "ms2", "ms1", "ms3" },
            "woody"    => new[] { "ms1", "ms2", "ms3", "ms0" },
            "mother"   => new[] { "ms2", "ms0", "ms1" },
            _          => new[] { "ms", "ms0", "ms2" }
        };
        return candidates.FirstOrDefault(c => _registry.GetAnimation(actorName, c) != null)
            ?? candidates[0];
    }
}
