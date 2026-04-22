using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Mother's behavioral state machine (NFH2, ship2 onwards).
///
/// Mother behaviour rules:
///   1. Follows a scripted route with object interactions (like Neighbor).
///   2. Has "blind" steps -- sleeping, diving, absorbed -- during which she
///      does NOT see Woody and does NOT react.
///   3. Does NOT react to pranks (no REACT state).
///   4. Reacts to Woody only when NOT in a blind step (ALERT).
///   5. Special: if Woody fails a mini-game while in the same room as Mother,
///      she wakes up and beats him (even if in blind step).
///      Call OnMiniGameFailedNearMother() from GameLogicWithMiniGames.
///   6. If Neighbor is present when Mother is beating Woody, Neighbor laughs.
///      This is handled externally in GameLogicWithMiniGames.
/// </summary>
public class MotherBrain
{
    private readonly WorldState       _world;
    private readonly ActorController  _actorCtrl;
    private readonly RouteData        _route;
    private readonly NeighborSettings _settings;

    public MotherState State { get; private set; } = MotherState.Patrol;

    // Patrol
    private int  _routeIndex;
    private int  _waitElapsedMs;
    private bool _waitingInRoom;
    private bool _currentStepBlind;

    // Alert
    private int  _alertElapsedMs;
    private bool _catchSequenceStarted;

    public MotherBrain(
        WorldState       world,
        ActorController  actorCtrl,
        RouteData        route,
        NeighborSettings settings)
    {
        _world     = world;
        _actorCtrl = actorCtrl;
        _route     = route;
        _settings  = settings;

        _actorCtrl.ActorReachedDestination += OnActorReachedDestination;
    }

    // --- Events --------------------------------------------------------------

    public event Action<string>?         EnteredRoom;
    public event Action<string, string>? ObjectInteraction;     // (object, action)
    public event Action?                 AlertStarted;
    public event Action?                 CatchSequenceStarted;
    public event Action?                 WoodyCaught;
    public event Action?                 WoodyEscaped;

    /// <summary>
    /// Fired when Mother wakes from a blind step due to mini-game failure near her.
    /// Rendering layer plays wakeup animation before beating.
    /// </summary>
    public event Action? WokenByMiniGameFailure;

    // --- Blind state ---------------------------------------------------------

    /// <summary>
    /// True when Mother is in a blind step (sleeping, diving, etc.).
    /// Normal Woody detection is suppressed. Only mini-game failure can wake her.
    /// </summary>
    public bool IsBlind => State == MotherState.Patrol && _currentStepBlind;

    // --- External triggers ----------------------------------------------------

    /// <summary>
    /// Called by TriggerSystem when Mother spots Woody (fight behavior, NFH2).
    /// Ignored if Mother is currently in a blind step.
    /// </summary>
    public void OnWoodySpotted()
    {
        if (State == MotherState.Alert) return;
        if (IsBlind) return; // sleeping/absorbed -- doesn't see Woody

        StartAlert();
    }

    /// <summary>
    /// Called by GameLogicWithMiniGames when Woody fails a mini-game
    /// while in the same room as Mother.
    /// Wakes Mother regardless of blind state.
    /// </summary>
    public void OnMiniGameFailedNearMother()
    {
        if (State == MotherState.Alert) return;

        _currentStepBlind = false; // forced awake
        WokenByMiniGameFailure?.Invoke();
        StartAlert();
    }

    /// <summary>
    /// Called by rendering layer when animation completes (duration=0 step).
    /// </summary>
    public void OnAnimationComplete()
    {
        if (State != MotherState.Patrol || !_waitingInRoom) return;
        var step = _route.Steps[_routeIndex];
        if (step.Duration == 0)
            AdvanceToNextStep();
    }

    // --- Update --------------------------------------------------------------

    public void Update(int deltaMs)
    {
        switch (State)
        {
            case MotherState.Patrol: UpdatePatrol(deltaMs); break;
            case MotherState.Alert:  UpdateAlert(deltaMs);  break;
        }
    }

    private void UpdatePatrol(int deltaMs)
    {
        if (_route.IsEmpty || !_waitingInRoom) return;

        var step = _route.Steps[_routeIndex];
        if (step.Duration == 0) return;

        _waitElapsedMs += deltaMs;
        if (_waitElapsedMs >= step.Duration)
            AdvanceToNextStep();
    }

    private void AdvanceToNextStep()
    {
        _waitElapsedMs    = 0;
        _waitingInRoom    = false;
        _currentStepBlind = false;
        _routeIndex       = (_routeIndex + 1) % _route.Steps.Count;
        _actorCtrl.NavigateTo("mother", _route.Steps[_routeIndex].Room);
    }

    private void UpdateAlert(int deltaMs)
    {
        if (!_catchSequenceStarted) return;

        _alertElapsedMs += deltaMs;

        if (_settings.CanEscape &&
            !_world.Woody.CurrentRoom.Equals(
                _world.Mother!.CurrentRoom, StringComparison.OrdinalIgnoreCase))
        {
            State = MotherState.Patrol;
            _catchSequenceStarted = false;
            WoodyEscaped?.Invoke();
            ResumePatrol();
            return;
        }

        if (_alertElapsedMs >= _settings.BeatingDurationMs)
        {
            State = MotherState.Patrol;
            _catchSequenceStarted = false;
            WoodyCaught?.Invoke();
        }
    }

    private void StartAlert()
    {
        State                 = MotherState.Alert;
        _alertElapsedMs       = 0;
        _catchSequenceStarted = false;
        _actorCtrl.NavigateTo("mother", _world.Woody.CurrentRoom);
        AlertStarted?.Invoke();
    }

    private void ResumePatrol()
    {
        _waitingInRoom    = false;
        _currentStepBlind = false;
        if (!_route.IsEmpty)
            _actorCtrl.NavigateTo("mother", _route.Steps[_routeIndex].Room);
    }

    public void StartPatrol()
    {
        if (_route.IsEmpty || _world.Mother == null) return;
        State             = MotherState.Patrol;
        _routeIndex       = 0;
        _waitingInRoom    = false;
        _currentStepBlind = false;
        _waitElapsedMs    = 0;
        _actorCtrl.NavigateTo("mother", _route.Steps[0].Room);
    }

    // --- ActorController callback ---------------------------------------------

    private void OnActorReachedDestination(string actorName, string roomName)
    {
        if (!actorName.Equals("mother", StringComparison.OrdinalIgnoreCase)) return;

        switch (State)
        {
            case MotherState.Patrol:
                _waitingInRoom    = true;
                _waitElapsedMs    = 0;
                EnteredRoom?.Invoke(roomName);

                var step = _route.Steps[_routeIndex];
                _currentStepBlind = step.Blind;

                if (step.Object != null && step.Action != null)
                    ObjectInteraction?.Invoke(step.Object, step.Action);
                break;

            case MotherState.Alert:
                if (!_catchSequenceStarted)
                {
                    _catchSequenceStarted = true;
                    _alertElapsedMs       = 0;
                    CatchSequenceStarted?.Invoke();
                    if (!_settings.CanEscape)
                        WoodyCaught?.Invoke();
                }
                break;
        }
    }
}

public enum MotherState { Patrol, Alert }
