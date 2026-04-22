using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Manages animation state for a world object (sofa, bed, microwave, door, etc.)
///
/// Objects have their own animation states separate from actors.
/// When an action plays:
///   action.objanim     -> animation to play on the object
///   action.objnextanim -> object's new state after completion
///
/// Some objects loop in their "ms" (main state) animation indefinitely.
/// Others have "oneshot" animations that complete and transition.
/// </summary>
public class ObjectAnimator
{
    private readonly string           _objectName;
    private readonly AnimationRegistry _registry;
    private readonly AnimationPlayer   _player;

    private string _currentAnimName = "ms";
    private string _nextAnimName    = "ms";

    public ObjectAnimator(string objectName, AnimationRegistry registry)
    {
        _objectName = objectName;
        _registry   = registry;
        _player     = new AnimationPlayer();

        _player.Completed      += OnAnimationCompleted;
        _player.SoundTriggered += sfx => SoundTriggered?.Invoke(sfx);
        _player.FrameChanged   += sprite => SpriteChanged?.Invoke(sprite);
    }

    // --- Events --------------------------------------------------------------

    public event Action<string>? SpriteChanged;
    public event Action<string>? SoundTriggered;
    public event Action<string>? AnimationCompleted; // arg: animation name that ended

    // --- State ---------------------------------------------------------------

    public string CurrentAnimName => _currentAnimName;
    public string CurrentSpriteName => _player.CurrentSprite;

    // --- Playback ------------------------------------------------------------

    /// <summary>
    /// Plays an object animation by name (objanim from objects.xml).
    /// nextAnimName = objnextanim (state after completion).
    /// </summary>
    public void PlayAnim(string animName, string? nextAnimName = null, int timeFrames = 0)
    {
        _nextAnimName = nextAnimName ?? _currentAnimName;

        var anim = _registry.GetAnimation(_objectName, animName);
        if (anim == null)
        {
            // Animation not found -- transition to next state immediately
            _currentAnimName = _nextAnimName;
            AnimationCompleted?.Invoke(animName);
            return;
        }

        _currentAnimName = animName;
        _player.Play(anim, timeFrames);
    }

    /// <summary>Sets the object's idle state animation (e.g. "ms", "sleep", "neighbor_sit").</summary>
    public void SetState(string animName)
    {
        _currentAnimName = animName;
        _nextAnimName    = animName;

        var anim = _registry.GetAnimation(_objectName, animName);
        if (anim != null)
            _player.Play(anim, 0);
    }

    // --- Update --------------------------------------------------------------

    public void Update(int deltaMs) => _player.Update(deltaMs);

    // --- Private -------------------------------------------------------------

    private void OnAnimationCompleted()
    {
        var completed    = _currentAnimName;
        _currentAnimName = _nextAnimName;
        _nextAnimName    = _currentAnimName; // stay in new state

        var nextAnim = _registry.GetAnimation(_objectName, _currentAnimName);
        if (nextAnim != null)
            _player.Play(nextAnim, 0);

        AnimationCompleted?.Invoke(completed);
    }
}
