using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Directed graph of room connections built from level.xml &lt;neighbor&gt; elements.
/// Used by ActorController to find paths between rooms (Dijkstra by cost).
/// </summary>
public class RoomGraph
{
    private readonly Dictionary<string, List<RoomEdge>> _adjacency =
        new(StringComparer.OrdinalIgnoreCase);

    // --- Build ----------------------------------------------------------------

    public static RoomGraph Build(LevelLayout layout)
    {
        var graph = new RoomGraph();

        foreach (var room in layout.Rooms)
        {
            if (!graph._adjacency.ContainsKey(room.Name))
                graph._adjacency[room.Name] = [];

            foreach (var neighbor in room.Neighbors)
            {
                graph._adjacency[room.Name].Add(new RoomEdge(
                    FromRoom: room.Name,
                    ToRoom:   neighbor.RoomName,
                    Cost:     neighbor.Costs,
                    DoorIn:   neighbor.DoorIn,
                    DoorOut:  neighbor.DoorOut));

                if (!graph._adjacency.ContainsKey(neighbor.RoomName))
                    graph._adjacency[neighbor.RoomName] = [];
            }
        }

        return graph;
    }

    // --- Pathfinding (Dijkstra) -----------------------------------------------

    public IReadOnlyList<RoomEdge> FindPath(string fromRoom, string toRoom)
    {
        if (fromRoom.Equals(toRoom, StringComparison.OrdinalIgnoreCase))
            return [];

        var dist    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var prev    = new Dictionary<string, RoomEdge?>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue   = new PriorityQueue<string, int>();

        foreach (var node in _adjacency.Keys)
        {
            dist[node] = int.MaxValue;
            prev[node] = null;
        }

        dist[fromRoom] = 0;
        queue.Enqueue(fromRoom, 0);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            if (current.Equals(toRoom, StringComparison.OrdinalIgnoreCase))
                break;

            if (!_adjacency.TryGetValue(current, out var edges)) continue;

            foreach (var edge in edges)
            {
                if (visited.Contains(edge.ToRoom)) continue;
                var newDist = dist[current] == int.MaxValue
                    ? int.MaxValue
                    : dist[current] + edge.Cost;

                if (!dist.ContainsKey(edge.ToRoom)) dist[edge.ToRoom] = int.MaxValue;
                if (newDist < dist[edge.ToRoom])
                {
                    dist[edge.ToRoom] = newDist;
                    prev[edge.ToRoom] = edge;
                    queue.Enqueue(edge.ToRoom, newDist);
                }
            }
        }

        if (!dist.ContainsKey(toRoom) || dist[toRoom] == int.MaxValue)
            return [];

        var path  = new List<RoomEdge>();
        var node2 = toRoom;
        while (prev.TryGetValue(node2, out var edge) && edge != null)
        {
            path.Add(edge);
            node2 = edge.FromRoom;
        }

        path.Reverse();
        return path;
    }

    public IEnumerable<string> Rooms => _adjacency.Keys;

    public IReadOnlyList<RoomEdge> EdgesFrom(string room) =>
        _adjacency.TryGetValue(room, out var edges) ? edges : [];
}

/// <summary>A directed edge in the room graph.</summary>
public record RoomEdge(
    string FromRoom,
    string ToRoom,
    int    Cost,
    string DoorIn,
    string DoorOut);
