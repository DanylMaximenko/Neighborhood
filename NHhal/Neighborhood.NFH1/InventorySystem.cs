using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Manages Woody's inventory and the combination system.
///
/// From combine.xml analysis:
///   - Each combination has 2 ingredients
///   - result name becomes the new object in the world
///   - trick="true"  -> installing a prank, scored in tricks.xml
///   - wrong="true"  -> invalid combo, show feedback to player
///   - remove="true" -> ingredient disappears after use
///   - remove="false" -> ingredient stays (e.g. a room used as location)
///
/// NFH2 additions (handled but not used in NFH1 path):
///   - layer override on result
///   - game="minigame/..." -> launches mini-game instead of direct placement
/// </summary>
public class InventorySystem
{
    private readonly WorldState   _world;
    private readonly CombineFile  _combines;
    private readonly TricksFile   _tricks;
    private readonly GameVariant  _variant;

    /// <summary>Items currently in Woody's inventory. Key: item name.</summary>
    private readonly HashSet<string> _inventory = new(StringComparer.OrdinalIgnoreCase);

    public InventorySystem(WorldState world, CombineFile combines, TricksFile tricks, GameVariant variant)
    {
        _world    = world;
        _combines = combines;
        _tricks   = tricks;
        _variant  = variant;
    }

    // --- Events --------------------------------------------------------------

    /// <summary>Fired when a valid trick is successfully installed.</summary>
    public event Action<InstalledTrick>? TrickInstalled;

    /// <summary>
    /// Fired when an invalid combination is attempted.
    /// Args: (combinationResultName, wrongFeedbackText).
    /// NFH2: text comes from wrongstrings.xml.
    /// NFH1: generic "can't do that" feedback.
    /// </summary>
    public event Action<string, string>? WrongCombination;

    /// <summary>Fired when Woody picks up an item from the level.</summary>
    public event Action<string>? ItemPickedUp;

    /// <summary>Fired when an item is removed from inventory (used/consumed).</summary>
    public event Action<string>? ItemConsumed;

    // --- Inventory management ------------------------------------------------

    public void AddItem(string itemName)
    {
        _inventory.Add(itemName);
        ItemPickedUp?.Invoke(itemName);
    }

    public bool HasItem(string itemName) =>
        _inventory.Contains(itemName);

    public IReadOnlyCollection<string> Items => _inventory;

    // --- Combination ---------------------------------------------------------

    /// <summary>
    /// Attempts to combine two items/objects.
    ///
    /// One ingredient can be a room name or an in-world object (remove=false),
    /// the other is typically an inventory item (remove=true).
    ///
    /// Returns a CombineResult describing what happened.
    /// </summary>
    public CombineResult TryCombine(string ingredientA, string ingredientB)
    {
        // Try both orderings -- XML doesn't guarantee order
        var combo = FindCombination(ingredientA, ingredientB)
                 ?? FindCombination(ingredientB, ingredientA);

        if (combo == null)
            return CombineResult.NoMatch;

        if (combo.IsWrong)
        {
            WrongCombination?.Invoke(combo.ResultName, string.Empty);
            return CombineResult.Wrong(combo.ResultName);
        }

        // NFH2 mini-game trigger -- return for caller to handle
        if (!string.IsNullOrEmpty(combo.MiniGamePath))
            return CombineResult.MiniGame(combo.ResultName, combo.MiniGamePath);

        // Apply ingredient removal
        foreach (var ingredient in combo.Ingredients)
        {
            if (ingredient.Remove)
            {
                _inventory.Remove(ingredient.Name);
                _world.SetObjectVisible(ingredient.Name, false);
                ItemConsumed?.Invoke(ingredient.Name);
            }
        }

        // Place result object in world
        if (combo.IsTrick)
        {
            var trick = BuildTrick(combo.ResultName);
            _world.InstallTrick(trick);
            _world.SetObjectVisible(combo.ResultName, true);
            TrickInstalled?.Invoke(trick);
            return CombineResult.TrickPlaced(trick);
        }

        // Non-trick combination (e.g. filling a bottle from a tub)
        _world.SetObjectVisible(combo.ResultName, true);
        if (_world.TryGetObject(combo.ResultName, out _))
            AddItem(combo.ResultName); // result goes to inventory if it's an inventar item

        return CombineResult.Success(combo.ResultName);
    }

    // --- Private helpers -----------------------------------------------------

    private Combination? FindCombination(string a, string b) =>
        _combines.Combinations.FirstOrDefault(c =>
            c.Ingredients.Count == 2 &&
            c.Ingredients[0].Name.Equals(a, StringComparison.OrdinalIgnoreCase) &&
            c.Ingredients[1].Name.Equals(b, StringComparison.OrdinalIgnoreCase));

    private InstalledTrick BuildTrick(string resultName)
    {
        var trick = _tricks.Tricks.FirstOrDefault(t =>
            t.Name.Equals(resultName, StringComparison.OrdinalIgnoreCase) ||
            resultName.EndsWith(t.Name, StringComparison.OrdinalIgnoreCase));

        // Determine which room the result object is in
        string roomName = FindObjectRoom(resultName) ?? _world.Neighbor.CurrentRoom;

        int progressValue = _variant == GameVariant.NFH1
            ? (trick?.Quotas.Count > 0 ? trick.Quotas[0] : 0)
            : (trick?.Coins ?? 0);

        int angerValue = _variant == GameVariant.NFH1
            ? (trick?.AngryTime ?? 0)
            : (trick?.Rage ?? 0);

        return new InstalledTrick
        {
            ResultObjectName = resultName,
            RoomName         = roomName,
            ProgressValue    = progressValue,
            AngerValue       = angerValue
        };
    }

    private string? FindObjectRoom(string objectName) =>
        _world.Layout.Rooms
            .FirstOrDefault(r => r.Objects.Any(o =>
                o.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase)))
            ?.Name;
}

// --- Result types -------------------------------------------------------------

public enum CombineResultType { NoMatch, Wrong, Success, TrickPlaced, MiniGame }

public class CombineResult
{
    public CombineResultType Type           { get; private init; }
    public string?           ResultName     { get; private init; }
    public InstalledTrick?   Trick          { get; private init; }
    public string?           MiniGamePath   { get; private init; }

    public static readonly CombineResult NoMatch = new() { Type = CombineResultType.NoMatch };

    public static CombineResult Wrong(string resultName) => new()
        { Type = CombineResultType.Wrong, ResultName = resultName };

    public static CombineResult Success(string resultName) => new()
        { Type = CombineResultType.Success, ResultName = resultName };

    public static CombineResult TrickPlaced(InstalledTrick trick) => new()
        { Type = CombineResultType.TrickPlaced, Trick = trick, ResultName = trick.ResultObjectName };

    public static CombineResult MiniGame(string resultName, string miniGamePath) => new()
        { Type = CombineResultType.MiniGame, ResultName = resultName, MiniGamePath = miniGamePath };
}
