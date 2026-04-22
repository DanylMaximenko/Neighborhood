using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// The neighbor's behavioral state machine.
///
/// States:
///   PATROL -- executes route steps in a loop.
///            Each step: navigate to room -> optionally interact with object ->
///            dwell for duration (or wait for animation) -> next step.
///
///   REACT  -- discovered a prank.
///            Saves current step index, navigates to trick room,
///            plays reaction, resumes from saved step.
///
///   ALERT  -- spotted Woody (or Mother spotted Woody).
///            Navigates to Woody, beats him.
///            If CanEscape=true: Woody can flee to another room.
///
/// Blind steps:
///   When the current route step has Blind=true, WoodySpotted triggers
///   are suppressed -- the actor is "absorbed" in an activity (sleeping, etc.)
///   and does not notice Woody until the step completes.
/// </summary>
public class NeighborBrain
{
    private readonly WorldState       _world;
    private readonly ActorController  _actorCtrl;
    private readonly RouteData        _route;
    private readonly NeighborSettings _settings;

    public NeighborState State { get; private set; } = NeighborState.Patrol;

    // --- Patrol state --------------------------------------------------------
    private int  _routeIndex;
    private int  _waitElapsedMs;
    private bool _waitingInRoom;     // arrived, performing action / dwelling
    private bool _currentStepBlind; // current step is blind (actor can't see Woody)

    // --- React state ---------------------------------------------------------
    private InstalledTrick? _currentTrick;
    private int             _interruptedRouteIndex;

    // --- Alert state ---------------------------------------------------------
    private int  _alertElapsedMs;
    private bool _catchSequenceStarted;

    public NeighborBrain(
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

    public event Action<string>?              EnteredRoom;

    /// <summary>
    /// Fired when the neighbor starts interacting with an object on arrival.
    /// Args: (objectName, actionName).
    /// Rendering layer plays the corresponding animation.
    /// </summary>
    public event Action<string, string>?      ObjectInteraction;

    /// <summary>Fired when an object interaction animation completes.</summary>

    public event Action<InstalledTrick, string>? ReactingToTrick;
    public event Action?                      ResumedPatrol;
    public event Action?                      AlertStarted;
    public event Action?                      CatchSequenceStarted;
    public event Action?                      WoodyCaught;
    public event Action?                      WoodyEscaped;

    // --- Blind state ---------------------------------------------------------

    /// <summary>
    /// True when the current patrol step is blind -- neighbor cannot spot Woody.
    /// TriggerSystem checks this before firing WoodySpotted.
    /// </summary>
    public bool IsBlind => State == NeighborState.Patrol && _currentStepBlind;

    // --- External triggers ----------------------------------------------------

    /// <summary>
    /// Called by TriggerSystem when neighbor discovers a prank.
    /// Ignored if currently blind (absorbed in activity).
    /// </summary>
    public void OnTrickDiscovered(InstalledTrick trick, string behaviorName)
    {
        if (State == NeighborState.Alert) return;
        // During a blind step, neighbor doesn't react to pranks either
        if (IsBlind) return;

        _interruptedRouteIndex = _routeIndex;
        _waitingInRoom         = false;
        _currentStepBlind      = false;
        _currentTrick          = trick;
        _waitElapsedMs         = 0;

        State = NeighborState.React;
        _actorCtrl.NavigateTo("neighbor", trick.RoomName);
        ReactingToTrick?.Invoke(trick, behaviorName);
    }

    /// <summary>
    /// Called by TriggerSystem when neighbor spots Woody.
    /// Ignored if currently blind.
    /// </summary>
    public void OnWoodySpotted()
    {
        if (State == NeighborState.Alert) return;
        if (IsBlind) return;

        State                 = NeighborState.Alert;
        _alertElapsedMs       = 0;
        _catchSequenceStarted = false;
        _currentStepBlind     = false;

        _actorCtrl.NavigateTo("neighbor", _world.Woody.CurrentRoom);
        AlertStarted?.Invoke();
    }

    /// <summary>
    /// Called by rendering layer when a reaction animation completes.
    /// Resumes patrol from the interrupted step.
    /// </summary>
    public void FinishReaction()
    {
        if (State != NeighborState.React) return;

        State             = NeighborState.Patrol;
        _routeIndex       = _interruptedRouteIndex;
        _waitingInRoom    = false;
        _currentStepBlind = false;
        _currentTrick     = null;

        if (!_route.IsEmpty)
            _actorCtrl.NavigateTo("neighbor", _route.Steps[_routeIndex].Room);

        ResumedPatrol?.Invoke();
    }

    /// <summary>
    /// Called by rendering layer when a step's object interaction animation ends.
    /// If duration=0 (auto), this advances to the next step.
    /// </summary>
    public void OnAnimationComplete()
    {
        if (State != NeighborState.Patrol || !_waitingInRoom) return;
        var step = _route.Steps[_routeIndex];
        // Auto-advance: only if duration=0 (wait-for-anim mode)
        if (step.Duration == 0)
            AdvanceToNextStep();
    }

    // --- Update --------------------------------------------------------------

    public void Update(int deltaMs)
    {
        switch (State)
        {
            case NeighborState.Patrol: UpdatePatrol(deltaMs); break;
            case NeighborState.React:  break; // animation-driven
            case NeighborState.Alert:  UpdateAlert(deltaMs);  break;
        }
    }

    private void UpdatePatrol(int deltaMs)
    {
        if (_route.IsEmpty || !_waitingInRoom) return;

        var step = _route.Steps[_routeIndex];
        if (step.Duration == 0) return; // waiting for animation (OnAnimationComplete)

        _waitElapsedMs += deltaMs;
        if (_waitElapsedMs >= step.Duration)
            AdvanceToNextStep();
    }

    private void AdvanceToNextStep()
    {
        _waitElapsedMs    = 0;
        _waitingInRoom    = false;
        _currentStepBlind = false;

        _routeIndex = (_routeIndex + 1) % _route.Steps.Count;
        var nextStep = _route.Steps[_routeIndex];
        _actorCtrl.NavigateTo("neighbor", nextStep.Room);
    }

    private void UpdateAlert(int deltaMs)
    {
        if (!_catchSequenceStarted) return;

        _alertElapsedMs += deltaMs;

        if (_settings.CanEscape &&
            !_world.Woody.CurrentRoom.Equals(
                _world.Neighbor.CurrentRoom, StringComparison.OrdinalIgnoreCase))
        {
            State = NeighborState.Patrol;
            _catchSequenceStarted = false;
            WoodyEscaped?.Invoke();
            ResumePatrolFromCurrent();
            return;
        }

        if (_alertElapsedMs >= _settings.BeatingDurationMs)
        {
            State = NeighborState.Patrol;
            _catchSequenceStarted = false;
            WoodyCaught?.Invoke();
        }
    }

    private void ResumePatrolFromCurrent()
    {
        _waitingInRoom    = false;
        _currentStepBlind = false;
        if (!_route.IsEmpty)
            _actorCtrl.NavigateTo("neighbor", _route.Steps[_routeIndex].Room);
        ResumedPatrol?.Invoke();
    }

    // --- ActorController callback ---------------------------------------------

    private void OnActorReachedDestination(string actorName, string roomName)
    {
        if (!actorName.Equals("neighbor", StringComparison.OrdinalIgnoreCase)) return;

        switch (State)
        {
            case NeighborState.Patrol:
                _waitingInRoom = true;
                _waitElapsedMs = 0;
                EnteredRoom?.Invoke(roomName);

                var step = _route.Steps[_routeIndex];
                _currentStepBlind = step.Blind;

                // Fire object interaction if this step has one
                if (step.Object != null && step.Action != null)
                    ObjectInteraction?.Invoke(step.Object, step.Action);
                break;

            case NeighborState.React:
                EnteredRoom?.Invoke(roomName);
                break;

            case NeighborState.Alert:
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

    // --- Startup -------------------------------------------------------------

    public void StartPatrol()
    {
        if (_route.IsEmpty) return;
        State             = NeighborState.Patrol;
        _routeIndex       = 0;
        _waitingInRoom    = false;
        _currentStepBlind = false;
        _waitElapsedMs    = 0;
        _actorCtrl.NavigateTo("neighbor", _route.Steps[0].Room);
    }
}

public enum NeighborState { Patrol, React, Alert }

public class NeighborSettings
{
    /// <summary>
    /// If true, Woody can escape during the ALERT catch window.
    /// </summary>
    public bool CanEscape { get; set; } = true;

    /// <summary>How long (ms) the beating plays before WoodyCaught fires.</summary>
    public int BeatingDurationMs { get; set; } = 3000;

    public PatrolMode PatrolMode { get; set; } = PatrolMode.Fixed;
}

public enum PatrolMode { Fixed, Random }
