using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1
{
    public class RoomGraph
    {
        private readonly Dictionary<string, List<RoomEdge>> _edges =
            new(StringComparer.OrdinalIgnoreCase);

        private RoomGraph() { }

        /// <summary>Factory method - builds the room graph from a level layout.</summary>
        public static RoomGraph Build(LevelLayout layout)
        {
            var g = new RoomGraph();
            g.BuildEdges(layout);
            return g;
        }

        // Build edges from the explicit <neighbor> elements in each room.
        // Each <neighbor> entry means: "from this room you can go to roomName via doorIn".
        // Edge positions are world-space midpoints of each room's walk path.
        private void BuildEdges(LevelLayout layout)
        {
            // Index rooms by name for fast lookup
            var byName = layout.Rooms.ToDictionary(
                r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);

            foreach (var room in layout.Rooms)
            {
                if (!_edges.ContainsKey(room.Name))
                    _edges[room.Name] = new List<RoomEdge>();

                foreach (var neighbor in room.Neighbors)
                {
                    if (!byName.TryGetValue(neighbor.RoomName, out var toRoom))
                        continue;

                    // World-space midpoints
                    var fromPos = new NfhPoint(
                        room.Offset.X   + (room.Path1.X   + room.Path2.X)   / 2,
                        room.Offset.Y   +  room.Path1.Y);

                    var toPos = new NfhPoint(
                        toRoom.Offset.X + (toRoom.Path1.X + toRoom.Path2.X) / 2,
                        toRoom.Offset.Y +  toRoom.Path1.Y);

                    _edges[room.Name].Add(new RoomEdge(
                        room.Name,
                        toRoom.Name,
                        neighbor.DoorIn,
                        fromPos,
                        toPos,
                        neighbor.Costs));
                }
            }
        }

        public List<RoomEdge> FindPath(string from, string to)
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
                return new List<RoomEdge>();

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
            var queue   = new Queue<(string room, List<RoomEdge> path)>();
            queue.Enqueue((from, new List<RoomEdge>()));

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();

                if (!_edges.TryGetValue(current, out var edges))
                    continue;

                foreach (var edge in edges)
                {
                    if (visited.Contains(edge.ToRoom)) continue;
                    visited.Add(edge.ToRoom);

                    var newPath = new List<RoomEdge>(path) { edge };

                    if (edge.ToRoom.Equals(to, StringComparison.OrdinalIgnoreCase))
                        return newPath;

                    queue.Enqueue((edge.ToRoom, newPath));
                }
            }

            return new List<RoomEdge>(); // no path
        }
    }
}
