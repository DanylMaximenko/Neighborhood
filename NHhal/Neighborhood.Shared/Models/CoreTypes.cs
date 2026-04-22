namespace Neighborhood.Shared.Models;

/// <summary>
/// Coordinate or size stored in XML as "x/y" (e.g. "617/-101", "1428/736").
/// Used for positions, sizes, offsets, hotspots throughout all XML files.
/// </summary>
public readonly record struct NfhPoint(int X, int Y)
{
    public static NfhPoint Zero => new(0, 0);

    /// <summary>Parses "x/y" format. Returns Zero on failure.</summary>
    public static NfhPoint Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Zero;
        var slash = value.IndexOf('/');
        if (slash < 0) return Zero;
        if (int.TryParse(value.AsSpan(0, slash), out var x) &&
            int.TryParse(value.AsSpan(slash + 1), out var y))
            return new NfhPoint(x, y);
        return Zero;
    }

    public override string ToString() => $"{X}/{Y}";
}

/// <summary>Game variant -- determines which Data subfolder and XML schema to use.</summary>
public enum GameVariant { NFH1, NFH2 }
