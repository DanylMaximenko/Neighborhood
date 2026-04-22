using System.Xml.Linq;

namespace Neighborhood.Shared.Models;

/// <summary>
/// Parsed from route.xml in routes.bnd.
///
/// A route is an ordered list of steps. Each step describes:
///   - which room to go to
///   - optionally: which object to interact with and which action to perform
///   - how long to stay (duration) or wait for animation to finish (duration=0 -> auto)
///   - whether the actor is "blind" during this step (cannot see Woody)
///
/// Example route.xml:
///   &lt;route&gt;
///     &lt;!-- Walk to sofa and sit down --&gt;
///     &lt;step room="lir" object="lir/sofa" action="enter"/&gt;
///     &lt;step room="lir" object="lir/sofa" action="sit" duration="5000"/&gt;
///
///     &lt;!-- Walk to bedroom and sleep (blind -- doesn't see Woody while asleep) --&gt;
///     &lt;step room="bed" object="bed/bed" action="enter"/&gt;
///     &lt;step room="bed" object="bed/bed" action="sleep" duration="15000" blind="true"/&gt;
///     &lt;step room="bed" object="bed/bed" action="wake"/&gt;
///
///     &lt;!-- Simple room visit with no object interaction --&gt;
///     &lt;step room="toi" duration="2000"/&gt;
///   &lt;/route&gt;
///
/// duration="0" or absent -> wait for animation to complete (actornextanim reached),
///                          then advance to next step automatically.
/// blind="true"           -> actor ignores Woody during this step (sleeping, diving, etc.)
/// </summary>
public class RouteData
{
    public IReadOnlyList<RouteStep> Steps { get; init; } = [];
    public bool IsEmpty => Steps.Count == 0;

    // --- Parsing -------------------------------------------------------------

    public static RouteData Parse(string xmlText)
    {
        try
        {
            var doc   = XDocument.Parse(xmlText);
            var steps = doc.Root!.Elements("step").Select(ParseStep)
                            .Where(s => !string.IsNullOrEmpty(s.Room))
                            .ToList();
            return new RouteData { Steps = steps };
        }
        catch { return new RouteData(); }
    }

    private static RouteStep ParseStep(XElement el)
    {
        var durationAttr = el.Attribute("duration")?.Value;
        int duration = durationAttr != null && int.TryParse(durationAttr, out var d) ? d : 0;

        return new RouteStep(
            Room:     el.Attribute("room")?.Value     ?? string.Empty,
            Object:   el.Attribute("object")?.Value,
            Action:   el.Attribute("action")?.Value,
            Duration: duration,
            Blind:    el.Attribute("blind")?.Value == "true");
    }

    // --- Random generation ----------------------------------------------------

    /// <summary>
    /// Generates a simple random route -- room visits only, no object interactions.
    /// Used as fallback when no route.xml is present.
    /// </summary>
    public static RouteData GenerateRandom(
        IEnumerable<string> roomNames,
        string levelName,
        int minDuration = 1500,
        int maxDuration = 4000)
    {
        var rooms = roomNames
            .Where(r => !r.Equals("fro", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rooms.Count == 0) return new RouteData();

        var rng   = new Random(levelName.GetHashCode());
        var steps = rooms
            .OrderBy(_ => rng.Next())
            .Select(r => new RouteStep(r, null, null, rng.Next(minDuration, maxDuration), false))
            .ToList();

        return new RouteData { Steps = steps };
    }
}

/// <summary>
/// One step in a patrol route.
/// </summary>
/// <param name="Room">Target room name.</param>
/// <param name="Object">
///   Optional object name to interact with on arrival (e.g. "lir/sofa").
///   Null = no object interaction, actor simply dwells in the room.
/// </param>
/// <param name="Action">
///   Action name from objects.xml to perform (e.g. "enter", "sit", "sleep").
///   Null if no object interaction.
/// </param>
/// <param name="Duration">
///   Milliseconds to hold this step before advancing.
///   0 = wait for animation to complete (actornextanim reached), then auto-advance.
/// </param>
/// <param name="Blind">
///   If true, actor cannot spot Woody during this step.
///   Used for sleeping, diving, absorbed-in-activity states.
/// </param>
public record RouteStep(
    string  Room,
    string? Object,
    string? Action,
    int     Duration,
    bool    Blind);
