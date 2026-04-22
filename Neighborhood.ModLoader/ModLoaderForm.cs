using System.Xml.Serialization;
using Neighborhood.Shared.Models;

namespace Neighborhood.ModLoader;

/// <summary>
/// Main form for ModLoader.exe.
///
/// Variant detection: checks for NFH2.dll in app directory.
///
/// Mod discovery: scans Mods/ for subfolders containing mod.xml.
/// Only shows mods compatible with the detected variant.
///
/// Launching: starts Game.exe passing --game and --mod arguments.
/// Packing: ModPackDialog lets user create a new mod folder from a Data copy.
/// </summary>
public partial class ModLoaderForm : Form
{
    private readonly string      _baseDir;
    private readonly string      _modsDir;
    private readonly GameVariant _activeVariant;
    private readonly List<ModInfo> _mods = [];

    private static readonly XmlSerializer _manifestSerializer = new(typeof(ModManifest));

    public ModLoaderForm()
    {
        _baseDir       = AppDomain.CurrentDomain.BaseDirectory;
        _modsDir       = Path.Combine(_baseDir, "Mods");
        _activeVariant = File.Exists(Path.Combine(_baseDir, "NFH2.dll"))
            ? GameVariant.NFH2
            : GameVariant.NFH1;

        InitializeComponent();
        LoadMods();
    }

    // --- Mod discovery -------------------------------------------------------

    private void LoadMods()
    {
        _mods.Clear();
        listBoxMods.Items.Clear();

        if (!Directory.Exists(_modsDir))
            Directory.CreateDirectory(_modsDir);

        foreach (var dir in Directory.GetDirectories(_modsDir))
        {
            var manifestPath = Path.Combine(dir, "mod.xml");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifest = ReadManifest(manifestPath);
                if (!manifest.IsCompatibleWith(_activeVariant)) continue;

                var info = new ModInfo { FolderPath = dir, Manifest = manifest };
                _mods.Add(info);
                listBoxMods.Items.Add(info);
            }
            catch { /* malformed mod.xml -- skip */ }
        }

        UpdateDetails(null);
        btnLaunch.Enabled = false;
    }

    private ModManifest ReadManifest(string path)
    {
        using var stream = File.OpenRead(path);
        return (_manifestSerializer.Deserialize(stream) as ModManifest)!;
    }

    // --- UI events -----------------------------------------------------------

    private void listBoxMods_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = listBoxMods.SelectedItem as ModInfo;
        UpdateDetails(selected);
        btnLaunch.Enabled = selected != null;
    }

    private void btnLaunch_Click(object? sender, EventArgs e)
    {
        if (listBoxMods.SelectedItem is not ModInfo mod) return;
        LaunchGame($"--mod \"{mod.FolderPath}\"");
    }

    private void btnLaunchNoMod_Click(object? sender, EventArgs e) =>
        LaunchGame(null);

    private void LaunchGame(string? extraArgs)
    {
        var gameExe = Path.Combine(_baseDir, "Game.exe");
        if (!File.Exists(gameExe))
        {
            MessageBox.Show("Game.exe not found.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var args = $"--game {_activeVariant.ToString().ToLower()}";
        if (extraArgs != null) args += " " + extraArgs;

        System.Diagnostics.Process.Start(gameExe, args);
        Application.Exit();
    }

    private void btnNewMod_Click(object? sender, EventArgs e)
    {
        using var dlg = new ModPackDialog(_activeVariant, _modsDir);
        if (dlg.ShowDialog() == DialogResult.OK)
            LoadMods();
    }

    private void btnRefresh_Click(object? sender, EventArgs e) => LoadMods();

    private void btnOpenFolder_Click(object? sender, EventArgs e)
    {
        if (listBoxMods.SelectedItem is ModInfo mod)
            System.Diagnostics.Process.Start("explorer.exe", mod.FolderPath);
    }

    // --- Details panel -------------------------------------------------------

    private void UpdateDetails(ModInfo? mod)
    {
        lblModName.Text        = mod?.Manifest.Name ?? string.Empty;
        lblModAuthor.Text      = mod != null ? $"Author: {mod.Manifest.Author}" : string.Empty;
        lblModVersion.Text     = mod != null ? $"Version: {mod.Manifest.Version}" : string.Empty;
        txtModDescription.Text = mod?.Manifest.Description ?? string.Empty;
    }

    // --- Controls ------------------------------------------------------------

    private ListBox  listBoxMods       = null!;
    private Label    lblVariant        = null!;
    private Label    lblModName        = null!;
    private Label    lblModAuthor      = null!;
    private Label    lblModVersion     = null!;
    private TextBox  txtModDescription = null!;
    private Button   btnLaunch         = null!;
    private Button   btnLaunchNoMod   = null!;
    private Button   btnNewMod         = null!;
    private Button   btnRefresh        = null!;
    private Button   btnOpenFolder     = null!;

    private void InitializeComponent()
    {
        Text          = "Neighborhood -- Mod Loader";
        Size          = new Size(620, 500);
        MinimumSize   = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;

        lblVariant = new Label
        {
            Text      = $"Active: {_activeVariant}",
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock      = DockStyle.Top, Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(6, 0, 0, 0)
        };

        listBoxMods = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f)
        };
        listBoxMods.SelectedIndexChanged += listBoxMods_SelectedIndexChanged;

        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 220 };
        leftPanel.Controls.Add(listBoxMods);

        lblModName        = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        lblModAuthor      = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 8.5f) };
        lblModVersion     = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 8.5f) };
        txtModDescription = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Control,
            Font = new Font("Segoe UI", 8.5f)
        };

        var detailsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        detailsPanel.Controls.AddRange([txtModDescription, lblModVersion, lblModAuthor, lblModName]);

        btnLaunch      = new Button { Text = "Launch with mod", Width = 150, Height = 32, Enabled = false };
        btnLaunchNoMod = new Button { Text = "Launch (no mod)", Width = 140, Height = 32 };
        btnNewMod      = new Button { Text = "New mod...",        Width = 100, Height = 32 };
        btnOpenFolder  = new Button { Text = "Open folder",     Width = 100, Height = 32 };
        btnRefresh     = new Button { Text = "Refresh",         Width = 80,  Height = 32 };

        btnLaunch.Click      += btnLaunch_Click;
        btnLaunchNoMod.Click += btnLaunchNoMod_Click;
        btnNewMod.Click      += btnNewMod_Click;
        btnRefresh.Click     += btnRefresh_Click;
        btnOpenFolder.Click  += btnOpenFolder_Click;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4)
        };
        btnPanel.Controls.AddRange([btnLaunch, btnLaunchNoMod, btnNewMod, btnOpenFolder, btnRefresh]);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, SplitterDistance = 220,
            Panel1MinSize = 160, Panel2MinSize = 200
        };
        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(detailsPanel);

        Controls.Add(split);
        Controls.Add(btnPanel);
        Controls.Add(lblVariant);
    }
}
