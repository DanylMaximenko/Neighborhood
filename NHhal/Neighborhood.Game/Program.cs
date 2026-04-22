using Neighborhood.GFXLoader;
using Neighborhood.Loader;
using Neighborhood.NFH1;
using Neighborhood.NFH2;
using Neighborhood.Shared.Models;
using Neighborhood.SFXLoader;

namespace Neighborhood.Game;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var dataPath = ResolveDataPath(ParseArg(args, "--data"));

        if (!TryResolveVariant(args, out var variant))
            return;

        var modPath = ParseArg(args, "--mod");

        ModInfo? mod = null;
        if (modPath != null && Directory.Exists(modPath))
        {
            var manifestPath = Path.Combine(modPath, "mod.xml");
            if (File.Exists(manifestPath))
            {
                var s = new System.Xml.Serialization.XmlSerializer(typeof(ModManifest));
                using var stream = File.OpenRead(manifestPath);
                mod = new ModInfo { FolderPath = modPath, Manifest = (ModManifest)s.Deserialize(stream)! };
            }
        }

        // Bootstrap loaders
        var gfxLoader     = new GfxLoader();
        var sfxLoader     = new SfxLoader();
        var loaderService = new LoaderService(new BndLoader(), gfxLoader, sfxLoader);

        try { loaderService.Load(dataPath, variant, mod); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal load error: {ex.Message}");
            Environment.Exit(1);
        }

        // Select and initialise game logic
        GameLogic logic = variant == GameVariant.NFH2
            ? new GameLogicWithMiniGames(gfxLoader, sfxLoader)
            : new GameLogic(gfxLoader, sfxLoader);

        logic.Initialise(
            loaderService.Layouts,
            loaderService.Combines,
            loaderService.Tricks,
            loaderService.Triggers,
            loaderService.Routes,
            new Dictionary<string, Neighborhood.Shared.Models.AnimsFile>(), // populated per-level by renderer
            loaderService.LevelData);

        logic.LevelWon  += () => Console.WriteLine("Level won!");
        logic.LevelLost += () => Console.WriteLine("Level lost.");

        // Load first available level
        var firstLevel = loaderService.LevelData.Sets
            .SelectMany(s => s.Levels)
            .FirstOrDefault(l => l.State == "playable");

        if (firstLevel == null)
        {
            MessageBox.Show(
                "No playable level found in leveldata.xml.",
                "Neighborhood",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        logic.LoadLevel(firstLevel.Name);
        Console.WriteLine($"Loaded level: {firstLevel.Name}");
        Console.WriteLine($"Engine ready. Variant={variant}, Levels={loaderService.Layouts.Count}");

        // --mode original | hd  (default: hd)
        var modeArg = ParseArg(args, "--mode");
        var renderMode = modeArg?.Equals("original", StringComparison.OrdinalIgnoreCase) == true
            ? RenderMode.Original
            : RenderMode.HD;

        using var form = new GameForm(logic, loaderService, gfxLoader, sfxLoader,
                                      variant, firstLevel.Name, renderMode);
        Application.Run(form);
    }

    private static bool TryResolveVariant(string[] args, out GameVariant variant)
    {
        var variantArg = ParseArg(args, "--game");
        if (variantArg?.Equals("nfh2", StringComparison.OrdinalIgnoreCase) == true)
        {
            variant = GameVariant.NFH2;
            return true;
        }

        if (variantArg?.Equals("nfh1", StringComparison.OrdinalIgnoreCase) == true)
        {
            variant = GameVariant.NFH1;
            return true;
        }

        using var dialog = new GameVariantSelectionForm();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            variant = dialog.SelectedVariant;
            return true;
        }

        variant = default;
        return false;
    }

    private static string? ParseArg(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static string ResolveDataPath(string? userPath)
    {
        if (!string.IsNullOrWhiteSpace(userPath) && Directory.Exists(userPath))
        {
            var resolved = Path.GetFullPath(userPath);
            Console.WriteLine($"[Path] Using --data: {resolved}");
            return resolved;
        }

        foreach (var candidate in GetDefaultDataCandidates())
        {
            if (!Directory.Exists(candidate))
                continue;

            Console.WriteLine($"[Path] Using data path: {candidate}");
            return candidate;
        }

        var fallback = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"));
        Console.Error.WriteLine($"[Path] Data folder not found; fallback to: {fallback}");
        return fallback;
    }

    private static IEnumerable<string> GetDefaultDataCandidates()
    {
        yield return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"));
        yield return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

        // Typical dev run from bin/Debug/net8.0-windows
        yield return Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "Data"));
    }
}
