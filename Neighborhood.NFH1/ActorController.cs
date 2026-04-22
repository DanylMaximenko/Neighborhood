using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

public class ActorController
{
    private readonly WorldState _world;
    private readonly RoomGraph _graph;

    private readonly Dictionary<string, ActorNavState> _navStates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fired when an actor starts moving toward a new room. Args: (actorName, walkAnimName).</summary>
    public event Action<string, string>? ActorStartedMoving;

    /// <summary>Fired when an actor arrives in a new room. Args: (actorName, roomName).</summary>
    public event Action<string, string>? ActorEnteredRoom;

    /// <summary>Fired when an actor reaches their final destination. Args: (actorName, roomName).</summary>
    public event Action<string, string>? ActorReachedDestination;

    public ActorController(WorldState world, RoomGraph graph)
    {
        _world = world;
        _graph = graph;
    }

    public void NavigateTo(string actorName, string targetRoom)
    {
        var actor = GetActor(actorName);
        if (actor == null) return;

        var path = _graph.FindPath(actor.CurrentRoom, targetRoom);
        if (path.Count == 0) return;

        _navStates[actorName] = new ActorNavState(targetRoom, new Queue<RoomEdge>(path));
    }

    public void Update(int deltaMs)
    {
        foreach (var actorName in _navStates.Keys.ToList())
        {
            var actor = GetActor(actorName);
            if (actor == null) continue;

            Advance(actorName, actor, deltaMs);
        }
    }

    private void Advance(string name, ActorInstance actor, int deltaMs)
    {
        var nav = _navStates[name];

        if (nav.Edge == null)
        {
            if (nav.Path.Count == 0)
            {
                _navStates.Remove(name);
                ActorReachedDestination?.Invoke(name, actor.CurrentRoom);
                return;
            }

            nav.Edge = nav.Path.Dequeue();
            nav.Time = 0;

            // Notify: actor started walking
            var walkAnim = GetWalkAnimation(actor);
            ActorStartedMoving?.Invoke(name, walkAnim);
        }

        nav.Time += deltaMs;
        float t = Math.Clamp(nav.Time / 300f, 0f, 1f);

        UpdateTransition(actor, nav.Edge, t);

        if (t >= 1f)
        {
            actor.CurrentRoom = nav.Edge.ToRoom;
            nav.Edge = null;

            UpdateRoomPosition(actor);

            // Notify: actor entered room
            ActorEnteredRoom?.Invoke(name, actor.CurrentRoom);
        }
    }

    // Door transition movement
    private static void UpdateTransition(ActorInstance actor, RoomEdge edge, float t)
    {
        var start = edge.FromPosition;
        var end = edge.ToPosition;

        float x = start.X + (end.X - start.X) * t;
        float y = start.Y + (end.Y - start.Y) * t;

        actor.Position = new NfhPoint((int)x, (int)y);

        float dx = end.X - start.X;
        float dy = end.Y - start.Y;

        if (Math.Abs(dx) > Math.Abs(dy))
            actor.FacingDirection = dx > 0 ? 1 : 3;
        else
            actor.FacingDirection = dy > 0 ? 2 : 0;

        actor.CurrentAnimation = GetWalkAnimation(actor);
    }

    // 4-directional walk animation
    private static string GetWalkAnimation(ActorInstance actor)
    {
        string prefix = actor.Name.Equals("neighbor", StringComparison.OrdinalIgnoreCase)
            ? "mr"
            : "mg";

        return actor.FacingDirection switch
        {
            0 => prefix + "0",
            1 => prefix + "1",
            2 => prefix + "2",
            3 => prefix + "3",
            _ => prefix + "0"
        };
    }

    private void UpdateRoomPosition(ActorInstance actor)
    {
        var room = _world.Layout.Rooms
            .FirstOrDefault(r => r.Name.Equals(actor.CurrentRoom, StringComparison.OrdinalIgnoreCase));
        if (room == null) return;

        // World position = room.Offset + midpoint of walk path
        actor.Position = new NfhPoint(
            room.Offset.X + (room.Path1.X + room.Path2.X) / 2,
            room.Offset.Y + room.Path1.Y
        );
    }

    private ActorInstance? GetActor(string name) =>
        _world.GetActor(name, includeOlga: true);
}

internal class ActorNavState
{
    public string Target;
    public Queue<RoomEdge> Path;
    public RoomEdge? Edge;
    public float Time;

    public ActorNavState(string target, Queue<RoomEdge> path)
    {
        Target = target;
        Path = path;
    }
}