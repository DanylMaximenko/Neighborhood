namespace Neighborhood.Game;

internal sealed class GameVariantSelectionForm : Form
{
    private readonly Button _btnNfh1;
    private readonly Button _btnNfh2;

    public Neighborhood.Shared.Models.GameVariant SelectedVariant { get; private set; } = Neighborhood.Shared.Models.GameVariant.NFH1;

    public GameVariantSelectionForm()
    {
        Text          = "Neighborhood";
        Size          = new Size(360, 180);
        MinimumSize   = new Size(320, 160);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = true;

        var titleLabel = new Label
        {
            Text      = "Choose game variant",
            Dock      = DockStyle.Top,
            Height    = 48,
            TextAlign  = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 11f, FontStyle.Bold)
        };

        var subtitleLabel = new Label
        {
            Text      = "Start Neighborhood as NFH1 or NFH2.",
            Dock      = DockStyle.Top,
            Height    = 28,
            TextAlign  = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 8.5f)
        };

        _btnNfh1 = new Button
        {
            Text   = "NFH1",
            Width  = 110,
            Height = 36,
            DialogResult = DialogResult.OK
        };

        _btnNfh2 = new Button
        {
            Text   = "NFH2",
            Width  = 110,
            Height = 36,
            DialogResult = DialogResult.OK
        };

        _btnNfh1.Click += (_, _) =>
        {
            SelectedVariant = Neighborhood.Shared.Models.GameVariant.NFH1;
            Close();
        };

        _btnNfh2.Click += (_, _) =>
        {
            SelectedVariant = Neighborhood.Shared.Models.GameVariant.NFH2;
            Close();
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            Padding = new Padding(0)
        };
        buttonPanel.Controls.Add(_btnNfh1);
        buttonPanel.Controls.Add(_btnNfh2);

        var buttonHost = new Panel { Dock = DockStyle.Fill };
        buttonPanel.Location = new Point((ClientSize.Width - 240) / 2, 0);
        buttonPanel.Size = new Size(240, 44);
        buttonHost.Controls.Add(buttonPanel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(subtitleLabel, 0, 1);
        layout.Controls.Add(buttonHost, 0, 2);

        Controls.Add(layout);

        AcceptButton = _btnNfh1;
        CancelButton = null;
    }
}