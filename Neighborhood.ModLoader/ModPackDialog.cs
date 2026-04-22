using System.IO.Compression;
using System.Xml.Serialization;
using Neighborhood.Shared.Models;

namespace Neighborhood.ModLoader;

/// <summary>
/// Creates a new mod folder under Mods/.
///
/// Format A -- .bnd archives (original compatibility):
///   Copies selected .bnd files from the source Data/NFH* folder.
///   Resulting mod is compatible with original game mod loaders.
///
/// Format B -- loose files (new format):
///   Copies selected XML/asset files preserving folder structure.
///   Only changed files need to be included.
///
/// Format A+B -- combined:
///   Both .bnd archives and loose file overrides in same folder.
///   Loose files take precedence over .bnd contents at load time.
/// </summary>
public class ModPackDialog : Form
{
    private readonly GameVariant _activeVariant;
    private readonly string      _modsDir;
    private string?              _sourcePath;

    private static readonly XmlSerializer _manifestSerializer = new(typeof(ModManifest));

    // Metadata controls
    private TextBox txtId = null!, txtName = null!, txtAuthor = null!,
                    txtVersion = null!, txtDescription = null!, txtSourcePath = null!;
    private Button  btnBrowse = null!, btnOk = null!, btnCancel = null!;

    // Format selector
    private RadioButton rbFormatA = null!, rbFormatB = null!, rbFormatAB = null!;

    // Format A controls
    private Panel         panelA = null!;
    private CheckBox      chkGamedata = null!, chkGfxData = null!, chkSfxData = null!, chkSfxHigh = null!;

    // Format B controls
    private Panel         panelB = null!;
    private CheckedListBox chkLooseFiles = null!;

    public ModPackDialog(GameVariant activeVariant, string modsDir)
    {
        _activeVariant = activeVariant;
        _modsDir       = modsDir;
        Build();
    }

    // --- Build UI ------------------------------------------------------------

    private void Build()
    {
        Text            = "Create New Mod";
        Size            = new Size(500, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;

        var main = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12), AutoScroll = true
        };

        // -- Metadata fields --
        main.Controls.Add(MakeLabel("Mod ID:"));
        txtId = MakeTextBox(); main.Controls.Add(txtId);

        main.Controls.Add(MakeLabel("Name:"));
        txtName = MakeTextBox(); main.Controls.Add(txtName);

        main.Controls.Add(MakeLabel("Author:"));
        txtAuthor = MakeTextBox(); main.Controls.Add(txtAuthor);

        main.Controls.Add(MakeLabel("Version:"));
        txtVersion = MakeTextBox("1.0"); main.Controls.Add(txtVersion);

        main.Controls.Add(MakeLabel("Description:"));
        txtDescription = new TextBox
        {
            Multiline = true, Height = 50, Width = 440,
            ScrollBars = ScrollBars.Vertical
        };
        main.Controls.Add(txtDescription);

        // -- Source folder --
        main.Controls.Add(MakeLabel("Source folder:"));
        var pathRow = new FlowLayoutPanel { Width = 440, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        txtSourcePath = new TextBox { ReadOnly = true, Width = 400 };
        btnBrowse = new Button { Text = "...", Width = 32 };
        btnBrowse.Click += BtnBrowse_Click;
        pathRow.Controls.AddRange([txtSourcePath, btnBrowse]);
        main.Controls.Add(pathRow);

        // -- Format selector --
        main.Controls.Add(MakeLabel("Format:"));
        var fmtRow = new FlowLayoutPanel { Width = 440, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        rbFormatA  = new RadioButton { Text = "A -- .bnd archives",   Checked = true, Width = 145 };
        rbFormatB  = new RadioButton { Text = "B -- loose files",      Width = 130 };
        rbFormatAB = new RadioButton { Text = "A+B -- both",          Width = 100 };
        rbFormatA.CheckedChanged  += (_, _) => UpdateFormatPanels();
        rbFormatB.CheckedChanged  += (_, _) => UpdateFormatPanels();
        rbFormatAB.CheckedChanged += (_, _) => UpdateFormatPanels();
        fmtRow.Controls.AddRange([rbFormatA, rbFormatB, rbFormatAB]);
        main.Controls.Add(fmtRow);

        // -- Format A panel --
        panelA = new Panel { Width = 440, Height = 80, Visible = true };
        chkGamedata = new CheckBox { Text = "gamedata.bnd",    Checked = true,  Left = 0,   Top = 0  };
        chkGfxData  = new CheckBox { Text = "gfxdata.bnd",    Checked = true,  Left = 0,   Top = 22 };
        chkSfxData  = new CheckBox { Text = "sfxdata.bnd",    Checked = true,  Left = 160, Top = 0  };
        chkSfxHigh  = new CheckBox { Text = "sfxdatahigh.bnd",Checked = true,  Left = 160, Top = 22 };
        panelA.Controls.AddRange([chkGamedata, chkGfxData, chkSfxData, chkSfxHigh]);
        main.Controls.Add(panelA);

        // -- Format B panel --
        panelB = new Panel { Width = 440, Height = 120, Visible = false };
        var lblFiles = new Label { Text = "Files to include:", Dock = DockStyle.Top, Height = 18 };
        chkLooseFiles = new CheckedListBox { Dock = DockStyle.Fill };
        panelB.Controls.AddRange([chkLooseFiles, lblFiles]);
        main.Controls.Add(panelB);

        // -- Buttons --
        btnOk     = new Button { Text = "Create", DialogResult = DialogResult.OK,     Width = 80 };
        btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        btnOk.Click += BtnOk_Click;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 40,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4)
        };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);

        Controls.Add(main);
        Controls.Add(btnPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void UpdateFormatPanels()
    {
        panelA.Visible = rbFormatA.Checked || rbFormatAB.Checked;
        panelB.Visible = rbFormatB.Checked || rbFormatAB.Checked;
    }

    // --- Source folder browse -------------------------------------------------

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = $"Select Data/NFH{(_activeVariant == GameVariant.NFH1 ? "1" : "2")} folder"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        _sourcePath = dlg.SelectedPath;
        txtSourcePath.Text = _sourcePath;

        // Populate Format B loose file list
        chkLooseFiles.Items.Clear();
        foreach (var file in Directory.EnumerateFiles(_sourcePath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_sourcePath, file).Replace('\\', '/');
            // Skip .bnd -- those are Format A
            if (rel.EndsWith(".bnd", StringComparison.OrdinalIgnoreCase)) continue;
            chkLooseFiles.Items.Add(rel, true);
        }
    }

    // --- Create mod -----------------------------------------------------------

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            MessageBox.Show("Mod ID is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var outDir = Path.Combine(_modsDir, txtId.Text.Trim());
        if (Directory.Exists(outDir))
        {
            if (MessageBox.Show($"Folder '{txtId.Text}' already exists. Overwrite?",
                    "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                DialogResult = DialogResult.None;
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(outDir);

            // Write mod.xml
            var manifest = new ModManifest
            {
                Id               = txtId.Text.Trim(),
                Name             = txtName.Text.Trim(),
                Author           = txtAuthor.Text.Trim(),
                Version          = txtVersion.Text.Trim(),
                Description      = txtDescription.Text.Trim(),
                CompatibilityRaw = _activeVariant.ToString()
            };
            using (var stream = File.Create(Path.Combine(outDir, "mod.xml")))
                _manifestSerializer.Serialize(stream, manifest);

            if (_sourcePath == null)
            {
                MessageBox.Show($"Mod folder created (empty):\n{outDir}", "Done",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Format A -- copy .bnd archives
            if (rbFormatA.Checked || rbFormatAB.Checked)
            {
                var bndFiles = new (string Name, CheckBox Chk)[]
                {
                    ("gamedata.bnd",    chkGamedata),
                    ("gfxdata.bnd",     chkGfxData),
                    ("sfxdata.bnd",     chkSfxData),
                    ("sfxdatahigh.bnd", chkSfxHigh),
                };
                foreach (var (name, chk) in bndFiles)
                {
                    if (!chk.Checked) continue;
                    var src = Path.Combine(_sourcePath, name);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(outDir, name), overwrite: true);
                }
            }

            // Format B -- copy selected loose files
            if (rbFormatB.Checked || rbFormatAB.Checked)
            {
                foreach (var item in chkLooseFiles.CheckedItems.Cast<string>())
                {
                    var src  = Path.Combine(_sourcePath, item.Replace('/', Path.DirectorySeparatorChar));
                    var dest = Path.Combine(outDir,      item.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, overwrite: true);
                }
            }

            MessageBox.Show($"Mod created:\n{outDir}", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = DialogResult.None;
        }
    }

    // --- Helpers -------------------------------------------------------------

    private static Label MakeLabel(string text) =>
        new() { Text = text, Width = 440, Height = 18, Font = new Font("Segoe UI", 8.5f) };

    private static TextBox MakeTextBox(string text = "") =>
        new() { Text = text, Width = 440 };
}
