using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Controls actor movement through the level's room graph.
///
/// Responsibilities:
///   * Navigate an actor from their current room to a target room via BFS path.
///   * Emit door open/close events as the actor passes through transitions.
///   * Update actor position and animation state during movement.
///   * Respect the path1/path2 walk boundaries within each room.
///
/// The neighbor's AI selects target rooms externally (TriggerSystem or a
/// simple patrol loop). ActorController only handles the how, not the why.
///
/// Movement is tick-based: caller drives Update(deltaMs) each game tick.
/// </summary>
public class ActorController
{
    private readonly WorldState _world;
    private readonly RoomGraph _graph;

    // Per-actor navigation state
    private readonly Dictionary<string, ActorNavState> _navStates = new(StringComparer.OrdinalIgnoreCase);

    public ActorController(WorldState world, RoomGraph graph)
    {
        _world = world;
        _graph = graph;
    }

    // --- Events --------------------------------------------------------------

    /// <summary>Fired when an actor enters a new room. Args: (actorName, roomName).</summary>
    public event Action<string, string>? ActorEnteredRoom;

    /// <summary>Fired when an actor uses a door. Args: (actorName, doorName).</summary>
    public event Action<string, string>? ActorUsedDoor;

    /// <summary>Fired when an actor reaches their destination room.</summary>
    public event Action<string, string>? ActorReachedDestination;

    // --- Navigation commands -------------------------------------------------

    /// <summary>
    /// Orders an actor to navigate to the target room.
    /// Computes path immediately; movement happens on subsequent Update() calls.
    /// If actor is already in target room, fires ActorReachedDestination immediately.
    /// </summary>
    public void NavigateTo(string actorName, string targetRoom)
    {
        var actor = GetActor(actorName);
        if (actor == null) return;

        if (actor.CurrentRoom.Equals(targetRoom, StringComparison.OrdinalIgnoreCase))
        {
            ActorReachedDestination?.Invoke(actorName, targetRoom);
            return;
        }

        var path = _graph.FindPath(actor.CurrentRoom, targetRoom);
        if (path.Count == 0) return; // no path -- actor stays

        _navStates[actorName] = new ActorNavState(
            targetRoom: targetRoom,
            path: new Queue<RoomEdge>(path),
            currentEdge: null);
    }

    /// <summary>Stops the actor's current navigation immediately.</summary>
    public void Stop(string actorName)
    {
        _navStates.Remove(actorName);
        var actor = GetActor(actorName);
        if (actor != null)
        {
            actor.IsTransitioning = false;
            actor.CurrentAnimation = "mg0"; // idle
        }
    }

    // --- Update --------------------------------------------------------------

    /// <summary>
    /// Advances all actor movements by deltaMs milliseconds.
    /// Called once per game tick by the main game loop.
    /// </summary>
    public void Update(int deltaMs)
    {
        foreach (var actorName in _navStates.Keys.ToList())
        {
            var actor = GetActor(actorName);
            if (actor == null) continue;

            AdvanceNavigation(actorName, actor, deltaMs);
        }
    }

    // --- Private movement logic -----------------------------------------------

    private void AdvanceNavigation(string actorName, ActorInstance actor, int deltaMs)
    {
        if (!_navStates.TryGetValue(actorName, out var nav)) return;

        // If not currently transitioning, start next edge
        if (nav.CurrentEdge == null)
        {
            if (nav.Path.Count == 0)
            {
                // Reached destination
                _navStates.Remove(actorName);
                actor.IsTransitioning = false;
                actor.CurrentAnimation = "mg0";
                ActorReachedDestination?.Invoke(actorName, nav.TargetRoom);
                return;
            }

            nav.CurrentEdge = nav.Path.Dequeue();
            actor.IsTransitioning = true;
            actor.CurrentAnimation = GetWalkAnimation(actor);

            // Notify door usage
            ActorUsedDoor?.Invoke(actorName, nav.CurrentEdge.DoorIn);
        }

        // Advance transition timer
        nav.TransitionElapsedMs += deltaMs;

        // Transition duration: proportional to cost (1000 cost ~= 500ms base)
        int transitionMs = nav.CurrentEdge.Cost / 2;

        if (nav.TransitionElapsedMs >= transitionMs)
        {
            // Complete the room transition
            actor.CurrentRoom = nav.CurrentEdge.ToRoom;
            actor.IsTransitioning = false;
            nav.TransitionElapsedMs = 0;
            nav.CurrentEdge = null;

            ActorEnteredRoom?.Invoke(actorName, actor.CurrentRoom);

            // Update facing direction based on position change
            UpdateRoomPosition(actor);
        }
        else
        {
            // Interpolate position during transition (simplified linear)
            float t = (float)nav.TransitionElapsedMs / transitionMs;
            UpdateTransitionPosition(actor, nav.CurrentEdge, t);
        }
    }

    private static string GetWalkAnimation(ActorInstance actor)
    {
        // mg = move gentle, mr = move run
        // Neighbor typically uses mr (run), Woody uses mg (sneak)
        return actor.Name.Equals("neighbor", StringComparison.OrdinalIgnoreCase)
            ? "mr0"
            : "mg0";
    }

    private void UpdateRoomPosition(ActorInstance actor)
    {
        // Place actor at the walk path midpoint of the new room
        var room = _world.Layout.Rooms
            .FirstOrDefault(r => r.Name.Equals(actor.CurrentRoom, StringComparison.OrdinalIgnoreCase));

        if (room == null) return;

        int midX = room.Offset.X + (room.Path1.X + room.Path2.X) / 2;
        int y = room.Offset.Y + room.Path1.Y; // path Y is the walk baseline
        actor.Position = new NfhPoint(midX, y);

        // Face toward room center
        actor.FacingDirection = 1;
    }

    private void UpdateTransitionPosition(ActorInstance actor, RoomEdge edge, float t)
    {
        // Linearly interpolate between the midpoints of the two rooms
        var fromRoom = _world.Layout.Rooms
            .FirstOrDefault(r => r.Name.Equals(edge.FromRoom, StringComparison.OrdinalIgnoreCase));
        var toRoom   = _world.Layout.Rooms
            .FirstOrDefault(r => r.Name.Equals(edge.ToRoom,   StringComparison.OrdinalIgnoreCase));

        if (fromRoom == null || toRoom == null) return;

        float fromX = fromRoom.Offset.X + (fromRoom.Path1.X + fromRoom.Path2.X) / 2.0f;
        float fromY = fromRoom.Offset.Y +  fromRoom.Path1.Y;
        float toX   = toRoom.Offset.X   + (toRoom.Path1.X   + toRoom.Path2.X)   / 2.0f;
        float toY   = toRoom.Offset.Y   +  toRoom.Path1.Y;

        actor.Position = new NfhPoint(
            (int)(fromX + (toX - fromX) * t),
            (int)(fromY + (toY - fromY) * t));

        actor.FacingDirection = toX > fromX ? 1 : -1;
    }

    private ActorInstance? GetActor(string name) =>
        _world.GetActor(name, includeOlga: true); // ActorController moves all actors including Olga
}

/// <summary>Per-actor navigation state (mutable, internal to ActorController).</summary>
internal class ActorNavState
{
    public string TargetRoom { get; }
    public Queue<RoomEdge> Path { get; }
    public RoomEdge? CurrentEdge { get; set; }
    public int TransitionElapsedMs { get; set; }

    public ActorNavState(string targetRoom, Queue<RoomEdge> path, RoomEdge? currentEdge)
    {
        TargetRoom = targetRoom;
        Path = path;
        CurrentEdge = currentEdge;
    }
}