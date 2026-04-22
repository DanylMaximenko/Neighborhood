namespace Neighborhood.Shared.Models;

/// <summary>
/// Parsed from level.xml inside each level folder.
/// Describes the physical layout: rooms, floors, objects, doors, actors.
/// NFH2 adds visible="true/false" on objects/doors and animation/focussed on actors.
/// Coordinate format: "x/y" parsed via NfhPoint.
/// </summary>
public class LevelLayout
{
    public string Name { get; init; } = string.Empty;
    public NfhPoint Size { get; init; }
    public int AngryTime { get; init; }
    public IReadOnlyList<LevelObject> Objects { get; init; } = [];
    public IReadOnlyList<Room> Rooms { get; init; } = [];
}

public class Room
{
    public string Name { get; init; } = string.Empty;
    public NfhPoint Offset { get; init; }
    public NfhPoint Path1 { get; init; }
    public NfhPoint Path2 { get; init; }
    public IReadOnlyList<Floor> Floors { get; init; } = [];
    public IReadOnlyList<LevelObject> Objects { get; init; } = [];
    public IReadOnlyList<LevelDoor> Doors { get; init; } = [];
    public IReadOnlyList<LevelActor> Actors { get; init; } = [];
    public IReadOnlyList<RoomNeighbor> Neighbors { get; init; } = [];
}

public class Floor
{
    public NfhPoint Offset { get; init; }
    public NfhPoint Size { get; init; }
    public bool IsWall { get; init; }
    public NfhPoint Hotspot { get; init; }
}

public class LevelObject
{
    public string Name { get; init; } = string.Empty;
    public int Layer { get; init; }
    public NfhPoint Position { get; init; }
    public bool Visible { get; init; } = true;
}

public class LevelDoor
{
    public string Name { get; init; } = string.Empty;
    public int Layer { get; init; }
    public NfhPoint Position { get; init; }
    public bool Visible { get; init; } = true;
}

public class LevelActor
{
    public string Name { get; init; } = string.Empty;
    public int Layer { get; init; }
    public NfhPoint Position { get; init; }

    /// <summary>NFH2 only: initial visibility. Always true in NFH1.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// NFH2 only: initial animation state on level load
    /// (e.g. "ms2", "annoyed_remote"). Null in NFH1.
    /// </summary>
    public string? Animation { get; init; }

    /// <summary>
    /// NFH2 only: camera focuses on this actor at level start.
    /// </summary>
    public bool Focussed { get; init; }
}

public class RoomNeighbor
{
    public string RoomName { get; init; } = string.Empty;
    public int Costs { get; init; }
    public string DoorIn { get; init; } = string.Empty;
    public string DoorOut { get; init; } = string.Empty;
}
