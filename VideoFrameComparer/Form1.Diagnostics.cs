using System.Drawing.Drawing2D;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace VideoFrameComparer;

public partial class Form1
{
    private void InitializeDiagnosticsUi()
    {
        _diagnosticsTopBarButton = CreateLayoutButton(
            location: new DrawingPoint(0, 12),
            onClick: ToggleDiagnosticsPanel);
        _diagnosticsTopBarButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _diagnosticsIcon = CreateDiagnosticsIcon(Color.FromArgb(238, 238, 238));
        _diagnosticsTopBarButton.Image = _diagnosticsIcon;
        topBarPanel.Controls.Add(_diagnosticsTopBarButton);
        _diagnosticsTopBarButton.BringToFront();

        _diagnosticsPanel = new Panel
        {
            BackColor = Color.FromArgb(18, 18, 18),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            Size = new DrawingSize(420, 150)
        };

        _diagnosticsLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(10, 10, 10, 10),
            AutoSize = false
        };

        _copyDiagnosticsButton = new Button
        {
            Text = "Copy",
            AutoSize = false,
            Size = new DrawingSize(72, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Standard,
            UseVisualStyleBackColor = true
        };
        _copyDiagnosticsButton.Click += (_, _) => CopyDiagnosticsToClipboard();

        _diagnosticsPanel.Controls.Add(_diagnosticsLabel);
        _diagnosticsPanel.Controls.Add(_copyDiagnosticsButton);
        Controls.Add(_diagnosticsPanel);
        _diagnosticsPanel.BringToFront();

        UpdateDiagnosticsPanelText();
        LayoutTopBarButtons();
        LayoutDiagnosticsPanel();
    }

    private void ToggleDiagnosticsPanel()
    {
        if (_diagnosticsPanel is null)
        {
            return;
        }

        _diagnosticsPanel.Visible = !_diagnosticsPanel.Visible;
        if (_diagnosticsPanel.Visible)
        {
            _diagnosticsPanel.BringToFront();
        }
        LayoutDiagnosticsPanel();
    }

    private void LayoutDiagnosticsPanel()
    {
        if (_diagnosticsPanel is null || _copyDiagnosticsButton is null)
        {
            return;
        }

        int rightMargin = 14;
        int topMargin = topBarPanel.Bottom + 4;
        int x = Math.Max(0, ClientSize.Width - _diagnosticsPanel.Width - rightMargin);
        _diagnosticsPanel.Location = new DrawingPoint(x, topMargin);

        int buttonMargin = 10;
        _copyDiagnosticsButton.Location = new DrawingPoint(
            _diagnosticsPanel.ClientSize.Width - _copyDiagnosticsButton.Width - buttonMargin,
            _diagnosticsPanel.ClientSize.Height - _copyDiagnosticsButton.Height - buttonMargin);

        if (_diagnosticsPanel.Visible)
        {
            _diagnosticsPanel.BringToFront();
        }
    }

    private void UpdateDiagnosticsPanelText()
    {
        if (_diagnosticsLabel is null)
        {
            return;
        }

        string text =
            $"FFmpeg: {_diagnosticsFfmpegStatus}\r\n" +
            $"Install: {_diagnosticsInstallStatus}\r\n" +
            $"Encoder: {_diagnosticsEncoderStatus}\r\n" +
            $"Last export: {_diagnosticsLastExportStatus}\r\n" +
            $"Last error: {_diagnosticsLastError}";

        if (!string.IsNullOrWhiteSpace(_diagnosticsCopyStatus))
        {
            text += $"\r\n{_diagnosticsCopyStatus}";
        }

        _diagnosticsLabel.Text = text;
    }

    private void RecordDiagnostics(
        string? ffmpegStatus = null,
        string? installStatus = null,
        string? exportStatus = null,
        string? error = null)
    {
        if (!string.IsNullOrWhiteSpace(ffmpegStatus))
        {
            _diagnosticsFfmpegStatus = ffmpegStatus;
        }

        if (!string.IsNullOrWhiteSpace(installStatus))
        {
            _diagnosticsInstallStatus = installStatus;
        }

        if (!string.IsNullOrWhiteSpace(exportStatus))
        {
            _diagnosticsLastExportStatus = exportStatus;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _diagnosticsLastError = error;
        }

        _diagnosticsCopyStatus = string.Empty;
        UpdateDiagnosticsPanelText();
    }

    private void CopyDiagnosticsToClipboard()
    {
        string text =
            $"FFmpeg: {_diagnosticsFfmpegStatus}\r\n" +
            $"Install: {_diagnosticsInstallStatus}\r\n" +
            $"Encoder: {_diagnosticsEncoderStatus}\r\n" +
            $"Last export: {_diagnosticsLastExportStatus}\r\n" +
            $"Last error: {_diagnosticsLastError}";

        try
        {
            Clipboard.SetText(text);
            _diagnosticsCopyStatus = "Copied";
        }
        catch (Exception ex)
        {
            _diagnosticsCopyStatus = $"Copy failed: {ex.Message}";
        }

        UpdateDiagnosticsPanelText();
    }

    private static Bitmap CreateDiagnosticsIcon(Color strokeColor)
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(strokeColor, 1.4f);
        using (GraphicsPath backShape = CreateRoundedRectPath(new Rectangle(2, 4, 8, 8), 2))
        {
            g.DrawPath(pen, backShape);
        }

        using (GraphicsPath frontShape = CreateRoundedRectPath(new Rectangle(8, 7, 8, 8), 2))
        {
            g.DrawPath(pen, frontShape);
        }

        return bmp;
    }
}
