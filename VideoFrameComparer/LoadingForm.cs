namespace VideoFrameComparer;

internal sealed class LoadingForm : Form
{
    private static readonly string[] SpinnerFrames = ["◜", "◠", "◝", "◞", "◡", "◟"];
    private readonly Label _spinnerLabel;
    private readonly System.Windows.Forms.Timer _spinnerTimer;
    private int _spinnerFrameIndex;

    public LoadingForm()
    {
        Text = "Opening Project";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 104);
        BackColor = Color.FromArgb(28, 28, 28);
        TopMost = true;

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Padding = new Padding(14, 10, 14, 0),
            Text = "Loading project..."
        };

        _spinnerLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.FromArgb(135, 192, 255),
            Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = SpinnerFrames[0]
        };

        var hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(170, 170, 170),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            Padding = new Padding(14, 0, 14, 10),
            Text = "Preparing timelines and previews",
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(hintLabel);
        Controls.Add(_spinnerLabel);
        Controls.Add(titleLabel);

        _spinnerTimer = new System.Windows.Forms.Timer { Interval = 90 };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerFrameIndex = (_spinnerFrameIndex + 1) % SpinnerFrames.Length;
            _spinnerLabel.Text = SpinnerFrames[_spinnerFrameIndex];
        };
        _spinnerTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _spinnerTimer.Stop();
        _spinnerTimer.Dispose();
        base.OnFormClosed(e);
    }
}
