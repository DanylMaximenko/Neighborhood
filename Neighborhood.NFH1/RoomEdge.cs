using Neighborhood.Shared.Models;

public class RoomEdge
{
    public string FromRoom { get; }
    public string ToRoom { get; }
    public string DoorIn { get; }

    public NfhPoint FromPosition { get; }
    public NfhPoint ToPosition { get; }

    public int Cost { get; }

    public RoomEdge(string from, string to, string doorIn,
        NfhPoint fromPos, NfhPoint toPos, int cost = 1000)
    {
        FromRoom = from;
        ToRoom = to;
        DoorIn = doorIn;
        FromPosition = fromPos;
        ToPosition = toPos;
        Cost = cost;
    }
}