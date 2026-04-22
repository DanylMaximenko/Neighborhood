using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Mutable runtime state of the game world for one level session.
///
/// Actor classification (NFH2):
///   neighbor -- main antagonist, patrols, reacts to pranks, beats Woody, shown in UI
///   mother   -- secondary antagonist (ship2+), patrols, beats Woody, shown in UI
///   olga     -- passive NPC, object of pranks, does NOT react to Woody directly,
///               NOT shown in UI; reacts to neighbor via prank animations only
///   woody    -- player character
///   aux      -- invisible helper actor for complex scene animations (NFH2)
///   kid/dog/chili/etc -- other passive actors
/// </summary>
public class WorldState
{
    // --- Named actors (always present) ---------------------------------------

    /// <summary>Null on levels where no neighbor actor is defined (e.g. tutorials).</summary>
    public ActorInstance? Neighbor { get; private set; }
    public ActorInstance Woody { get; private set; } = null!;

    /// <summary>
    /// NFH2 only: Mother (Neighbor's Mother).
    /// Present from ship2 onwards. Patrols independently, beats Woody on sight.
    /// Null on levels where mother is absent.
    /// </summary>
    public ActorInstance? Mother { get; private set; }

    /// <summary>
    /// NFH2 only: Olga.
    /// Present on all NFH2 levels. Passive NPC -- does NOT react to Woody.
    /// Participates as a target or bystander in prank animations.
    /// Null on NFH1 levels.
    /// </summary>
    public ActorInstance? Olga { get; private set; }

    // --- Other actors ---------------------------------------------------------

    /// <summary>All remaining actors keyed by name (dog, chili, kid, aux, etc.).</summary>
    public Dictionary<string, ActorInstance> Actors { get; } = new(StringComparer.OrdinalIgnoreCase);

    // --- World objects --------------------------------------------------------

    public Dictionary<string, ObjectInstance> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DoorInstance> Doors { get; } = new(StringComparer.OrdinalIgnoreCase);

    // --- Installed tricks ----------------------------------------------------

    public Dictionary<string, InstalledTrick> InstalledTricks { get; } = new(StringComparer.OrdinalIgnoreCase);

    // --- Level metadata ------------------------------------------------------

    public LevelLayout Layout { get; private set; } = null!;
    public string LevelName { get; private set; } = string.Empty;

    // --- Initialisation ------------------------------------------------------

    public void Initialise(LevelLayout layout, string levelName)
    {
        Layout = layout;
        LevelName = levelName;

        Objects.Clear();
        Doors.Clear();
        Actors.Clear();
        InstalledTricks.Clear();
        Mother = null;
        Olga = null;

        foreach (var obj in layout.Objects)
            Objects[obj.Name] = new ObjectInstance(obj.Name, obj.Layer, obj.Position, obj.Visible);

        foreach (var room in layout.Rooms)
        {
            foreach (var obj in room.Objects)
            {
                var worldPosition = new NfhPoint(room.Offset.X + obj.Position.X, room.Offset.Y + obj.Position.Y);
                Objects[obj.Name] = new ObjectInstance(obj.Name, obj.Layer, worldPosition, obj.Visible);
            }

            foreach (var door in room.Doors)
            {
                var worldPosition = new NfhPoint(room.Offset.X + door.Position.X, room.Offset.Y + door.Position.Y);
                Doors[door.Name] = new DoorInstance(door.Name, door.Layer, worldPosition, door.Visible);
            }

            foreach (var actor in room.Actors)
            {
                var worldPosition = new NfhPoint(room.Offset.X + actor.Position.X, room.Offset.Y + actor.Position.Y);
                var instance = new ActorInstance(
                    actor.Name, room.Name, worldPosition, actor.Layer,
                    initialAnimation: actor.Animation);

                switch (actor.Name.ToLowerInvariant())
                {
                    case "neighbor": Neighbor = instance; break;
                    case "woody": Woody = instance; break;
                    case "mother": Mother = instance; break;
                    case "olga": Olga = instance; break;
                    default: Actors[actor.Name] = instance; break;
                }
            }
        }

        // If Woody wasn't placed in any room (some NFH2 levels omit him),
        // spawn at walk-path midpoint of the first non-entrance room.
        if (Woody == null)
        {
            var spawnRoom = layout.Rooms
                .FirstOrDefault(r => !r.Name.Equals("fro", StringComparison.OrdinalIgnoreCase))
                ?? layout.Rooms.FirstOrDefault();

            if (spawnRoom != null)
            {
                int midX = (spawnRoom.Path1.X + spawnRoom.Path2.X) / 2;
                int y    = spawnRoom.Path1.Y;
                Woody = new ActorInstance(
                    "woody", spawnRoom.Name,
                    new NfhPoint(spawnRoom.Offset.X + midX, spawnRoom.Offset.Y + y),
                    layer: 4, initialAnimation: "ms2");
            }
        }

        // Note: Neighbor may be null on tutorial levels that have no neighbor actor.
        // GameLogic and NeighborBrain must handle Neighbor == null gracefully.
    }

    // --- Helpers -------------------------------------------------------------

    public bool TryGetObject(string name, out ObjectInstance obj) =>
        Objects.TryGetValue(name, out obj!);

    public void ReplaceObject(string oldName, string newName, bool visible = true)
    {
        if (Objects.TryGetValue(oldName, out var existing))
        {
            Objects.Remove(oldName);
            Objects[newName] = new ObjectInstance(newName, existing.Layer, existing.Position, visible);
        }
    }

    public void SetObjectVisible(string name, bool visible)
    {
        if (Objects.TryGetValue(name, out var obj))
            obj.Visible = visible;
    }

    public void InstallTrick(InstalledTrick trick) =>
        InstalledTricks[trick.ResultObjectName] = trick;

    public bool TryGetTrick(string objectName, out InstalledTrick trick) =>
        InstalledTricks.TryGetValue(objectName, out trick!);

    /// <summary>
    /// Returns an actor by name, checking all actor slots.
    /// Returns null for "olga" lookups from threat-detection code -- Olga is not a threat.
    /// </summary>
    public ActorInstance? GetActor(string name, bool includeOlga = false)
    {
        return name.ToLowerInvariant() switch
        {
            "neighbor" => Neighbor,
            "woody" => Woody,
            "mother" => Mother,
            "olga" => includeOlga ? Olga : null, // Olga excluded from threat logic
            _ => Actors.GetValueOrDefault(name)
        };
    }
}

// --- Instance types ----------------------------------------------------------

public class ObjectInstance
{
    public string Name { get; }
    public int Layer { get; }
    public NfhPoint Position { get; set; }
    public bool Visible { get; set; }
    public string CurrentAnimation { get; set; } = "ms";

    public ObjectInstance(string name, int layer, NfhPoint position, bool visible)
    {
        Name = name; Layer = layer; Position = position; Visible = visible;
    }
}

public class DoorInstance
{
    public string Name { get; }
    public int Layer { get; }
    public NfhPoint Position { get; set; }
    public bool Visible { get; set; }
    public bool IsOpen { get; set; }

    public DoorInstance(string name, int layer, NfhPoint position, bool visible)
    {
        Name = name; Layer = layer; Position = position; Visible = visible;
    }
}

public class ActorInstance
{
    public string Name { get; }
    public string CurrentRoom { get; set; }
    public NfhPoint Position { get; set; }
    public int Layer { get; }
    public string CurrentAnimation { get; set; }
    public bool IsTransitioning { get; set; }
    public int FacingDirection { get; set; } = 1;

    public ActorInstance(string name, string startRoom, NfhPoint position, int layer,
                         string? initialAnimation = null)
    {
        Name = name;
        CurrentRoom = startRoom;
        Position = position;
        Layer = layer;
        CurrentAnimation = initialAnimation ?? "mg0";
    }
}

public class InstalledTrick
{
    public string ResultObjectName { get; init; } = string.Empty;
    public string RoomName { get; init; } = string.Empty;
    public bool IsTriggered { get; set; }
    public int ProgressValue { get; init; }
    public int AngerValue { get; init; }
}