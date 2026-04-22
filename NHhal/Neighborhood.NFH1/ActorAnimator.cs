using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Manages the animation state of a single actor (neighbor, woody, mother, etc.)
///
/// Resolves action names from objects.xml to animation names in anims.xml:
///   action.actoranim  -> animation to play on the actor
///   action.actornextanim -> animation to transition to after completion
///
/// The actor has a "current state" (e.g. "ms0", "mg1") which is the
/// looping idle animation between actions.
///
/// Flow:
///   1. PlayAction("slip1") -> looks up action in ObjectDef -> plays actoranim
///   2. On completion -> transitions to actornextanim
///   3. actornextanim is the new idle state (e.g. "ms0")
///
/// Movement animations (mg0/mg1/mg2/mg3, mr0/mr1) are driven by
/// ActorController and set directly via SetMovementState().
/// </summary>
public class ActorAnimator
{
    private readonly string           _actorName;
    private readonly AnimationRegistry _registry;
    private readonly ObjectDef?        _objectDef;   // from objects.xml
    private readonly AnimationPlayer   _player;

    private string _currentAnimName = "ms0"; // current idle/state animation
    private string _nextAnimName    = "ms0"; // transition target after current ends

    public ActorAnimator(
        string            actorName,
        AnimationRegistry registry,
        ObjectDef?        objectDef = null)
    {
        _actorName  = actorName;
        _registry   = registry;
        _objectDef  = objectDef;
        _player     = new AnimationPlayer();

        _player.Completed      += OnAnimationCompleted;
        _player.FrameChanged   += OnFrameChanged;
        _player.SoundTriggered += sfx => SoundTriggered?.Invoke(sfx);
    }

    // --- Events --------------------------------------------------------------

    /// <summary>Current frame sprite changed. Arg: sprite filename.</summary>
    public event Action<string>? SpriteChanged;

    /// <summary>An action animation completed and transitioned to next state.</summary>
    public event Action<string>? ActionCompleted; // arg: action name that completed

    /// <summary>A sound should be played this frame.</summary>
    public event Action<string>? SoundTriggered;

    // --- State ---------------------------------------------------------------

    public string CurrentAnimName => _currentAnimName;
    public string CurrentSpriteName => _player.CurrentSprite;
    public bool   IsPlayingAction { get; private set; }

    // --- Playback ------------------------------------------------------------

    /// <summary>
    /// Plays a named action from objects.xml (e.g. "slip1", "enter", "eat_candy").
    /// Looks up actoranim and actornextanim from the actor's ObjectDef.
    /// Falls back to playing the action name directly as an animation name.
    /// </summary>
    public void PlayAction(string actionName, int timeFrames = 0)
    {
        var action = _objectDef?.Actions
            .FirstOrDefault(a =>
                a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase) &&
                a.ActorName.Equals(_actorName, StringComparison.OrdinalIgnoreCase));

        string animName     = action?.ActorAnim ?? actionName;
        string nextAnimName = action?.ActorNextAnim ?? _currentAnimName;

        // "inv" means actor is invisible during this action (object does the animation)
        if (animName.Equals("inv", StringComparison.OrdinalIgnoreCase))
        {
            // Transition immediately to next state
            _currentAnimName  = nextAnimName;
            _nextAnimName     = nextAnimName;
            IsPlayingAction   = false;
            PlayState(_currentAnimName);
            ActionCompleted?.Invoke(actionName);
            return;
        }

        _nextAnimName   = nextAnimName;
        IsPlayingAction = true;

        int frames = timeFrames > 0 ? timeFrames
                   : (action?.TimeFrames ?? 0);

        PlayAnim(animName, frames);
    }

    /// <summary>
    /// Directly sets the actor's idle/state animation (e.g. "ms0", "mg1", "mr0").
    /// Used by ActorController for movement states.
    /// </summary>
    public void SetState(string animName)
    {
        if (IsPlayingAction) return; // don't interrupt action mid-play
        _currentAnimName = animName;
        _nextAnimName    = animName;
        PlayState(animName);
    }

    /// <summary>
    /// Forces a state regardless of whether an action is playing.
    /// Used for ALERT/REACT interrupts.
    /// </summary>
    public void ForceState(string animName)
    {
        IsPlayingAction  = false;
        _currentAnimName = animName;
        _nextAnimName    = animName;
        PlayState(animName);
    }

    // --- Update --------------------------------------------------------------

    public void Update(int deltaMs) => _player.Update(deltaMs);

    // --- Private -------------------------------------------------------------

    private void PlayAnim(string animName, int timeFrames = 0)
    {
        var anim = _registry.GetAnimation(_actorName, animName);
        if (anim == null) return;

        _currentAnimName = animName;
        _player.FrameChanged -= OnFrameChanged;
        _player.FrameChanged += OnFrameChanged;
        _player.Play(anim, timeFrames);
    }

    private void PlayState(string animName)
    {
        var anim = _registry.GetAnimation(_actorName, animName);
        if (anim == null) return;

        _player.FrameChanged -= OnFrameChanged;
        _player.FrameChanged += OnFrameChanged;
        _player.Play(anim, 0);
    }

    private void OnFrameChanged(string sprite) => SpriteChanged?.Invoke(sprite);

    private void OnAnimationCompleted()
    {
        if (IsPlayingAction)
        {
            var completedAction = _currentAnimName;
            IsPlayingAction  = false;
            _currentAnimName = _nextAnimName;
            PlayState(_currentAnimName);
            ActionCompleted?.Invoke(completedAction);
        }
        else
        {
            // State animation completed (oneshot idle) -- restart it
            PlayState(_currentAnimName);
        }
    }
}

/// <summary>Parsed action definition from objects.xml (in-memory, not XML model).</summary>
public class ObjectDef
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ActionDef> Actions { get; init; } = [];
}

public class ActionDef
{
    public string Name          { get; init; } = string.Empty;
    public string ActorName     { get; init; } = string.Empty;
    public string ActorAnim     { get; init; } = string.Empty;
    public string ActorNextAnim { get; init; } = string.Empty;

    /// <summary>Object animation name (when actor is "inv").</summary>
    public string ObjAnim       { get; init; } = string.Empty;
    public string ObjNextAnim   { get; init; } = string.Empty;

    /// <summary>0 = auto (play full animation), N = hold N frames.</summary>
    public int TimeFrames       { get; init; }
    public int Noise            { get; init; }
}
