using DrawingSize = System.Drawing.Size;

namespace VideoFrameComparer;

public partial class Form1
{
    private static bool TryCaptureWindowBitmap(IntPtr windowHandle, out Bitmap? bitmap)
    {
        bitmap = null;
        if (windowHandle == IntPtr.Zero || !IsWindowVisible(windowHandle) || IsIconic(windowHandle))
        {
            return false;
        }

        if (!TryGetClientScreenRect(windowHandle, out RECT clientRect))
        {
            return false;
        }

        int clientWidth = clientRect.Right - clientRect.Left;
        int clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            return false;
        }

        // Ask Windows for the target window's client area directly to avoid DPI/crop mismatches.
        var clientBitmap = new Bitmap(clientWidth, clientHeight);
        using (Graphics graphics = Graphics.FromImage(clientBitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (PrintWindow(windowHandle, hdc, PrintWindowClientOnly | PrintWindowRenderFullContent))
                {
                    bitmap = clientBitmap;
                    return true;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        var fallbackBitmap = new Bitmap(clientWidth, clientHeight);
        using (Graphics graphics = Graphics.FromImage(fallbackBitmap))
        {
            graphics.CopyFromScreen(clientRect.Left, clientRect.Top, 0, 0, new DrawingSize(clientWidth, clientHeight), CopyPixelOperation.SourceCopy);
        }
        bitmap = fallbackBitmap;
        return bitmap is not null;
    }

    private static bool TryGetClientScreenRect(IntPtr windowHandle, out RECT clientRect)
    {
        clientRect = default;
        if (!GetClientRect(windowHandle, out RECT clientLocalRect))
        {
            return false;
        }

        POINT clientTopLeft = new() { X = clientLocalRect.Left, Y = clientLocalRect.Top };
        POINT clientBottomRight = new() { X = clientLocalRect.Right, Y = clientLocalRect.Bottom };
        if (!ClientToScreen(windowHandle, ref clientTopLeft) || !ClientToScreen(windowHandle, ref clientBottomRight))
        {
            return false;
        }

        clientRect = new RECT
        {
            Left = clientTopLeft.X,
            Top = clientTopLeft.Y,
            Right = clientBottomRight.X,
            Bottom = clientBottomRight.Y
        };
        return true;
    }

    private static IReadOnlyList<WindowChoice> GetAvailableWindows()
    {
        var windows = new List<WindowChoice>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
            {
                return true;
            }

            int length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return true;
            }

            var builder = new System.Text.StringBuilder(length + 1);
            int copied = GetWindowText(handle, builder, builder.Capacity);
            if (copied <= 0)
            {
                return true;
            }
            string title = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            windows.Add(new WindowChoice(handle, title));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(choice => choice.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private readonly record struct WindowChoice(IntPtr Handle, string Title)
    {
        public override string ToString() => Title;
    }

    private sealed class FfmpegInstallProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;

        public FfmpegInstallProgressForm()
        {
            Text = "Installing FFmpeg";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;
            Width = 500;
            Height = 130;
            BackColor = Color.FromArgb(28, 28, 28);

            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = Color.Gainsboro,
                Padding = new Padding(12, 10, 12, 0),
                Text = "Preparing installer..."
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(12, 0, 12, 0),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28
            };

            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
        }

        public void UpdateStatus(string status)
        {
            _statusLabel.Text = status;
            _statusLabel.Refresh();
        }
    }

    private sealed class TrimExportProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly Label _countLabel;
        private readonly ProgressBar _progressBar;
        private readonly int _totalJobs;

        public TrimExportProgressForm(int totalJobs)
        {
            _totalJobs = Math.Max(1, totalJobs);

            Text = "Saving Trimmed Videos";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;
            Width = 460;
            Height = 130;
            BackColor = Color.FromArgb(28, 28, 28);

            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.Gainsboro,
                Padding = new Padding(12, 8, 12, 0),
                Text = "Preparing export..."
            };
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(12, 0, 12, 0),
                Minimum = 0,
                Maximum = _totalJobs * 1000,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            _countLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                ForeColor = Color.FromArgb(170, 170, 170),
                Padding = new Padding(12, 4, 12, 0),
                Text = $"0 / {_totalJobs:n0} jobs"
            };

            Controls.Add(_countLabel);
            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
        }

        public void UpdateProgress(int completedJobs, double currentJobRatio, string status)
        {
            int safeCompletedJobs = Math.Clamp(completedJobs, 0, _totalJobs);
            double safeRatio = Math.Clamp(currentJobRatio, 0d, 1d);
            int barValue = Math.Clamp((safeCompletedJobs * 1000) + (int)Math.Round(safeRatio * 1000d), 0, _progressBar.Maximum);
            _statusLabel.Text = status;
            _progressBar.Value = barValue;
            int shownCompleted = Math.Min(_totalJobs, safeCompletedJobs + (safeRatio >= 0.999 ? 1 : 0));
            _countLabel.Text = $"{shownCompleted:n0} / {_totalJobs:n0} jobs";
            _statusLabel.Refresh();
            _progressBar.Refresh();
            _countLabel.Refresh();
        }
    }

    private sealed class WindowPickerForm : Form
    {
        private readonly ListBox _windowList;
        private readonly Button _refreshButton;
        private readonly Button _selectButton;
        private readonly Button _cancelButton;

        public WindowChoice? SelectedWindow { get; private set; }

        public WindowPickerForm()
        {
            Text = "Select App Window";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Width = 600;
            Height = 430;

            _windowList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
            _windowList.DoubleClick += (_, _) => SelectAndClose();

            _refreshButton = new Button
            {
                Text = "Refresh",
                Width = 88,
                Height = 28
            };
            _refreshButton.Click += (_, _) => PopulateWindows();

            _selectButton = new Button
            {
                Text = "Use Window",
                Width = 108,
                Height = 28
            };
            _selectButton.Click += (_, _) => SelectAndClose();

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = 88,
                Height = 28
            };
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8)
            };
            buttonRow.Controls.Add(_cancelButton);
            buttonRow.Controls.Add(_selectButton);
            buttonRow.Controls.Add(_refreshButton);

            Controls.Add(_windowList);
            Controls.Add(buttonRow);
            PopulateWindows();
        }

        private void PopulateWindows()
        {
            _windowList.BeginUpdate();
            _windowList.Items.Clear();
            foreach (WindowChoice choice in GetAvailableWindows())
            {
                _windowList.Items.Add(choice);
            }
            _windowList.EndUpdate();

            if (_windowList.Items.Count > 0)
            {
                _windowList.SelectedIndex = 0;
            }
        }

        private void SelectAndClose()
        {
            if (_windowList.SelectedItem is not WindowChoice selected)
            {
                return;
            }

            SelectedWindow = selected;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

}
