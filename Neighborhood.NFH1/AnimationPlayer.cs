using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Plays a single animation sequence at a fixed frame rate.
///
/// Frame rate: 12 FPS = 83ms per frame (matches original game).
///
/// time attribute in objects.xml:
///   "auto"        -> play all frames once, then fire Completed
///   N (integer)   -> hold for exactly N frames total, then fire Completed
///                   (the animation loops internally if N > frame count)
///   0             -> play one frame (instant) then fire Completed
///
/// Loop types from anims.xml:
///   "oneshot"     -> play once, stop on last frame, fire Completed
///   "loop"        -> repeat indefinitely, never fires Completed
/// </summary>
public class AnimationPlayer
{
    public const int FramesPerSecond = 12;
    public const int MsPerFrame      = 1000 / FramesPerSecond; // 83ms

    private Animation?  _animation;
    private int         _currentFrame;
    private int         _elapsedMs;
    private int         _totalFramesTarget; // -1 = use animation length
    private int         _framesPlayed;
    private bool        _completed;
    private bool        _looping;

    // --- Events --------------------------------------------------------------

    /// <summary>Fired when the current frame changes. Arg: sprite filename to render.</summary>
    public event Action<string>? FrameChanged;

    /// <summary>
    /// Fired when animation completes (oneshot reached end, or timed frame count elapsed).
    /// Not fired for looping animations.
    /// </summary>
    public event Action? Completed;

    /// <summary>
    /// If the animation has an sfx on the current frame, fired with the sound filename.
    /// </summary>
    public event Action<string>? SoundTriggered;

    // --- State ---------------------------------------------------------------

    public bool   IsPlaying   => _animation != null && !_completed;
    public bool   IsCompleted => _completed;
    public int    CurrentFrame => _currentFrame;
    public string CurrentSprite => _animation?.Frames.Count > 0
        ? _animation.Frames[_currentFrame].Gfx
        : string.Empty;

    // --- Control -------------------------------------------------------------

    /// <summary>
    /// Starts playing an animation.
    /// </summary>
    /// <param name="animation">Animation definition from AnimationRegistry.</param>
    /// <param name="timeFrames">
    ///   Frame count from objects.xml action time attribute.
    ///   0  = auto (play all frames once).
    ///   N  = play for exactly N frames (loops animation if needed).
    /// </param>
    public void Play(Animation animation, int timeFrames = 0)
    {
        _animation          = animation;
        _currentFrame       = 0;
        _elapsedMs          = 0;
        _framesPlayed       = 0;
        _completed          = false;
        _looping            = animation.AnimType.Equals("loop", StringComparison.OrdinalIgnoreCase);
        _totalFramesTarget  = timeFrames > 0 ? timeFrames : -1;

        if (_animation.Frames.Count == 0)
        {
            // Empty animation -- complete immediately
            _completed = true;
            Completed?.Invoke();
            return;
        }

        FireCurrentFrame();
    }

    /// <summary>Stops playback immediately without firing Completed.</summary>
    public void Stop()
    {
        _animation = null;
        _completed = true;
    }

    // --- Update --------------------------------------------------------------

    /// <summary>Advance animation by deltaMs. Call every game tick.</summary>
    public void Update(int deltaMs)
    {
        if (_animation == null || _completed) return;
        if (_animation.Frames.Count == 0) return;

        _elapsedMs += deltaMs;

        while (_elapsedMs >= MsPerFrame)
        {
            _elapsedMs    -= MsPerFrame;
            _framesPlayed += 1;

            bool reachedEnd = _currentFrame >= _animation.Frames.Count - 1;

            if (reachedEnd)
            {
                if (_looping)
                {
                    // Loop: restart from frame 0
                    _currentFrame = 0;
                }
                else
                {
                    // Oneshot: check if we've hit the frame target
                    if (_totalFramesTarget > 0 && _framesPlayed < _totalFramesTarget)
                    {
                        // Timed mode: keep showing last frame until target
                        // (don't advance, just count frames)
                    }
                    else
                    {
                        // Auto or timed target reached -> complete
                        _completed = true;
                        Completed?.Invoke();
                        return;
                    }
                }
            }
            else
            {
                // Check timed completion before advancing
                if (_totalFramesTarget > 0 && _framesPlayed >= _totalFramesTarget)
                {
                    _completed = true;
                    Completed?.Invoke();
                    return;
                }

                _currentFrame++;
            }

            FireCurrentFrame();
        }
    }

    // --- Helpers -------------------------------------------------------------

    private void FireCurrentFrame()
    {
        if (_animation == null || _animation.Frames.Count == 0) return;

        var frame = _animation.Frames[_currentFrame];
        FrameChanged?.Invoke(frame.Gfx);

        if (!string.IsNullOrEmpty(frame.Sfx))
            SoundTriggered?.Invoke(frame.Sfx);
    }
}
