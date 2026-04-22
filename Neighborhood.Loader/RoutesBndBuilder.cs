using Neighborhood.Shared.Models;
using System.IO.Compression;
using System.Text;

namespace Neighborhood.Loader;

/// <summary>
/// Utility for creating and updating routes.bnd -- the shared patrol route archive.
///
/// routes.bnd structure:
///   level_bath/route.xml          <- NFH1 neighbor patrol
///   level_peep/route.xml
///   cn_b1/route.xml               <- NFH2 neighbor patrol
///   cn_b1_mother/route.xml        <- NFH2 mother patrol (separate key)
///   ship2/route.xml
///   ship2_mother/route.xml        <- Mother first appears on ship2
///   ...
///
/// Mother route key convention: "{levelName}_mother"
/// Mother is only present on: ship2, in_*, me_*, ship3, ship4
///
/// This file lives in Data/ (not Data/NFH1/ or Data/NFH2/) and is shared
/// between both game variants. It is NOT part of the original game files --
/// it is created by the engine/editor to define patrol routes.
///
/// Usage:
///   // Create new routes.bnd with routes for all levels
///   var builder = new RoutesBndBuilder();
///   builder.AddRoute("level_bath", new RouteData { Steps = [...] });
///   builder.AddRoute("cn_b1",      new RouteData { Steps = [...] });
///   builder.Save("Data/routes.bnd");
///
///   // Update a single route in existing routes.bnd
///   RoutesBndBuilder.UpdateRoute("Data/routes.bnd", "level_bath", route);
/// </summary>
public class RoutesBndBuilder
{
    private readonly Dictionary<string, RouteData> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public void AddRoute(string levelName, RouteData route)
    {
        _routes[levelName] = route;
    }

    public void RemoveRoute(string levelName) =>
        _routes.Remove(levelName);

    /// <summary>Saves all routes to a new routes.bnd ZIP archive.</summary>
    public void Save(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        foreach (var (levelName, route) in _routes)
        {
            var entryName = $"{levelName}/route.xml";
            var entry     = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var xml = SerializeRoute(levelName, route);
            stream.Write(Encoding.UTF8.GetBytes(xml));
        }
    }

    /// <summary>
    /// Loads existing routes.bnd, adds/updates a single route, saves back.
    /// Creates the file if it doesn't exist.
    /// </summary>
    public static void UpdateRoute(string bndPath, string levelName, RouteData route)
    {
        var builder = LoadExisting(bndPath);
        builder.AddRoute(levelName, route);
        builder.Save(bndPath);
    }

    /// <summary>Loads all routes from an existing routes.bnd.</summary>
    public static RoutesBndBuilder LoadExisting(string bndPath)
    {
        var builder = new RoutesBndBuilder();

        if (!File.Exists(bndPath)) return builder;

        using var zip = ZipFile.OpenRead(bndPath);
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.Equals("route.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            var levelName = entry.FullName.Replace('\\', '/').Split('/')[0];
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var xml = reader.ReadToEnd();
            builder._routes[levelName] = RouteData.Parse(xml);
        }

        return builder;
    }

    // --- Serialization --------------------------------------------------------

    private static string SerializeRoute(string levelName, RouteData route)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<!-- Patrol route for level: {levelName} -->");
        sb.AppendLine("<route>");
        foreach (var step in route.Steps)
            sb.AppendLine($"  <step room=\"{step.Room}\" duration=\"{step.Duration}\"/>");
        sb.AppendLine("</route>");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a complete routes.bnd with random routes for all known levels.
    /// Useful as a starting point -- routes can then be refined manually via Editor.
    /// </summary>
    public static void GenerateDefault(
        string outputPath,
        IEnumerable<(string LevelName, IEnumerable<string> RoomNames)> levels)
    {
        var builder = new RoutesBndBuilder();
        foreach (var (levelName, rooms) in levels)
        {
            var route = RouteData.GenerateRandom(rooms, levelName);
            if (!route.IsEmpty)
                builder.AddRoute(levelName, route);
        }
        builder.Save(outputPath);
    }
}
