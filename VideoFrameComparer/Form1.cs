using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using System.Text.Json;

namespace VideoFrameComparer;

public partial class Form1 : Form
{
    private const string PlayIcon = "▶";
    private const string PauseIcon = "❚❚";
    private const int LargeStep = 20;
    private const int MarkerSnapThresholdPixels = 24;
    private const int MarkerSnapReleaseThresholdPixels = 36;
    private const int GlobalPlayheadSnapThresholdPixels = 12;
    private const int TrackDragMarkerSnapThresholdPixels = 16;
    private const int TrackDragMarkerSnapReleaseThresholdPixels = 24;
    private const int MarkerSelectionThresholdPixels = 18;
    private const int SharedCommentSelectionThresholdPixels = 16;
    private const int CommentDeleteGlyphSize = 12;
    private const int CommentDeleteGlyphPadding = 6;
    private const int TimelineEdgePadding = 8;
    private const float WheelZoomStep = 1.1f;
    private const float MinZoomMultiplier = 1.0f;
    private const float MaxZoomMultiplier = 8.0f;
    private const int TopBarHeight = 52;
    private const int BottomBarHeight = 104;
    private const int PreviewTitleHeight = 28;
    private const int PreviewMarkerHeight = 24;
    private const int PreviewTimelineHeight = 52;
    private const int BetweenPreviewGap = 1;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int GlobalToggleDebounceMs = 250;
    private const int ExportWriterQualityPercent = 85;
    private const string FfmpegEncoderNvenc = "h264_nvenc";
    private const string FfmpegEncoderHevcNvenc = "hevc_nvenc";
    private const string FfmpegEncoderX264 = "libx264";
    private const int NvencH264MaxWidth = 4096;
    private const int NvencH264MaxHeight = 4096;
    private const int NvencHevcMaxWidth = 8192;
    private const int NvencHevcMaxHeight = 8192;
    private static readonly string[] SupportedExtensions = [".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v"];
    private static readonly float[] PlaybackSpeedOptions = [0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f];
    private const string VideoFileDialogFilter =
        "Video Files (*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.m4v)|*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.m4v|All Files (*.*)|*.*";

    private readonly LibVLC _libVlc;
    private readonly string _projectFilePath;
    private readonly VideoTrack _leftTrack;
    private readonly VideoTrack _rightTrack;
    private VideoTrack? _activeTrack;
    private readonly System.Windows.Forms.Timer _playbackTimer;
    private readonly System.Windows.Forms.Timer _windowSourceTimer;
    private bool _isPlaying;
    private bool _isUpdatingMasterTimeline;
    private bool _isUpdatingTrackTimelines;
    private float _playbackSpeedMultiplier = 1.0f;
    private bool _isRestoringSession;
    private long _lastGlobalToggleTick;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private HookProc? _keyboardHookProc;
    private readonly List<SharedComment> _sharedComments = [];
    private int? _selectedSharedCommentId;
    private Panel? _sharedCommentsPanel;
    private ListBox? _sharedCommentsListBox;
    private Panel? _sharedCommentMarkerPanel;
    private Button? _toggleCommentsTopBarButton;
    private Image? _commentsActiveIcon;
    private Image? _commentsInactiveIcon;
    private int _nextSharedCommentId = 1;
    private bool _isUpdatingSharedCommentsList;
    private bool _isCommentsSidebarCollapsed;
    private bool _suppressAutoWindowResize;
    private PictureBox? _alignmentOverlayPictureBox;
    private Panel? _clipTimelineViewport;
    private Panel? _clipTimelineCanvas;
    private const int ClipTimelineHeaderHeight = 18;
    private const int ClipTimelineTrackHeight = 18;
    private const int ClipTimelineTrackGap = 2;
    private const int ClipTimelineCanvasHeight = 60;
    private const double TimelineMinPaddingFactor = 1.2;
    private const double TimelineZoomMax = 8.0;
    private const double TimelineZoomStep = 1.2;
    private const int PlayheadDragThresholdPixels = 14;
    private VideoTrack? _draggingTimelineTrack;
    private VideoTrack? _hoverTimelineTrack;
    private int _dragStartMouseX;
    private int _dragStartTrackOffset;
    private bool _isDraggingPlayhead;
    private double _timelinePixelsPerFrame = 1.0;
    private long _lastDragStatusTick;
    private bool _showTimelineDebug;
    private int? _hoverDeleteCommentId;
    private VideoTrack? _snapPreviewTrack;
    private int? _snapPreviewMarker;
    private int? _trackDragSnapOwnMarker;
    private int? _trackDragSnapTargetGlobalMarkerFrame;
    private Button? _layoutSideBySideButton;
    private Button? _layoutStackedButton;
    private Image? _layoutStackedActiveIcon;
    private Image? _layoutStackedInactiveIcon;
    private Image? _layoutSideBySideActiveIcon;
    private Image? _layoutSideBySideInactiveIcon;
    private Button? _helpTopBarButton;
    private Image? _helpIcon;
    private Button? _diagnosticsTopBarButton;
    private Image? _diagnosticsIcon;
    private Panel? _diagnosticsPanel;
    private Label? _diagnosticsLabel;
    private Button? _copyDiagnosticsButton;
    private readonly ToolTip _uiToolTip = new ToolTip();
    private Panel? _transportHostPanel;
    private Panel? _transportLeftPanel;
    private Panel? _transportRightPanel;
    private int? _globalTrimInFrame;
    private int? _globalTrimOutFrame;
    private Button? _saveTrimmedVideosTopBarButton;
    private Button? _saveCombinedVideoTopBarButton;
    private bool _isSavingTrimmedVideos;
    private bool _isRepairingPreviewFrames;
    private string _diagnosticsFfmpegStatus = "Not checked";
    private string _diagnosticsInstallStatus = "Not run";
    private string _diagnosticsLastExportStatus = "None";
    private string _diagnosticsLastError = "None";
    private string _diagnosticsCopyStatus = string.Empty;
    private string _diagnosticsEncoderStatus = "Unknown";
    private string? _cachedFfmpegEncodersOutput;
    // Export quality is fixed to a high-quality preset (no UI toggle).

    public Form1(string projectFilePath)
    {
        _projectFilePath = projectFilePath;
        AppLog.Write("App starting.");
        Core.Initialize();
        AppLog.Write("LibVLCSharp core initialized.");
        _libVlc = new LibVLC("--no-video-title-show");
        AppLog.Write("LibVLC instance created.");

        InitializeComponent();
        topBarPanel.Height = TopBarHeight;
        useWindowSourceButton.Location = new DrawingPoint(useWindowSourceButton.Left, 14);
        helpLabel.Visible = false;
        restoreVideoSourceButton.Visible = false;
        alignmentModeCheckBox.Visible = false;
        InitializeLayoutQuickButtons();
        bottomTransportPanel.Height = BottomBarHeight;
        playbackStatusLabel.ForeColor = Color.FromArgb(145, 145, 145);
        playbackStatusLabel.Font = new Font("Segoe UI", 8f, FontStyle.Regular);
        InitializeSharedCommentsUi();
        InitializeAlignmentOverlay();

        _leftTrack = new VideoTrack(
            name: "Video A",
            hostPanel: leftHostPanel,
            imagePanel: leftImagePanel,
            pictureBox: leftPictureBox,
            footerPanel: leftFooterPanel,
            markerPanel: leftMarkerPanel,
            timeline: leftTimeline,
            infoLabel: leftInfoLabel,
            titleLabel: leftTitleLabel);
        _rightTrack = new VideoTrack(
            name: "Video B",
            hostPanel: rightHostPanel,
            imagePanel: rightImagePanel,
            pictureBox: rightPictureBox,
            footerPanel: rightFooterPanel,
            markerPanel: rightMarkerPanel,
            timeline: rightTimeline,
            infoLabel: rightInfoLabel,
            titleLabel: rightTitleLabel);
        InitializeClipTimelineUi();

        _leftTrack.AttachPlaybackView();
        _rightTrack.AttachPlaybackView();

        HookTrack(_leftTrack);
        HookTrack(_rightTrack);
        ConfigureTrackLayout(_leftTrack);
        ConfigureTrackLayout(_rightTrack);
        UpdateEmptyHintState(_leftTrack);
        UpdateEmptyHintState(_rightTrack);
        leftImagePanel.Resize += (_, _) => ApplyScale(_leftTrack);
        rightImagePanel.Resize += (_, _) => ApplyScale(_rightTrack);
        _playbackTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _windowSourceTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _windowSourceTimer.Tick += WindowSourceTimer_Tick;

        speedComboBox.SelectedIndex = 3;
        layoutComboBox.SelectedIndex = 0;
        SetActiveTrack(_leftTrack);
        UpdateLayoutMode();
        UpdateVideoFit();
        UpdateMasterTimelineBounds();
        UpdatePlaybackStatus();
        Text = $"Video Frame Comparer - {Path.GetFileNameWithoutExtension(_projectFilePath)}";
        RestoreSession();
        speedComboBox.SelectedIndex = 3;
        _playbackSpeedMultiplier = PlaybackSpeedOptions[3];
        _isCommentsSidebarCollapsed = _sharedComments.Count == 0;
        UpdateLayoutMode();
        UpdateCommentsSidebarButtonText();
        topBarPanel.Resize += (_, _) => LayoutTopBarButtons();
        topBarPanel.Resize += (_, _) => LayoutTopBarControls();
        LayoutTopBarButtons();
        LayoutTopBarControls();
        InitializeHelpUi();
        InitializeDiagnosticsUi();
        InitializeTrimTopBarButtons();
        ConfigureTooltips();
        UpdateTrimActionButtonsState();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        AppLog.Write("App closing.");
        StopPlayback();
        SaveSession();
        _playbackTimer.Dispose();
        _windowSourceTimer.Dispose();
        _leftTrack.Dispose();
        _rightTrack.Dispose();
        DisposeUiImages();
        _uiToolTip.Dispose();
        _libVlc.Dispose();
        base.OnFormClosing(e);
    }

    private void DisposeUiImages()
    {
        DisposeImage(ref _layoutStackedActiveIcon);
        DisposeImage(ref _layoutStackedInactiveIcon);
        DisposeImage(ref _layoutSideBySideActiveIcon);
        DisposeImage(ref _layoutSideBySideInactiveIcon);
        DisposeImage(ref _commentsActiveIcon);
        DisposeImage(ref _commentsInactiveIcon);
        DisposeImage(ref _helpIcon);
        DisposeImage(ref _diagnosticsIcon);
    }

    private static void DisposeImage(ref Image? image)
    {
        if (image is null)
        {
            return;
        }

        image.Dispose();
        image = null;
    }

    private void InitializeAlignmentOverlay()
    {
        _alignmentOverlayPictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = false
        };
        _alignmentOverlayPictureBox.Click += (_, _) => SetActiveTrack(_leftTrack);
        leftImagePanel.Controls.Add(_alignmentOverlayPictureBox);
        _alignmentOverlayPictureBox.BringToFront();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _keyboardHookProc = KeyboardHookCallback;
        IntPtr moduleHandle = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            AppLog.WriteError("Failed to install global keyboard hook.");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == (IntPtr)WmKeyDown)
        {
            KBDLLHOOKSTRUCT keyInfo = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (keyInfo.vkCode == (uint)Keys.PageDown)
            {
                BeginInvoke(new Action(HandleGlobalPlayPauseHotkey));
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void HandleGlobalPlayPauseHotkey()
    {
        long now = Environment.TickCount64;
        if (now - _lastGlobalToggleTick < GlobalToggleDebounceMs)
        {
            return;
        }

        _lastGlobalToggleTick = now;
        if (_isPlaying)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void HookTrack(VideoTrack track)
    {
        track.PictureBox.Click += (_, _) => SetActiveTrack(track);
        track.ImagePanel.Click += (_, _) => SetActiveTrack(track);
        track.HostPanel.Click += (_, _) => SetActiveTrack(track);
        track.PlaybackView.Click += (_, _) => SetActiveTrack(track);
        track.PictureBox.MouseWheel += (_, e) => HandleTrackMouseWheel(track, e);
        track.ImagePanel.MouseWheel += (_, e) => HandleTrackMouseWheel(track, e);
        track.HostPanel.MouseWheel += (_, e) => HandleTrackMouseWheel(track, e);
        track.PlaybackView.MouseWheel += (_, e) => HandleTrackMouseWheel(track, e);
        track.PictureBox.AllowDrop = true;
        track.ImagePanel.AllowDrop = true;
        track.HostPanel.AllowDrop = true;
        track.PlaybackView.AllowDrop = true;
        track.PictureBox.DragEnter += (_, e) => HandleVideoDragEnter(e);
        track.ImagePanel.DragEnter += (_, e) => HandleVideoDragEnter(e);
        track.HostPanel.DragEnter += (_, e) => HandleVideoDragEnter(e);
        track.PlaybackView.DragEnter += (_, e) => HandleVideoDragEnter(e);
        track.PictureBox.DragDrop += (_, e) => HandleVideoDragDrop(track, e);
        track.ImagePanel.DragDrop += (_, e) => HandleVideoDragDrop(track, e);
        track.HostPanel.DragDrop += (_, e) => HandleVideoDragDrop(track, e);
        track.PlaybackView.DragDrop += (_, e) => HandleVideoDragDrop(track, e);
        track.Timeline.Scroll += (_, _) =>
        {
            if (_isUpdatingTrackTimelines)
            {
                return;
            }
            SeekToFrame(track, track.Timeline.Value);
        };
        track.Timeline.MouseDown += (_, _) => track.IsScrubbingTimeline = true;
        track.Timeline.MouseUp += (_, _) =>
        {
            track.IsScrubbingTimeline = false;
            track.SnapLockedMarker = null;
            track.MarkerPanel.Invalidate();
        };
        track.Timeline.MouseDown += (_, _) => SetActiveTrack(track);
        track.MarkerPanel.Paint += (_, e) => DrawMarkers(track, e.Graphics);
        track.MarkerPanel.Resize += (_, _) => track.MarkerPanel.Invalidate();
        track.MarkerPanel.MouseDown += (_, e) => HandleMarkerPanelMouseDown(track, e);
        track.ImagePanel.Paint += (_, e) => DrawEmptyTrackHint(track, e.Graphics);
    }

    private static void ConfigureTrackLayout(VideoTrack track)
    {
        track.InfoLabel.Visible = false;
        track.Timeline.AutoSize = false;
        track.Timeline.Height = PreviewTimelineHeight;
        track.MarkerPanel.Height = PreviewMarkerHeight;
        track.FooterPanel.Height = 0;
        track.FooterPanel.Visible = false;
        track.Timeline.Visible = false;
        track.MarkerPanel.Visible = false;
        track.MarkerPanel.BorderStyle = BorderStyle.None;
        track.PictureBox.Visible = false;
        if (track.FooterPanel.Parent is TableLayoutPanel trackLayout && trackLayout.RowStyles.Count >= 3)
        {
            trackLayout.RowStyles[2].Height = 0f;
            trackLayout.RowStyles[2].SizeType = SizeType.Absolute;
        }
        track.ApplyTemporarySourceVisualState();
    }

    private void InitializeSharedCommentsUi()
    {
        var sidebar = new Panel
        {
            BackColor = Color.FromArgb(30, 30, 30),
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 8, 8, 8)
        };
        _sharedCommentsPanel = sidebar;

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawVariable
        };
        listBox.MeasureItem += SharedCommentsListBox_MeasureItem;
        listBox.DrawItem += SharedCommentsListBox_DrawItem;
        listBox.SelectedIndexChanged += SharedCommentsListBox_SelectedIndexChanged;
        listBox.MouseDown += SharedCommentsListBox_MouseDown;
        listBox.MouseMove += SharedCommentsListBox_MouseMove;
        listBox.MouseLeave += SharedCommentsListBox_MouseLeave;
        _sharedCommentsListBox = listBox;

        sidebar.Controls.Add(listBox);

        if (videosTableLayout.ColumnCount < 3)
        {
            videosTableLayout.ColumnCount = 3;
        }
        while (videosTableLayout.ColumnStyles.Count < 3)
        {
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320f));
        }
        videosTableLayout.Controls.Add(sidebar, 2, 0);

        _sharedCommentMarkerPanel = new Panel
        {
            BackColor = Color.FromArgb(52, 52, 52),
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            Margin = new Padding(3, 0, 0, 0)
        };
        _sharedCommentMarkerPanel.Paint += (_, e) => DrawSharedCommentMarkers(e.Graphics);
        _sharedCommentMarkerPanel.MouseDown += SharedCommentMarkerPanel_MouseDown;

        _toggleCommentsTopBarButton = CreateLayoutButton(
            location: new DrawingPoint(108, 18),
            onClick: () =>
        {
            _isCommentsSidebarCollapsed = !_isCommentsSidebarCollapsed;
            _suppressAutoWindowResize = true;
            try
            {
                UpdateLayoutMode();
            }
            finally
            {
                _suppressAutoWindowResize = false;
            }
            UpdateCommentsSidebarButtonText();
        });
        _commentsActiveIcon = CreateCommentsIcon(Color.FromArgb(238, 238, 238));
        _commentsInactiveIcon = CreateCommentsIcon(Color.FromArgb(140, 140, 140));
        _toggleCommentsTopBarButton.Image = _isCommentsSidebarCollapsed ? _commentsInactiveIcon : _commentsActiveIcon;
        _toggleCommentsTopBarButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topBarPanel.Controls.Add(_toggleCommentsTopBarButton);
        _toggleCommentsTopBarButton.BringToFront();
        LayoutTopBarButtons();
    }

    private void LayoutTopBarButtons()
    {
        if (_toggleCommentsTopBarButton is null || _helpTopBarButton is null)
        {
            return;
        }

        int rightMargin = 14;
        int y = 12;
        int gap = 6;
        int commentsX = Math.Max(0, topBarPanel.ClientSize.Width - _toggleCommentsTopBarButton.Width - rightMargin);
        int helpX = commentsX;
        if (_diagnosticsTopBarButton is not null)
        {
            int diagnosticsX = Math.Max(0, commentsX - _diagnosticsTopBarButton.Width - gap);
            _diagnosticsTopBarButton.Location = new DrawingPoint(diagnosticsX, y);
            _diagnosticsTopBarButton.BringToFront();
            helpX = diagnosticsX;
        }
        helpX = Math.Max(0, helpX - _helpTopBarButton.Width - gap);
        _helpTopBarButton.Location = new DrawingPoint(helpX, y);
        _toggleCommentsTopBarButton.Location = new DrawingPoint(
            commentsX,
            y);
        _helpTopBarButton.BringToFront();
        _toggleCommentsTopBarButton.BringToFront();
        LayoutDiagnosticsPanel();
    }

    private void LayoutTopBarControls()
    {
        int y = useWindowSourceButton.Top;
        int nextX = useWindowSourceButton.Right + 12;

        if (alignmentModeCheckBox.Visible)
        {
            alignmentModeCheckBox.Location = new DrawingPoint(nextX, useWindowSourceButton.Top + 3);
            nextX = alignmentModeCheckBox.Right + 14;
        }
        else
        {
            alignmentModeCheckBox.Location = new DrawingPoint(nextX, useWindowSourceButton.Top + 3);
        }

        if (_saveTrimmedVideosTopBarButton is not null)
        {
            _saveTrimmedVideosTopBarButton.Location = new DrawingPoint(nextX, y);
            _saveTrimmedVideosTopBarButton.BringToFront();
            nextX = _saveTrimmedVideosTopBarButton.Right + 8;
        }

        if (_saveCombinedVideoTopBarButton is not null)
        {
            _saveCombinedVideoTopBarButton.Location = new DrawingPoint(nextX, y);
            _saveCombinedVideoTopBarButton.BringToFront();
        }

        alignmentModeCheckBox.BringToFront();
    }

    private void InitializeTrimTopBarButtons()
    {
        _saveTrimmedVideosTopBarButton = CreateTopBarActionButton("Save Trimmed Videos", SaveTrimmedVideosButton_Click);
        _saveTrimmedVideosTopBarButton.Size = new DrawingSize(148, 24);
        _saveCombinedVideoTopBarButton = CreateTopBarActionButton("Save Combined Video", SaveCombinedVideoButton_Click);
        _saveCombinedVideoTopBarButton.Size = new DrawingSize(146, 24);
        topBarPanel.Controls.Add(_saveTrimmedVideosTopBarButton);
        topBarPanel.Controls.Add(_saveCombinedVideoTopBarButton);
        LayoutTopBarControls();
    }

    private static Button CreateTopBarActionButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Size = new DrawingSize(74, 24),
            FlatStyle = FlatStyle.Standard,
            UseVisualStyleBackColor = true,
            TabStop = false
        };
        button.Click += onClick;
        return button;
    }

    private static Bitmap CreateCommentsIcon(Color strokeColor)
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(strokeColor, 1.5f);
        using (GraphicsPath topBubble = CreateRoundedRectPath(new Rectangle(2, 2, 10, 8), 2))
        {
            g.DrawPath(pen, topBubble);
        }
        g.DrawLine(pen, 5, 10, 4, 14);
        using (GraphicsPath bottomBubble = CreateRoundedRectPath(new Rectangle(6, 7, 10, 8), 2))
        {
            g.DrawPath(pen, bottomBubble);
        }
        g.DrawLine(pen, 13, 15, 12, 17);
        return bmp;
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        int arcWidth = Math.Min(diameter, rect.Width);
        int arcHeight = Math.Min(diameter, rect.Height);
        int right = rect.X + rect.Width - arcWidth;
        int bottom = rect.Y + rect.Height - arcHeight;

        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, arcWidth, arcHeight, 180, 90);
        path.AddArc(right, rect.Y, arcWidth, arcHeight, 270, 90);
        path.AddArc(right, bottom, arcWidth, arcHeight, 0, 90);
        path.AddArc(rect.X, bottom, arcWidth, arcHeight, 90, 90);
        path.CloseFigure();
        return path;
    }


    private static Rectangle GetCommentDeleteBounds(Rectangle itemBounds)
    {
        int size = CommentDeleteGlyphSize;
        int x = itemBounds.Right - CommentDeleteGlyphPadding - size;
        int y = itemBounds.Y + Math.Max(0, (itemBounds.Height - size) / 2);
        return new Rectangle(x, y, size, size);
    }

    private void RemoveSharedComment(int commentId)
    {
        int index = _sharedComments.FindIndex(c => c.Id == commentId);
        if (index < 0)
        {
            return;
        }

        _sharedComments.RemoveAt(index);
        if (_selectedSharedCommentId == commentId)
        {
            _selectedSharedCommentId = _sharedComments.Count > 0
                ? _sharedComments[Math.Clamp(index, 0, _sharedComments.Count - 1)].Id
                : null;
        }

        UpdateSharedCommentsUi();
        SaveSession();
    }

    private void InitializeClipTimelineUi()
    {
        _clipTimelineViewport = new Panel
        {
            Dock = DockStyle.None,
            BackColor = Color.FromArgb(34, 34, 34),
            AutoScroll = false,
            Margin = new Padding(0)
        };

        _clipTimelineCanvas = new Panel
        {
            Location = new DrawingPoint(0, 0),
            Size = new DrawingSize(20000, ClipTimelineCanvasHeight - 2),
            BackColor = Color.FromArgb(34, 34, 34)
        };
        EnableDoubleBuffer(_clipTimelineCanvas);
        EnableDoubleBuffer(_clipTimelineViewport);
        _clipTimelineCanvas.Paint += (_, e) => DrawClipTimeline(e.Graphics);
        _clipTimelineCanvas.MouseDown += ClipTimelineCanvas_MouseDown;
        _clipTimelineCanvas.MouseMove += ClipTimelineCanvas_MouseMove;
        _clipTimelineCanvas.MouseUp += (_, _) =>
        {
            if (_draggingTimelineTrack is not null || _isDraggingPlayhead)
            {
                RenderTrackForGlobalFrame(_leftTrack, masterTimeline.Value);
                RenderTrackForGlobalFrame(_rightTrack, masterTimeline.Value);
                UpdatePlaybackStatus();
                if (_draggingTimelineTrack is not null)
                {
                    SaveSession();
                }
            }
            ClearSnapPreview();
            ClearTrackDragSnap();
            _draggingTimelineTrack = null;
            _isDraggingPlayhead = false;
            _clipTimelineCanvas.Cursor = Cursors.Default;
            _clipTimelineCanvas?.Invalidate();
        };
        _clipTimelineViewport.Controls.Add(_clipTimelineCanvas);
        _clipTimelineViewport.MouseWheel += ClipTimelineCanvas_MouseWheel;
        _clipTimelineViewport.Resize += (_, _) =>
        {
            EnsureClipTimelineCanvasWidth();
            _clipTimelineCanvas?.Invalidate();
        };

        if (_sharedCommentMarkerPanel is not null)
        {
            _sharedCommentMarkerPanel.Visible = false;
        }
        masterTimeline.Visible = false;
        speedLabel.Visible = false;

        ConfigureTransportLayout();
        bottomTransportPanel.Resize += (_, _) => LayoutBottomTransportUi();
    }

    private void ConfigureTransportLayout()
    {
        if (_clipTimelineViewport is null)
        {
            return;
        }

        _transportHostPanel ??= new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _transportLeftPanel ??= new Panel
        {
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _transportRightPanel ??= new Panel
        {
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        bottomTransportPanel.Controls.Clear();
        bottomTransportPanel.Controls.Add(_transportHostPanel);
        _transportHostPanel.Controls.Clear();
        _transportHostPanel.Controls.Add(_transportLeftPanel);
        _transportHostPanel.Controls.Add(_transportRightPanel);

        _transportLeftPanel.Controls.Clear();
        _transportRightPanel.Controls.Clear();

        playPauseButton.Parent = _transportLeftPanel;
        speedComboBox.Parent = _transportLeftPanel;
        _clipTimelineViewport.Parent = _transportRightPanel;
        playbackStatusLabel.Parent = _transportRightPanel;

        playPauseButton.Dock = DockStyle.None;
        speedComboBox.Dock = DockStyle.None;
        speedComboBox.Visible = true;
        speedComboBox.MinimumSize = DrawingSize.Empty;
        speedComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        speedComboBox.IntegralHeight = false;

        _clipTimelineViewport.Dock = DockStyle.None;
        playbackStatusLabel.Dock = DockStyle.None;
        playbackStatusLabel.ForeColor = Color.FromArgb(168, 168, 168);
        playbackStatusLabel.Padding = new Padding(8, 0, 0, 0);

        transportLayout.Visible = false;
        LayoutBottomTransportUi();
    }

    private void LayoutBottomTransportUi()
    {
        if (_transportHostPanel is null || _transportLeftPanel is null || _transportRightPanel is null || _clipTimelineViewport is null)
        {
            return;
        }

        const int gap = 8;
        const int leftWidth = 132;
        const int playHeight = 30;
        const int playToSpeedGap = 4;
        const int speedHeight = 26;
        const int timelineHeight = 60;
        const int statusHeight = 22;

        int hostWidth = _transportHostPanel.ClientSize.Width;
        int leftHeight = playHeight + playToSpeedGap + speedHeight;
        int rightX = leftWidth + gap;
        int rightWidth = Math.Max(0, hostWidth - rightX);

        _transportLeftPanel.Bounds = new Rectangle(0, 0, leftWidth, leftHeight);
        _transportRightPanel.Bounds = new Rectangle(rightX, 0, rightWidth, timelineHeight + statusHeight);

        playPauseButton.Bounds = new Rectangle(0, 0, leftWidth, playHeight);
        playPauseButton.FlatStyle = FlatStyle.Standard;
        playPauseButton.UseVisualStyleBackColor = true;
        playPauseButton.Text = _isPlaying ? PauseIcon : PlayIcon;
        speedComboBox.Bounds = new Rectangle(0, playHeight + playToSpeedGap, leftWidth, speedHeight);
        speedComboBox.FlatStyle = FlatStyle.Standard;

        _clipTimelineViewport.Bounds = new Rectangle(0, 0, rightWidth, timelineHeight);
        playbackStatusLabel.Bounds = new Rectangle(0, timelineHeight, rightWidth, statusHeight);
    }

    private void LoadVideoIntoTrack(VideoTrack track, string filePath)
    {
        AppLog.Write($"Loading {track.Name}: {filePath}");
        StopPlayback();
        track.Load(filePath, _libVlc);
        UpdateWindowSourceTimerState();
        AppLog.Write($"Loaded {track.Name}: fps={track.Fps:0.###}, frames={track.FrameCount}, size={track.FrameSize.Width}x{track.FrameSize.Height}");
        RenderTrack(track, track.CurrentFrameIndex, updateMasterTimeline: false);
        track.MarkerPanel.Invalidate();
        SetActiveTrack(track);
        UpdateVideoFit();
        UpdateMasterTimelineBounds();
        EnsureClipTimelineCanvasWidth();
        _clipTimelineCanvas?.Invalidate();
        UpdatePlaybackStatus();
        UpdateWindowToContent();
        SaveSession();
    }

    private static void HandleVideoDragEnter(DragEventArgs e)
    {
        if (TryGetDroppedVideoPath(e.Data) is not null)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void HandleVideoDragDrop(VideoTrack track, DragEventArgs e)
    {
        try
        {
            string? videoPath = TryGetDroppedVideoPath(e.Data);
            if (videoPath is null)
            {
                return;
            }

            LoadVideoIntoTrack(track, videoPath);
        }
        catch (Exception ex)
        {
            AppLog.WriteError($"Could not load dropped video into {track.Name}.", ex);
            string details = ex.InnerException is null ? ex.Message : $"{ex.Message}\n\n{ex.InnerException.Message}";
            MessageBox.Show(this, details, "Could not load dropped video", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void useWindowSourceButton_Click(object sender, EventArgs e)
    {
        if (GetTrackToRestore() is not null)
        {
            RestoreTemporaryWindowSource();
            return;
        }

        if (_activeTrack is null)
        {
            return;
        }

        using var picker = new WindowPickerForm();
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedWindow is null)
        {
            return;
        }

        StopPlayback();
        _activeTrack.SetTemporaryWindowSource(picker.SelectedWindow.Value.Handle, picker.SelectedWindow.Value.Title);
        if (!TryRenderTemporaryWindowFrame(_activeTrack))
        {
            MessageBox.Show(this, "Could not capture that window. Make sure it is visible and not minimized.", "Capture Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _activeTrack.ClearTemporaryWindowSource();
            RenderTrack(_activeTrack, _activeTrack.CurrentFrameIndex, updateMasterTimeline: false);
        }

        UpdateWindowSourceTimerState();
        UpdateMasterTimelineBounds();
        UpdateTrackInfo(_activeTrack);
        UpdatePlaybackStatus();
    }

    private void restoreVideoSourceButton_Click(object sender, EventArgs e)
    {
        RestoreTemporaryWindowSource();
    }

    private void WindowSourceTimer_Tick(object? sender, EventArgs e)
    {
        RenderTemporaryWindowTrack(_leftTrack);
        RenderTemporaryWindowTrack(_rightTrack);
    }

    private void RenderTemporaryWindowTrack(VideoTrack track)
    {
        if (!track.IsTemporaryWindowSource)
        {
            return;
        }

        if (!TryRenderTemporaryWindowFrame(track))
        {
            AppLog.WriteError($"Failed to refresh temporary window source for {track.Name}.");
            return;
        }

        UpdateTrackInfo(track);
        UpdateAlignmentPreview();
    }

    private bool TryRenderTemporaryWindowFrame(VideoTrack track)
    {
        if (!track.IsTemporaryWindowSource || track.TemporaryWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!TryCaptureWindowBitmap(track.TemporaryWindowHandle, out Bitmap? bitmap))
        {
            return false;
        }
        if (bitmap is null)
        {
            return false;
        }

        using (bitmap)
        {
            track.UpdateTemporaryFrameSize(bitmap.Size);
            Bitmap nextBitmap = (Bitmap)bitmap.Clone();
            Bitmap? previousBitmap = track.PictureBox.Image as Bitmap;
            track.PictureBox.Image = nextBitmap;
            previousBitmap?.Dispose();
        }

        track.ShowStillFrame();
        ApplyScale(track);
        return true;
    }

    private void UpdateWindowSourceTimerState()
    {
        bool hasTemporarySource = _leftTrack.IsTemporaryWindowSource || _rightTrack.IsTemporaryWindowSource;
        if (hasTemporarySource && !_windowSourceTimer.Enabled)
        {
            _windowSourceTimer.Start();
        }
        else if (!hasTemporarySource && _windowSourceTimer.Enabled)
        {
            _windowSourceTimer.Stop();
        }
        useWindowSourceButton.Text = hasTemporarySource ? "Back To Video" : "Use App Window";
        useWindowSourceButton.Enabled = true;
        UpdateAlignmentModeAvailability();
        LayoutTopBarControls();
        UpdateEmptyHintState(_leftTrack);
        UpdateEmptyHintState(_rightTrack);
    }

    private void UpdateAlignmentModeAvailability()
    {
        bool hasTemporarySource = _leftTrack.IsTemporaryWindowSource || _rightTrack.IsTemporaryWindowSource;
        if (!hasTemporarySource && alignmentModeCheckBox.Checked)
        {
            alignmentModeCheckBox.Checked = false;
        }

        alignmentModeCheckBox.Visible = hasTemporarySource;
        alignmentModeCheckBox.Enabled = hasTemporarySource;
    }

    private static string? TryGetDroppedVideoPath(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return null;
        }

        return paths.FirstOrDefault(path =>
            File.Exists(path) &&
            SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    private void SetActiveTrack(VideoTrack track)
    {
        _activeTrack = track;
        SelectTrackPanel(_leftTrack, track == _leftTrack);
        SelectTrackPanel(_rightTrack, track == _rightTrack);
        UpdateWindowSourceTimerState();
        UpdateEmptyHintState(_leftTrack);
        UpdateEmptyHintState(_rightTrack);
        track.MarkerPanel.Invalidate();
        (_activeTrack == _leftTrack ? _rightTrack : _leftTrack).MarkerPanel.Invalidate();
        _leftTrack.ImagePanel.Invalidate();
        _rightTrack.ImagePanel.Invalidate();
        UpdatePlaybackStatus();
    }

    private static void UpdateEmptyHintState(VideoTrack track)
    {
        bool showHint = !track.IsLoaded && !track.IsTemporaryWindowSource;
        if (showHint)
        {
            if (track.PictureBox.Image is Bitmap previousBitmap)
            {
                track.PictureBox.Image = null;
                previousBitmap.Dispose();
            }

            track.PlaybackView.Visible = false;
            track.PictureBox.Visible = false;
            track.ImagePanel.AutoScrollMinSize = DrawingSize.Empty;
            track.ImagePanel.AutoScrollPosition = new DrawingPoint(0, 0);
            track.ImagePanel.Invalidate();
            return;
        }

        if (!track.IsPlaybackVisible)
        {
            track.PictureBox.Visible = true;
        }
    }

    private VideoTrack? GetTrackToRestore()
    {
        return _activeTrack?.IsTemporaryWindowSource == true
            ? _activeTrack
            : _leftTrack.IsTemporaryWindowSource ? _leftTrack
            : _rightTrack.IsTemporaryWindowSource ? _rightTrack
            : null;
    }

    private void RestoreTemporaryWindowSource()
    {
        VideoTrack? trackToRestore = GetTrackToRestore();
        if (trackToRestore is null)
        {
            return;
        }

        StopPlayback();
        trackToRestore.ClearTemporaryWindowSource();
        RenderTrack(trackToRestore, trackToRestore.CurrentFrameIndex, updateMasterTimeline: false);
        SetActiveTrack(trackToRestore);
        UpdateWindowSourceTimerState();
        UpdateMasterTimelineBounds();
        UpdateTrackInfo(trackToRestore);
        UpdatePlaybackStatus();
    }

    private void alignmentModeCheckBox_CheckedChanged(object sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
        }

        UpdateAlignmentPreview();
    }

    private static void SelectTrackPanel(VideoTrack track, bool isActive)
    {
        track.HostPanel.BackColor = Color.FromArgb(45, 45, 45);
        track.TitleLabel.BackColor = isActive ? Color.FromArgb(50, 110, 170) : Color.FromArgb(45, 45, 45);
    }

    private void InitializeLayoutQuickButtons()
    {
        layoutLabel.Visible = false;
        layoutComboBox.Visible = false;

        _layoutStackedButton = CreateLayoutButton(
            location: new DrawingPoint(24, 12),
            onClick: () => layoutComboBox.SelectedIndex = 1);
        _layoutSideBySideButton = CreateLayoutButton(
            location: new DrawingPoint(66, 12),
            onClick: () => layoutComboBox.SelectedIndex = 0);

        _layoutStackedActiveIcon = CreateLayoutIcon(stacked: true, Color.FromArgb(238, 238, 238));
        _layoutStackedInactiveIcon = CreateLayoutIcon(stacked: true, Color.FromArgb(140, 140, 140));
        _layoutSideBySideActiveIcon = CreateLayoutIcon(stacked: false, Color.FromArgb(238, 238, 238));
        _layoutSideBySideInactiveIcon = CreateLayoutIcon(stacked: false, Color.FromArgb(140, 140, 140));

        topBarPanel.Controls.Add(_layoutStackedButton);
        topBarPanel.Controls.Add(_layoutSideBySideButton);
        _layoutStackedButton.BringToFront();
        _layoutSideBySideButton.BringToFront();

        UpdateLayoutButtonsState();
    }

    private void InitializeHelpUi()
    {
        _helpTopBarButton = CreateLayoutButton(
            location: new DrawingPoint(0, 12),
            onClick: ShowHelpOverlay);
        _helpIcon = CreateHelpIcon(Color.FromArgb(238, 238, 238));
        _helpTopBarButton.Image = _helpIcon;
        _helpTopBarButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topBarPanel.Controls.Add(_helpTopBarButton);
        _helpTopBarButton.BringToFront();
        LayoutTopBarButtons();
    }

    private static Bitmap CreateHelpIcon(Color strokeColor)
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(strokeColor, 1.5f);
        using var brush = new SolidBrush(strokeColor);
        g.DrawEllipse(pen, 2, 2, 14, 14);
        using var font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("?", font, brush, new RectangleF(4, 2, 10, 13), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        return bmp;
    }

    private void ConfigureTooltips()
    {
        _uiToolTip.ShowAlways = true;
        _uiToolTip.InitialDelay = 250;
        _uiToolTip.ReshowDelay = 100;
        _uiToolTip.AutoPopDelay = 7000;

        if (_layoutStackedButton is not null)
        {
            _uiToolTip.SetToolTip(_layoutStackedButton, "Stacked layout");
        }
        if (_layoutSideBySideButton is not null)
        {
            _uiToolTip.SetToolTip(_layoutSideBySideButton, "Side-by-side layout");
        }
        if (_toggleCommentsTopBarButton is not null)
        {
            _uiToolTip.SetToolTip(_toggleCommentsTopBarButton, "Show or hide comments");
        }
        if (_helpTopBarButton is not null)
        {
            _uiToolTip.SetToolTip(_helpTopBarButton, "Help (F1)");
        }
        if (_diagnosticsTopBarButton is not null)
        {
            _uiToolTip.SetToolTip(_diagnosticsTopBarButton, "Toggle diagnostics panel");
        }

        _uiToolTip.SetToolTip(useWindowSourceButton, "Capture a live app window for the active side");
        _uiToolTip.SetToolTip(alignmentModeCheckBox, "Overlay both sides to check visual alignment");
        _uiToolTip.SetToolTip(playPauseButton, "Play or pause (Space)");
        _uiToolTip.SetToolTip(speedComboBox, "Playback speed");
        if (_saveTrimmedVideosTopBarButton is not null)
        {
            _uiToolTip.SetToolTip(_saveTrimmedVideosTopBarButton, "Save trimmed videos using shared In/Out range (I/O, clear with X)");
        }
        if (_saveCombinedVideoTopBarButton is not null)
        {
            _uiToolTip.SetToolTip(_saveCombinedVideoTopBarButton, "Save one combined video in current layout (side-by-side or stacked)");
        }
        if (_clipTimelineCanvas is not null)
        {
            _uiToolTip.SetToolTip(_clipTimelineCanvas, "Drag blue marker to scrub. Drag bars to shift clips. Snap near yellow markers.");
        }
    }

    private void ShowHelpOverlay()
    {
        using var helpForm = new Form
        {
            Text = "FrameComp Help",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Width = 560,
            Height = 520,
            BackColor = Color.FromArgb(24, 24, 24)
        };

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(24, 24, 24),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 12, 14, 8)
        };
        content.Controls.Add(CreateHelpSection(
            "Playback",
            "Space: Play/Pause\r\nSpeed dropdown: playback speed",
            CreateHelpPlaybackIcon()));

        content.Controls.Add(CreateHelpSection(
            "Timeline",
            "Drag blue marker: scrub timeline\r\nClick timeline: jump and keep dragging\r\nDrag clip bars: move clip timing\r\nYellow markers snap when close",
            CreateHelpTimelineIcon()));

        content.Controls.Add(CreateHelpSection(
            "Markers And Comments",
            "M: add marker on active video\r\nDelete: remove selected marker\r\nI / O: set trim in/out\r\nX: clear active trim\r\nC: add timeline comment\r\nClick comment to jump there",
            CreateHelpMarkersIcon()));

        content.Controls.Add(CreateHelpSection(
            "Keyboard",
            "Arrow keys: frame-step active video\r\nShift + Arrows: larger step\r\nCtrl + Arrows: move shared timeline\r\nF1: open this help",
            CreateHelpKeyboardIcon()));

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(145, 145, 145),
            Text = "Press Esc to close",
            Padding = new Padding(0, 0, 8, 0)
        };
        helpForm.Controls.Add(content);
        helpForm.Controls.Add(footer);
        helpForm.KeyPreview = true;
        helpForm.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                helpForm.Close();
            }
        };
        helpForm.ShowDialog(this);
    }

    private static Panel CreateHelpSection(string title, string body, Bitmap icon)
    {
        var section = new Panel
        {
            Width = 500,
            Height = 108,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        var iconBox = new PictureBox
        {
            Location = new DrawingPoint(0, 4),
            Size = new DrawingSize(24, 24),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = icon
        };
        var heading = new Label
        {
            AutoSize = false,
            Location = new DrawingPoint(30, 0),
            Width = 460,
            Height = 24,
            ForeColor = Color.FromArgb(225, 225, 225),
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Text = title
        };
        var text = new Label
        {
            AutoSize = false,
            Location = new DrawingPoint(30, 24),
            Width = 460,
            Height = 80,
            ForeColor = Color.FromArgb(170, 170, 170),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            Text = body
        };
        section.Controls.Add(iconBox);
        section.Controls.Add(heading);
        section.Controls.Add(text);
        return section;
    }

    private static Bitmap CreateHelpPlaybackIcon()
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(230, 230, 230));
        DrawingPoint[] triangle =
        [
            new DrawingPoint(5, 3),
            new DrawingPoint(14, 9),
            new DrawingPoint(5, 15)
        ];
        g.FillPolygon(fill, triangle);
        return bmp;
    }

    private static Bitmap CreateHelpTimelineIcon()
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var linePen = new Pen(Color.FromArgb(175, 175, 175), 1.5f);
        using var playheadPen = new Pen(Color.FromArgb(80, 160, 255), 1.8f);
        using var playheadBrush = new SolidBrush(Color.FromArgb(80, 160, 255));
        g.DrawLine(linePen, 2, 12, 16, 12);
        g.DrawLine(playheadPen, 9, 2, 9, 15);
        DrawingPoint[] handle =
        [
            new DrawingPoint(6, 2),
            new DrawingPoint(12, 2),
            new DrawingPoint(9, 6)
        ];
        g.FillPolygon(playheadBrush, handle);
        return bmp;
    }

    private static Bitmap CreateHelpMarkersIcon()
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var lanePen = new Pen(Color.FromArgb(130, 130, 130), 1.2f);
        using var markerBrush = new SolidBrush(Color.FromArgb(255, 208, 64));
        g.DrawLine(lanePen, 2, 13, 16, 13);
        DrawingPoint[] leftMarker =
        [
            new DrawingPoint(5, 13),
            new DrawingPoint(2, 8),
            new DrawingPoint(8, 8)
        ];
        DrawingPoint[] rightMarker =
        [
            new DrawingPoint(13, 13),
            new DrawingPoint(10, 8),
            new DrawingPoint(16, 8)
        ];
        g.FillPolygon(markerBrush, leftMarker);
        g.FillPolygon(markerBrush, rightMarker);
        return bmp;
    }

    private static Bitmap CreateHelpKeyboardIcon()
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var keyPen = new Pen(Color.FromArgb(220, 220, 220), 1.2f);
        using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        using (GraphicsPath key = CreateRoundedRectPath(new Rectangle(2, 3, 14, 12), 2))
        {
            g.DrawPath(keyPen, key);
        }
        using var font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("F1", font, textBrush, new RectangleF(3, 5, 12, 8), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        return bmp;
    }

    private Button CreateLayoutButton(DrawingPoint location, Action onClick)
    {
        var button = new Button
        {
            Location = location,
            Size = new DrawingSize(38, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            ImageAlign = ContentAlignment.MiddleCenter,
            Text = string.Empty,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.Transparent;
        button.FlatAppearance.MouseOverBackColor = Color.Transparent;
        button.UseVisualStyleBackColor = false;
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Bitmap CreateLayoutIcon(bool stacked, Color strokeColor)
    {
        var bmp = new Bitmap(18, 18);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var borderPen = new Pen(strokeColor, 1.6f);
        if (stacked)
        {
            g.DrawRectangle(borderPen, 2, 2, 14, 6);
            g.DrawRectangle(borderPen, 2, 10, 14, 6);
        }
        else
        {
            g.DrawRectangle(borderPen, 2, 2, 6, 14);
            g.DrawRectangle(borderPen, 10, 2, 6, 14);
        }
        return bmp;
    }

    private void UpdateLayoutButtonsState()
    {
        if (_layoutSideBySideButton is null || _layoutStackedButton is null)
        {
            return;
        }

        bool isSideBySide = layoutComboBox.SelectedIndex == 0;
        _layoutSideBySideButton.Image = isSideBySide ? _layoutSideBySideActiveIcon : _layoutSideBySideInactiveIcon;
        _layoutStackedButton.Image = isSideBySide ? _layoutStackedInactiveIcon : _layoutStackedActiveIcon;
    }

    private void SeekToFrame(VideoTrack track, int frameIndex)
    {
        if (!track.IsLoaded)
        {
            return;
        }

        StopPlayback();
        RenderTrack(track, GetSnappedFrame(track, frameIndex), updateMasterTimeline: false);
    }

    private void RenderTrack(VideoTrack track, int requestedFrameIndex, bool updateMasterTimeline = false, bool lightweight = false)
    {
        if (!track.IsLoaded)
        {
            return;
        }

        int safeFrameIndex = Math.Clamp(requestedFrameIndex, 0, track.LastFrameIndex);
        Bitmap nextBitmap;
        if (!track.TryGetCachedFrameBitmap(safeFrameIndex, out Bitmap? cachedBitmap))
        {
            using Mat? frame = track.ReadFrame(safeFrameIndex);
            if (frame is null || frame.Empty())
            {
                AppLog.WriteError($"RenderTrack failed for {track.Name} at frame {safeFrameIndex}.");
                ClearTrackPreview(track, lightweight);
                return;
            }

            nextBitmap = BitmapConverter.ToBitmap(frame);
            track.CacheFrameBitmap(safeFrameIndex, nextBitmap);
        }
        else
        {
            nextBitmap = cachedBitmap!;
        }
        Bitmap? previousBitmap = track.PictureBox.Image as Bitmap;
        track.PictureBox.Image = nextBitmap;
        previousBitmap?.Dispose();
        if (!_isPlaying)
        {
            track.ShowStillFrame();
        }

        track.CurrentFrameIndex = safeFrameIndex;
        if (track.Timeline.Value != safeFrameIndex)
        {
            _isUpdatingTrackTimelines = true;
            try
            {
                track.Timeline.Value = safeFrameIndex;
            }
            finally
            {
                _isUpdatingTrackTimelines = false;
            }
        }

        track.ShowStillFrame();
        if (!lightweight)
        {
            ApplyScale(track);
            UpdateTrackInfo(track);
            if (updateMasterTimeline)
            {
                UpdateMasterTimelineAfterTrackRender(track);
            }

            track.MarkerPanel.Invalidate();
            UpdatePlaybackStatus();
            UpdateAlignmentPreview();
        }
    }

    private void layoutComboBox_SelectedIndexChanged(object sender, EventArgs e) => UpdateLayoutMode();

    private void speedComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        int speedIndex = Math.Clamp(speedComboBox.SelectedIndex, 0, PlaybackSpeedOptions.Length - 1);
        _playbackSpeedMultiplier = PlaybackSpeedOptions[speedIndex];

        if (_isPlaying)
        {
            ApplyPlaybackRate(_leftTrack);
            ApplyPlaybackRate(_rightTrack);
        }

        EnsureAllPreviewScrollStates();
        UpdatePlaybackStatus();
    }

    private void UpdateLayoutMode()
    {
        UpdateLayoutButtonsState();
        bool horizontal = layoutComboBox.SelectedIndex == 0;
        videosTableLayout.SuspendLayout();
        if (_sharedCommentsPanel is not null && !videosTableLayout.Controls.Contains(_sharedCommentsPanel))
        {
            videosTableLayout.Controls.Add(_sharedCommentsPanel);
        }

        if (horizontal)
        {
            videosTableLayout.ColumnCount = 3;
            videosTableLayout.RowCount = 1;
            videosTableLayout.ColumnStyles.Clear();
            videosTableLayout.RowStyles.Clear();
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _isCommentsSidebarCollapsed ? 0f : 320f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            videosTableLayout.SetCellPosition(leftHostPanel, new TableLayoutPanelCellPosition(0, 0));
            videosTableLayout.SetCellPosition(rightHostPanel, new TableLayoutPanelCellPosition(1, 0));
            if (_sharedCommentsPanel is not null)
            {
                videosTableLayout.SetCellPosition(_sharedCommentsPanel, new TableLayoutPanelCellPosition(2, 0));
                videosTableLayout.SetRowSpan(_sharedCommentsPanel, 1);
                _sharedCommentsPanel.Visible = !_isCommentsSidebarCollapsed;
            }
        }
        else
        {
            videosTableLayout.ColumnCount = 2;
            videosTableLayout.RowCount = 2;
            videosTableLayout.ColumnStyles.Clear();
            videosTableLayout.RowStyles.Clear();
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _isCommentsSidebarCollapsed ? 0f : 320f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            videosTableLayout.SetCellPosition(leftHostPanel, new TableLayoutPanelCellPosition(0, 0));
            videosTableLayout.SetCellPosition(rightHostPanel, new TableLayoutPanelCellPosition(0, 1));
            if (_sharedCommentsPanel is not null)
            {
                videosTableLayout.SetCellPosition(_sharedCommentsPanel, new TableLayoutPanelCellPosition(1, 0));
                videosTableLayout.SetRowSpan(_sharedCommentsPanel, 2);
                _sharedCommentsPanel.Visible = !_isCommentsSidebarCollapsed;
            }
        }

        videosTableLayout.ResumeLayout();
        UpdateCommentsSidebarButtonText();
        UpdateVideoFit();
        UpdateWindowToContent();
    }

    private void UpdateCommentsSidebarButtonText()
    {
        if (_toggleCommentsTopBarButton is null)
        {
            return;
        }

        _toggleCommentsTopBarButton.AccessibleName = _isCommentsSidebarCollapsed ? "Show Comments" : "Hide Comments";
        _toggleCommentsTopBarButton.Image = _isCommentsSidebarCollapsed ? _commentsInactiveIcon : _commentsActiveIcon;
        _toggleCommentsTopBarButton.BringToFront();
    }

    private void UpdateVideoFit()
    {
        ApplyScale(_leftTrack);
        ApplyScale(_rightTrack);
        UpdateWindowToContent();
    }

    private static void ApplyScale(VideoTrack track)
    {
        if (track.IsPlaybackVisible)
        {
            track.PlaybackView.Bounds = track.ImagePanel.DisplayRectangle;
            return;
        }

        DrawingSize sourceFrameSize = track.DisplayFrameSize;
        if (sourceFrameSize.Width <= 0 || sourceFrameSize.Height <= 0)
        {
            track.ImagePanel.AutoScrollMinSize = DrawingSize.Empty;
            track.ImagePanel.AutoScrollPosition = new DrawingPoint(0, 0);
            track.PictureBox.Size = new DrawingSize(320, 180);
            track.PictureBox.Location = new DrawingPoint(12, 12);
            return;
        }

        int availableWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int availableHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        float fitScale = Math.Min(
            availableWidth / (float)sourceFrameSize.Width,
            availableHeight / (float)sourceFrameSize.Height);
        float appliedScale = fitScale * track.ZoomMultiplier;

        track.PictureBox.Size = new DrawingSize(
            Math.Max(1, (int)Math.Round(sourceFrameSize.Width * appliedScale)),
            Math.Max(1, (int)Math.Round(sourceFrameSize.Height * appliedScale)));
        UpdateTrackScrollState(track, availableWidth, availableHeight);
        UpdateTrackPreviewLayout(track);
    }

    private static void UpdateTrackScrollState(VideoTrack track, int viewportWidth, int viewportHeight)
    {
        const int ScrollTolerancePixels = 2;
        bool allowScrollFromZoom = track.ZoomMultiplier > (MinZoomMultiplier + 0.001f);
        int picW = track.PictureBox.Width;
        int picH = track.PictureBox.Height;
        bool needsH = allowScrollFromZoom && (picW > (viewportWidth + ScrollTolerancePixels));
        bool needsV = allowScrollFromZoom && (picH > (viewportHeight + ScrollTolerancePixels));

        // Account for scrollbar cross-impact so we don't oscillate into phantom bars.
        if (needsV && !needsH)
        {
            int reducedWidth = Math.Max(1, viewportWidth - SystemInformation.VerticalScrollBarWidth);
            needsH = picW > (reducedWidth + ScrollTolerancePixels);
        }
        if (needsH && !needsV)
        {
            int reducedHeight = Math.Max(1, viewportHeight - SystemInformation.HorizontalScrollBarHeight);
            needsV = picH > (reducedHeight + ScrollTolerancePixels);
        }

        track.ImagePanel.AutoScrollMinSize = new DrawingSize(needsH ? picW : 0, needsV ? picH : 0);
        if (!needsH && !needsV)
        {
            track.ImagePanel.AutoScrollPosition = new DrawingPoint(0, 0);
        }
    }

    private static void UpdateTrackPreviewLayout(VideoTrack track)
    {
        if (track.PictureBox.Width <= 0 || track.PictureBox.Height <= 0)
        {
            return;
        }

        int viewportWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int viewportHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        bool canScrollX = track.ImagePanel.AutoScrollMinSize.Width > 0;
        bool canScrollY = track.ImagePanel.AutoScrollMinSize.Height > 0;
        DrawingPoint scrollOffset = track.ImagePanel.AutoScrollPosition;
        int x = !canScrollX
            ? track.ImagePanel.Padding.Left + ((viewportWidth - track.PictureBox.Width) / 2)
            : track.ImagePanel.Padding.Left + scrollOffset.X;
        int y = !canScrollY
            ? track.ImagePanel.Padding.Top + ((viewportHeight - track.PictureBox.Height) / 2)
            : track.ImagePanel.Padding.Top + scrollOffset.Y;
        track.PictureBox.Location = new DrawingPoint(x, y);
    }

    private void UpdateMasterTimelineBounds()
    {
        EnsureClipTimelineCanvasWidth();
        _isUpdatingMasterTimeline = true;
        masterTimeline.Minimum = 0;
        masterTimeline.Enabled = (_leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource) ||
            (_rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource);
        _isUpdatingMasterTimeline = false;
        _clipTimelineCanvas?.Invalidate();
    }

    private void HandleTrackMouseWheel(VideoTrack track, MouseEventArgs e)
    {
        if (!track.IsLoaded)
        {
            return;
        }

        SetActiveTrack(track);
        StopPlayback();

        DrawingPoint mousePoint = track.ImagePanel.PointToClient(Cursor.Position);
        Rectangle previousBounds = track.PictureBox.Bounds;
        float anchorX = previousBounds.Width <= 0
            ? 0.5f
            : Math.Clamp((mousePoint.X - previousBounds.Left) / (float)previousBounds.Width, 0f, 1f);
        float anchorY = previousBounds.Height <= 0
            ? 0.5f
            : Math.Clamp((mousePoint.Y - previousBounds.Top) / (float)previousBounds.Height, 0f, 1f);

        float nextZoom = e.Delta > 0
            ? track.ZoomMultiplier * WheelZoomStep
            : track.ZoomMultiplier / WheelZoomStep;
        nextZoom = Math.Clamp(nextZoom, MinZoomMultiplier, MaxZoomMultiplier);

        if (Math.Abs(nextZoom - track.ZoomMultiplier) < 0.0001f)
        {
            return;
        }

        // Keep both previews on the same zoom multiplier and center both views
        // on the same normalized anchor derived from the active mouse position.
        foreach (VideoTrack candidate in new[] { _leftTrack, _rightTrack })
        {
            if (!candidate.IsLoaded || candidate.IsTemporaryWindowSource)
            {
                continue;
            }

            candidate.ZoomMultiplier = nextZoom;
            ApplyScale(candidate);
            RestoreZoomCenter(candidate, anchorX, anchorY);
            UpdateTrackInfo(candidate);
        }

        AppLog.Write($"Zoom synced: {nextZoom:0.##}x");
    }

    private static void RestoreZoomCenter(VideoTrack track, float centerX, float centerY)
    {
        int viewportWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int viewportHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        int scrollX = 0;
        int scrollY = 0;

        if (track.PictureBox.Width > viewportWidth)
        {
            double centerPixelX = centerX * track.PictureBox.Width;
            scrollX = (int)Math.Round(centerPixelX - (viewportWidth / 2d));
            scrollX = Math.Clamp(scrollX, 0, Math.Max(0, track.PictureBox.Width - viewportWidth));
        }
        if (track.PictureBox.Height > viewportHeight)
        {
            double centerPixelY = centerY * track.PictureBox.Height;
            scrollY = (int)Math.Round(centerPixelY - (viewportHeight / 2d));
            scrollY = Math.Clamp(scrollY, 0, Math.Max(0, track.PictureBox.Height - viewportHeight));
        }

        track.ImagePanel.AutoScrollPosition = new DrawingPoint(scrollX, scrollY);
        UpdateTrackPreviewLayout(track);
    }

    private void UpdateMasterTimelineAfterTrackRender(VideoTrack renderedTrack)
    {
        if (_activeTrack == renderedTrack)
        {
            UpdateMasterTimelineFromFrame(renderedTrack.CurrentFrameIndex + renderedTrack.TimelineStartFrame);
        }
    }

    private void UpdateMasterTimelineFromTrack(VideoTrack? track)
    {
        if (track is null || !track.IsLoaded)
        {
            UpdateMasterTimelineFromFrame(0);
            return;
        }

        UpdateMasterTimelineFromFrame(track.CurrentFrameIndex + track.TimelineStartFrame);
    }

    private void UpdateMasterTimelineFromFrame(int frameIndex)
    {
        if (masterTimeline.Maximum < 0)
        {
            return;
        }

        _isUpdatingMasterTimeline = true;
        masterTimeline.Value = Math.Clamp(frameIndex, masterTimeline.Minimum, masterTimeline.Maximum);
        _isUpdatingMasterTimeline = false;
        _sharedCommentMarkerPanel?.Invalidate();
        _clipTimelineCanvas?.Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F1)
        {
            ShowHelpOverlay();
            return true;
        }

        if (keyData == Keys.F3)
        {
            _showTimelineDebug = !_showTimelineDebug;
            UpdatePlaybackStatus();
            return true;
        }

        if (keyData == Keys.Space)
        {
            if (_isPlaying)
            {
                StopPlayback();
            }
            else
            {
                StartPlayback();
            }

            return true;
        }

        if (keyData == Keys.M)
        {
            return AddMarkerToActiveTrack();
        }

        if (keyData == Keys.Delete)
        {
            return DeleteSelectedMarkerOnActiveTrack();
        }

        if (keyData == Keys.C)
        {
            return AddSharedCommentAtCurrentTimeline();
        }

        if (keyData == Keys.I)
        {
            return SetGlobalTrimIn();
        }

        if (keyData == Keys.O)
        {
            return SetGlobalTrimOut();
        }

        if (keyData == Keys.X)
        {
            return ClearGlobalTrim();
        }

        if (_activeTrack is null || !_activeTrack.IsLoaded)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        return keyData switch
        {
            Keys.Left => StepActiveTrack(-1),
            Keys.Shift | Keys.Left => StepActiveTrack(-LargeStep),
            Keys.Control | Keys.Left => StepBothTracks(-1),
            Keys.Control | Keys.Shift | Keys.Left => StepBothTracks(-LargeStep),
            Keys.Up => StepActiveTrack(-1),
            Keys.Shift | Keys.Up => StepActiveTrack(-LargeStep),
            Keys.Control | Keys.Up => StepBothTracks(-1),
            Keys.Control | Keys.Shift | Keys.Up => StepBothTracks(-LargeStep),
            Keys.Right => StepActiveTrack(1),
            Keys.Shift | Keys.Right => StepActiveTrack(LargeStep),
            Keys.Control | Keys.Right => StepBothTracks(1),
            Keys.Control | Keys.Shift | Keys.Right => StepBothTracks(LargeStep),
            Keys.Down => StepActiveTrack(1),
            Keys.Shift | Keys.Down => StepActiveTrack(LargeStep),
            Keys.Control | Keys.Down => StepBothTracks(1),
            Keys.Control | Keys.Shift | Keys.Down => StepBothTracks(LargeStep),
            _ => base.ProcessCmdKey(ref msg, keyData)
        };
    }

    private bool StepActiveTrack(int delta)
    {
        if (_activeTrack is null || _activeTrack.IsTemporaryWindowSource)
        {
            return false;
        }

        StopPlayback();
        int nextFrameIndex = Math.Clamp(_activeTrack.CurrentFrameIndex + delta, 0, _activeTrack.LastFrameIndex);
        int anchorGlobalFrame = masterTimeline.Value;

        RenderTrack(_activeTrack, nextFrameIndex, updateMasterTimeline: false);

        int desiredStart = anchorGlobalFrame - _activeTrack.CurrentFrameIndex;
        _activeTrack.TimelineStartFrame = Math.Max(0, desiredStart);

        UpdateMasterTimelineBounds();
        EnsureClipTimelineCanvasWidth();
        SetGlobalTimelineFrame(anchorGlobalFrame);
        SaveSession();
        return true;
    }

    private bool AddMarkerToActiveTrack()
    {
        if (_activeTrack is null || !_activeTrack.IsLoaded || _activeTrack.IsTemporaryWindowSource)
        {
            return false;
        }

        if (_activeTrack.Markers.Add(_activeTrack.CurrentFrameIndex))
        {
            _activeTrack.SelectedMarker = _activeTrack.CurrentFrameIndex;
            AppLog.Write($"Marker added for {_activeTrack.Name} at frame {_activeTrack.CurrentFrameIndex}.");
            _clipTimelineCanvas?.Invalidate();
            SaveSession();
        }
        else
        {
            _activeTrack.SelectedMarker = _activeTrack.CurrentFrameIndex;
            _clipTimelineCanvas?.Invalidate();
        }

        return true;
    }

    private bool DeleteSelectedMarkerOnActiveTrack()
    {
        if (_activeTrack is null || !_activeTrack.IsLoaded || _activeTrack.IsTemporaryWindowSource || _activeTrack.SelectedMarker is null)
        {
            return false;
        }

        int markerToDelete = _activeTrack.SelectedMarker.Value;
        if (_activeTrack.Markers.Remove(markerToDelete))
        {
            AppLog.Write($"Marker deleted for {_activeTrack.Name} at frame {markerToDelete}.");
            _activeTrack.SelectedMarker = null;
            _clipTimelineCanvas?.Invalidate();
            SaveSession();
            return true;
        }

        return false;
    }

    private bool SetGlobalTrimIn()
    {
        if (!_leftTrack.IsLoaded && !_rightTrack.IsLoaded)
        {
            return false;
        }

        int trimIn = Math.Clamp(masterTimeline.Value, 0, Math.Max(0, masterTimeline.Maximum));
        _globalTrimInFrame = trimIn;
        if (_globalTrimOutFrame is int trimOut && trimOut < trimIn)
        {
            _globalTrimOutFrame = trimIn;
        }

        _clipTimelineCanvas?.Invalidate();
        UpdatePlaybackStatus();
        UpdateTrimActionButtonsState();
        SaveSession();
        return true;
    }

    private bool SetGlobalTrimOut()
    {
        if (!_leftTrack.IsLoaded && !_rightTrack.IsLoaded)
        {
            return false;
        }

        int trimOut = Math.Clamp(masterTimeline.Value, 0, Math.Max(0, masterTimeline.Maximum));
        _globalTrimOutFrame = trimOut;
        if (_globalTrimInFrame is int trimIn && trimIn > trimOut)
        {
            _globalTrimInFrame = trimOut;
        }

        _clipTimelineCanvas?.Invalidate();
        UpdatePlaybackStatus();
        UpdateTrimActionButtonsState();
        SaveSession();
        return true;
    }

    private bool ClearGlobalTrim()
    {
        if (_globalTrimInFrame is null && _globalTrimOutFrame is null)
        {
            return false;
        }

        _globalTrimInFrame = null;
        _globalTrimOutFrame = null;
        _clipTimelineCanvas?.Invalidate();
        UpdatePlaybackStatus();
        UpdateTrimActionButtonsState();
        SaveSession();
        return true;
    }

    private bool StepBothTracks(int delta)
    {
        if ((!_leftTrack.IsLoaded || _leftTrack.IsTemporaryWindowSource) &&
            (!_rightTrack.IsLoaded || _rightTrack.IsTemporaryWindowSource))
        {
            return false;
        }

        StopPlayback();
        SetGlobalTimelineFrame(masterTimeline.Value + delta);
        return true;
    }

    private void playPauseButton_Click(object sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        StartPlayback();
    }

    private void masterTimeline_Scroll(object? sender, EventArgs e)
    {
        if (_isUpdatingMasterTimeline)
        {
            return;
        }

        StopPlayback();
        SetGlobalTimelineFrame(masterTimeline.Value);
    }

    private void StartPlayback()
    {
        if (alignmentModeCheckBox.Checked)
        {
            MessageBox.Show(this, "Alignment mode is for frame-accurate comparison. Turn it off to use realtime playback.", "Alignment Mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool hasPlayableVideo = (_leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource) ||
            (_rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource);
        if (!hasPlayableVideo)
        {
            AppLog.Write("StartPlayback ignored because no tracks are loaded.");
            return;
        }

        AppLog.Write($"StartPlayback requested at speed {_playbackSpeedMultiplier:0.##}x.");
        PrepareTrackForPlayback(_leftTrack);
        PrepareTrackForPlayback(_rightTrack);

        _playbackTimer.Start();
        _isPlaying = true;
        playPauseButton.Text = PauseIcon;
        UpdatePlaybackStatus();
    }

    private void PrepareTrackForPlayback(VideoTrack track)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        AppLog.Write($"Preparing playback for {track.Name} at frame {track.CurrentFrameIndex}.");
        track.ShowPlayback();
        if (!track.PlayFromCurrentFrame())
        {
            AppLog.WriteError($"Playback failed to start for {track.Name}.");
            track.ShowStillFrame();
            return;
        }

        ApplyPlaybackRate(track);
        AppLog.Write($"Playback started for {track.Name}.");
        UpdateTrackInfo(track);
        UpdateAlignmentPreview();
    }

    private void ApplyPlaybackRate(VideoTrack track)
    {
        if (track.PlaybackPlayer is null)
        {
            return;
        }

        int rateApplied = track.PlaybackPlayer.SetRate(_playbackSpeedMultiplier);
        AppLog.Write($"SetRate for {track.Name}: requested={_playbackSpeedMultiplier:0.##}, applied={rateApplied}");
    }

    private void StopPlayback()
    {
        AppLog.Write($"StopPlayback requested. WasPlaying={_isPlaying}");
        _playbackTimer.Stop();

        if (_isPlaying)
        {
            SyncTrackFromPlayback(_leftTrack);
            SyncTrackFromPlayback(_rightTrack);
        }
        else
        {
            // Safety: if playback surfaces were left visible while paused, always
            // switch back to still-frame view so rendered frames are not hidden.
            EnsureStillFrameVisible(_leftTrack);
            EnsureStillFrameVisible(_rightTrack);
        }

        _isPlaying = false;
        playPauseButton.Text = PlayIcon;
        UpdatePlaybackStatus();
    }

    private static void EnsureStillFrameVisible(VideoTrack track)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        if (track.IsPlaybackVisible || track.IsPlaybackRunning)
        {
            track.StopPlayback();
        }
        else
        {
            track.ShowStillFrame();
        }
    }

    private void SyncTrackFromPlayback(VideoTrack track)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        int currentFrameIndex = track.GetCurrentPlaybackFrame();
        AppLog.Write($"Syncing {track.Name} from playback at frame {currentFrameIndex}.");
        track.StopPlayback();
        RenderTrack(track, currentFrameIndex, updateMasterTimeline: false);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        bool anyPlaying = false;

        UpdateTrackPlaybackState(_leftTrack, ref anyPlaying);
        UpdateTrackPlaybackState(_rightTrack, ref anyPlaying);

        VideoTrack? timelineTrack = _activeTrack?.IsLoaded == true
            ? _activeTrack
            : _leftTrack.IsLoaded ? _leftTrack : _rightTrack.IsLoaded ? _rightTrack : null;
        if (timelineTrack is not null)
        {
            int absoluteFrame = timelineTrack.GetCurrentPlaybackFrame() + timelineTrack.TimelineStartFrame;
            UpdateMasterTimelineFromFrame(absoluteFrame);
        }

        if (!anyPlaying)
        {
            StopPlayback();
        }

        UpdatePlaybackStatus();
    }

    private void UpdateTrackPlaybackState(VideoTrack track, ref bool anyPlaying)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource || !track.IsPlaybackVisible)
        {
            return;
        }

        if (track.IsPlaybackRunning)
        {
            anyPlaying = true;
        }
        else if (_isPlaying)
        {
            AppLog.Write($"Playback view visible but player not running for {track.Name}.");
        }

        int playbackFrame = track.GetCurrentPlaybackFrame();
        track.CurrentFrameIndex = playbackFrame;
        if (track.Timeline.Value != playbackFrame)
        {
            _isUpdatingTrackTimelines = true;
            try
            {
                track.Timeline.Value = playbackFrame;
            }
            finally
            {
                _isUpdatingTrackTimelines = false;
            }
        }

        track.MarkerPanel.Invalidate();
        UpdateTrackInfo(track);
        UpdateAlignmentPreview();
    }

    private void UpdateTrackInfo(VideoTrack track)
    {
        if (!track.IsLoaded && !track.IsTemporaryWindowSource)
        {
            track.TitleLabel.Text = track.Name;
            return;
        }

        string sourceStatus = track.IsTemporaryWindowSource
            ? $"Live window: {track.TemporaryWindowTitle ?? "Unknown"}"
            : $"Video: {Path.GetFileName(track.FilePath)}";
        track.TitleLabel.Text = $"{track.Name} | {sourceStatus}";
    }

    private void UpdatePlaybackStatus()
    {
        EnsurePreviewFramesVisible();

        string leftStatus = _leftTrack.IsTemporaryWindowSource
            ? "A live window"
            : _leftTrack.IsLoaded
            ? $"A {FormatTrackFrame(_leftTrack)}"
            : "A not loaded";
        string rightStatus = _rightTrack.IsTemporaryWindowSource
            ? "B live window"
            : _rightTrack.IsLoaded
            ? $"B {FormatTrackFrame(_rightTrack)}"
            : "B not loaded";
        string playback = _isPlaying ? "Playing" : "Paused";
        int speedPercent = (int)Math.Round(_playbackSpeedMultiplier * 100f);
        playbackStatusLabel.Text = $"{playback} {speedPercent}% speed | {leftStatus} | {rightStatus}";
        if (_globalTrimInFrame is not null || _globalTrimOutFrame is not null)
        {
            int trimIn = GetGlobalTrimIn();
            int trimOut = GetGlobalTrimOut();
            playbackStatusLabel.Text += $" | Trim {trimIn + 1:n0}-{trimOut + 1:n0}";
        }
        if (_showTimelineDebug)
        {
            int global = masterTimeline.Value;
            int leftLocal = global - _leftTrack.TimelineStartFrame;
            int rightLocal = global - _rightTrack.TimelineStartFrame;
            string leftRange = _leftTrack.IsLoaded ? (leftLocal >= 0 && leftLocal <= _leftTrack.LastFrameIndex ? "in" : "out") : "-";
            string rightRange = _rightTrack.IsLoaded ? (rightLocal >= 0 && rightLocal <= _rightTrack.LastFrameIndex ? "in" : "out") : "-";
            playbackStatusLabel.Text += $" | dbg g:{global} A:{leftLocal}({leftRange}) B:{rightLocal}({rightRange})";
        }

        UpdateTrimActionButtonsState();
    }

    private void EnsurePreviewFramesVisible()
    {
        if (_isPlaying || _isRepairingPreviewFrames)
        {
            return;
        }

        int globalFrame = Math.Clamp(masterTimeline.Value, 0, Math.Max(0, masterTimeline.Maximum));
        _isRepairingPreviewFrames = true;
        try
        {
            foreach (VideoTrack track in new[] { _leftTrack, _rightTrack })
            {
                if (!track.IsLoaded || track.IsTemporaryWindowSource)
                {
                    continue;
                }

                if (track.IsPlaybackVisible)
                {
                    track.ShowStillFrame();
                }

                bool missingStillFrame = track.PictureBox.Image is null;
                if (!missingStillFrame)
                {
                    continue;
                }

                int localFrame = globalFrame - track.TimelineStartFrame;
                if (localFrame < 0 || localFrame > track.LastFrameIndex)
                {
                    continue;
                }

                // lightweight render avoids recursive status updates.
                RenderTrack(track, localFrame, updateMasterTimeline: false, lightweight: true);
                ApplyScale(track);
                track.MarkerPanel.Invalidate();
            }
        }
        finally
        {
            _isRepairingPreviewFrames = false;
        }
    }

    private void EnsureAllPreviewScrollStates()
    {
        EnsureTrackPreviewScrollState(_leftTrack);
        EnsureTrackPreviewScrollState(_rightTrack);
    }

    private static void EnsureTrackPreviewScrollState(VideoTrack track)
    {
        if (track.IsPlaybackVisible)
        {
            return;
        }

        int availableWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int availableHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        UpdateTrackScrollState(track, availableWidth, availableHeight);
        UpdateTrackPreviewLayout(track);
    }

    private void UpdateAlignmentPreview()
    {
        if (_alignmentOverlayPictureBox is null)
        {
            return;
        }

        if (!alignmentModeCheckBox.Checked)
        {
            _alignmentOverlayPictureBox.Visible = false;
            if (_alignmentOverlayPictureBox.Image is Bitmap offBitmap)
            {
                _alignmentOverlayPictureBox.Image = null;
                offBitmap.Dispose();
            }
            _leftTrack.PictureBox.Visible = true;
            return;
        }

        if (_leftTrack.PictureBox.Image is not Bitmap leftBitmap || _rightTrack.PictureBox.Image is not Bitmap rightBitmap)
        {
            _alignmentOverlayPictureBox.Visible = false;
            _leftTrack.PictureBox.Visible = true;
            return;
        }

        Bitmap composed = ComposeAlignmentBitmap(leftBitmap, rightBitmap, 0.5f);
        if (_alignmentOverlayPictureBox.Image is Bitmap old)
        {
            _alignmentOverlayPictureBox.Image = null;
            old.Dispose();
        }
        _alignmentOverlayPictureBox.Image = composed;
        _alignmentOverlayPictureBox.Visible = true;
        _alignmentOverlayPictureBox.BringToFront();
    }

    private static Bitmap ComposeAlignmentBitmap(Bitmap baseBitmap, Bitmap overlayBitmap, float overlayAlpha)
    {
        int width = Math.Max(1, baseBitmap.Width);
        int height = Math.Max(1, baseBitmap.Height);
        var composed = new Bitmap(width, height);
        using Graphics g = Graphics.FromImage(composed);
        g.Clear(Color.Black);
        g.DrawImage(baseBitmap, 0, 0, width, height);

        using var attr = new System.Drawing.Imaging.ImageAttributes();
        var colorMatrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix33 = Math.Clamp(overlayAlpha, 0.05f, 1f)
        };
        attr.SetColorMatrix(colorMatrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
        g.DrawImage(
            overlayBitmap,
            new Rectangle(0, 0, width, height),
            0,
            0,
            overlayBitmap.Width,
            overlayBitmap.Height,
            GraphicsUnit.Pixel,
            attr);
        return composed;
    }

    private void DrawEmptyTrackHint(VideoTrack track, Graphics graphics)
    {
        if (track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        Rectangle rect = track.ImagePanel.ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var titleFont = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(210, 210, 210));
        using var bodyBrush = new SolidBrush(Color.FromArgb(155, 155, 155));

        string title = _activeTrack == track ? "Drop Video Here" : "Click To Select Side";
        string body = _activeTrack == track
            ? "Drag a video file into this area\nor click 'Use App Window' in the top bar"
            : "Then drag a video here\nor use 'Use App Window' from the top bar";

        var titleRect = new RectangleF(0, (rect.Height / 2f) - 40f, rect.Width, 30f);
        var bodyRect = new RectangleF(0, (rect.Height / 2f) - 8f, rect.Width, 60f);
        var centerFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };

        graphics.DrawString(title, titleFont, titleBrush, titleRect, centerFormat);
        graphics.DrawString(body, bodyFont, bodyBrush, bodyRect, centerFormat);
    }



    private void UpdateWindowToContent()
    {
        if (_suppressAutoWindowResize)
        {
            return;
        }

        int maxVideoWidth = Math.Max(
            _leftTrack.IsLoaded || _leftTrack.IsTemporaryWindowSource ? _leftTrack.PictureBox.Width : 0,
            _rightTrack.IsLoaded || _rightTrack.IsTemporaryWindowSource ? _rightTrack.PictureBox.Width : 0);
        int maxVideoHeight = Math.Max(
            _leftTrack.IsLoaded || _leftTrack.IsTemporaryWindowSource ? _leftTrack.PictureBox.Height : 0,
            _rightTrack.IsLoaded || _rightTrack.IsTemporaryWindowSource ? _rightTrack.PictureBox.Height : 0);

        if (maxVideoWidth <= 0 || maxVideoHeight <= 0)
        {
            return;
        }

        bool horizontal = layoutComboBox.SelectedIndex == 0;
        int previewWidth = horizontal ? (maxVideoWidth * 2) + BetweenPreviewGap : maxVideoWidth;
        int previewHeight = horizontal ? maxVideoHeight : (maxVideoHeight * 2) + BetweenPreviewGap;
        previewHeight += PreviewTitleHeight;

        int desiredClientWidth = previewWidth + videosTableLayout.Padding.Horizontal;
        int desiredClientHeight = TopBarHeight + previewHeight + BottomBarHeight;

        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        DrawingSize desiredWindow = SizeFromClientSize(new DrawingSize(desiredClientWidth, desiredClientHeight));
        int width = Math.Min(desiredWindow.Width, workingArea.Width);
        int height = Math.Min(desiredWindow.Height, workingArea.Height);

        if (WindowState == FormWindowState.Normal)
        {
            Size = new DrawingSize(
                Math.Max(MinimumSize.Width, width),
                Math.Max(MinimumSize.Height, height));
        }
    }


    private static string FormatTrackFrame(VideoTrack track)
    {
        return $"frame {track.CurrentFrameIndex + 1:n0}/{track.FrameCount:n0}";
    }


    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private const uint PrintWindowClientOnly = 0x00000001;
    private const uint PrintWindowRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private sealed class VideoTrack : IDisposable
    {
        private VideoCapture? _capture;
        private Media? _media;
        private int? _cachedFrameIndex;
        private Bitmap? _cachedFrameBitmap;
        private int? _lastReadFrameIndex;

        public VideoTrack(
            string name,
            Panel hostPanel,
            Panel imagePanel,
            PictureBox pictureBox,
            Panel footerPanel,
            Panel markerPanel,
            TrackBar timeline,
            Label infoLabel,
            Label titleLabel)
        {
            Name = name;
            HostPanel = hostPanel;
            ImagePanel = imagePanel;
            PictureBox = pictureBox;
            FooterPanel = footerPanel;
            MarkerPanel = markerPanel;
            Timeline = timeline;
            InfoLabel = infoLabel;
            TitleLabel = titleLabel;
            PlaybackView = new VideoView
            {
                BackColor = Color.Black,
                Dock = DockStyle.Fill,
                Visible = false,
                TabStop = false
            };
            FilePath = string.Empty;
            FrameSize = DrawingSize.Empty;
            Markers = [];
            ZoomMultiplier = 1.0f;
        }

        public string Name { get; }

        public Panel HostPanel { get; }

        public Panel ImagePanel { get; }

        public PictureBox PictureBox { get; }

        public Panel MarkerPanel { get; }

        public Panel FooterPanel { get; }

        public VideoView PlaybackView { get; }

        public TrackBar Timeline { get; }

        public Label InfoLabel { get; }

        public Label TitleLabel { get; }

        public MediaPlayer? PlaybackPlayer { get; private set; }

        public string FilePath { get; private set; }

        public bool IsTemporaryWindowSource { get; private set; }

        public IntPtr TemporaryWindowHandle { get; private set; }

        public string? TemporaryWindowTitle { get; private set; }

        public DrawingSize DisplayFrameSize => IsTemporaryWindowSource ? _temporaryFrameSize : FrameSize;

        public SortedSet<int> Markers { get; }

        public int? SelectedMarker { get; set; }

        public int? TrimInFrame { get; set; }

        public int? TrimOutFrame { get; set; }

        public float ZoomMultiplier { get; set; }

        public int FrameCount { get; private set; }

        public int LastFrameIndex => Math.Max(FrameCount - 1, 0);

        public double Fps { get; private set; }

        public DrawingSize FrameSize { get; private set; }

        private DrawingSize _temporaryFrameSize;

        public int CurrentFrameIndex { get; set; }

        public int TimelineStartFrame { get; set; }

        public bool IsScrubbingTimeline { get; set; }

        public int? SnapLockedMarker { get; set; }

        public bool IsLoaded => _capture is not null && !_capture.IsDisposed;

        public bool IsPlaybackVisible => PlaybackView.Visible;

        public bool IsPlaybackRunning => PlaybackPlayer?.IsPlaying == true;

        public void AttachPlaybackView()
        {
            ImagePanel.Controls.Add(PlaybackView);
            PlaybackView.BringToFront();
        }

        public void Load(string filePath, LibVLC libVlc)
        {
            ClearTemporaryWindowSource();
            DisposeCapture();
            DisposePlayback();
            ClearCachedFrame();

            _capture = new VideoCapture(filePath);
            if (!_capture.IsOpened())
            {
                throw new InvalidOperationException($"Could not open video file: {filePath}");
            }

            FilePath = filePath;
            FrameCount = Math.Max(1, (int)Math.Round(_capture.Get(VideoCaptureProperties.FrameCount)));
            Fps = Math.Max(0.001, _capture.Get(VideoCaptureProperties.Fps));
            FrameSize = new DrawingSize(
                (int)Math.Round(_capture.Get(VideoCaptureProperties.FrameWidth)),
                (int)Math.Round(_capture.Get(VideoCaptureProperties.FrameHeight)));
            CurrentFrameIndex = 0;
            TimelineStartFrame = 0;
            IsScrubbingTimeline = false;
            SnapLockedMarker = null;
            Markers.Clear();
            SelectedMarker = null;
            TrimInFrame = null;
            TrimOutFrame = null;
            ZoomMultiplier = 1.0f;
            _lastReadFrameIndex = null;

            Timeline.Minimum = 0;
            Timeline.Maximum = LastFrameIndex;
            Timeline.TickStyle = TickStyle.None;
            Timeline.SmallChange = 1;
            Timeline.LargeChange = Math.Min(LargeStep, Math.Max(1, FrameCount / 50));
            Timeline.Enabled = true;

            _media = new Media(libVlc, new Uri(filePath));
            PlaybackPlayer = new MediaPlayer(_media)
            {
                EnableHardwareDecoding = true
            };
            HookPlaybackEvents();
            PlaybackView.MediaPlayer = PlaybackPlayer;
            ShowStillFrame();
        }

        public void SetTemporaryWindowSource(IntPtr windowHandle, string windowTitle)
        {
            ClearCachedFrame();
            IsTemporaryWindowSource = true;
            TemporaryWindowHandle = windowHandle;
            TemporaryWindowTitle = windowTitle;
            _temporaryFrameSize = DrawingSize.Empty;
            ZoomMultiplier = 1.0f;
            ImagePanel.AutoScrollPosition = new DrawingPoint(0, 0);
            Timeline.Enabled = false;
            MarkerPanel.Enabled = false;
            PlaybackView.Visible = false;
            ApplyTemporarySourceVisualState();
        }

        public void ClearTemporaryWindowSource()
        {
            IsTemporaryWindowSource = false;
            TemporaryWindowHandle = IntPtr.Zero;
            TemporaryWindowTitle = null;
            _temporaryFrameSize = DrawingSize.Empty;
            Timeline.Enabled = IsLoaded;
            MarkerPanel.Enabled = true;
            ApplyTemporarySourceVisualState();
        }

        public void UpdateTemporaryFrameSize(DrawingSize frameSize)
        {
            _temporaryFrameSize = frameSize;
        }

        public void ApplyTemporarySourceVisualState()
        {
            if (IsTemporaryWindowSource)
            {
                FooterPanel.BackColor = Color.FromArgb(32, 32, 32);
                Timeline.BackColor = Color.FromArgb(35, 35, 35);
                MarkerPanel.BackColor = Color.FromArgb(38, 38, 38);
            }
            else
            {
                FooterPanel.BackColor = Color.FromArgb(45, 45, 45);
                Timeline.BackColor = Color.FromArgb(45, 45, 45);
                MarkerPanel.BackColor = Color.FromArgb(52, 52, 52);
            }
        }

        public Mat? ReadFrame(int frameIndex)
        {
            if (_capture is null)
            {
                return null;
            }

            frameIndex = Math.Clamp(frameIndex, 0, LastFrameIndex);
            bool canReadSequentially = _lastReadFrameIndex is int lastFrame && frameIndex == lastFrame + 1;
            if (!canReadSequentially)
            {
                _capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            }
            var frame = new Mat();
            if (_capture.Read(frame))
            {
                _lastReadFrameIndex = frameIndex;
                return frame;
            }

            _lastReadFrameIndex = null;
            frame.Dispose();
            return null;
        }

        public bool TryGetCachedFrameBitmap(int frameIndex, out Bitmap? bitmap)
        {
            if (_cachedFrameIndex == frameIndex && _cachedFrameBitmap is not null)
            {
                bitmap = (Bitmap)_cachedFrameBitmap.Clone();
                return true;
            }

            bitmap = null;
            return false;
        }

        public void CacheFrameBitmap(int frameIndex, Bitmap bitmap)
        {
            ClearCachedFrame();
            _cachedFrameIndex = frameIndex;
            _cachedFrameBitmap = (Bitmap)bitmap.Clone();
        }

        public void ClearCachedFrame()
        {
            _cachedFrameIndex = null;
            if (_cachedFrameBitmap is not null)
            {
                _cachedFrameBitmap.Dispose();
                _cachedFrameBitmap = null;
            }
        }

        public bool PlayFromCurrentFrame()
        {
            if (PlaybackPlayer is null)
            {
                AppLog.WriteError($"PlayFromCurrentFrame aborted for {Name}: no playback player.");
                return false;
            }

            long startTimeMs = FrameToTime(CurrentFrameIndex);
            AppLog.Write($"PlayFromCurrentFrame for {Name}: frame={CurrentFrameIndex}, timeMs={startTimeMs}");
            PlaybackPlayer.Stop();
            bool started = PlaybackPlayer.Play();
            AppLog.Write($"PlaybackPlayer.Play() for {Name} returned {started}");
            if (!started)
            {
                return false;
            }

            if (startTimeMs > 0)
            {
                PlaybackPlayer.Time = startTimeMs;
                AppLog.Write($"PlaybackPlayer.Time set for {Name}: {startTimeMs}");
            }

            return true;
        }

        public int GetCurrentPlaybackFrame()
        {
            if (!IsLoaded)
            {
                return 0;
            }

            if (PlaybackPlayer is null)
            {
                return CurrentFrameIndex;
            }

            return Math.Clamp(TimeToFrame(PlaybackPlayer.Time), 0, LastFrameIndex);
        }

        public void ShowPlayback()
        {
            PlaybackView.Visible = true;
            PlaybackView.BringToFront();
            PictureBox.Visible = false;
        }

        public void ShowStillFrame()
        {
            PlaybackView.Visible = false;
            PictureBox.Visible = true;
            PictureBox.BringToFront();
        }

        public void StopPlayback()
        {
            AppLog.Write($"Stopping playback for {Name}.");
            PlaybackPlayer?.Stop();
            ShowStillFrame();
        }

        public void Dispose()
        {
            DisposeCapture();
            DisposePlayback();
            ClearCachedFrame();

            if (PictureBox.Image is Bitmap bitmap)
            {
                PictureBox.Image = null;
                bitmap.Dispose();
            }
        }

        private long FrameToTime(int frameIndex)
        {
            return (long)Math.Round((Math.Clamp(frameIndex, 0, LastFrameIndex) / Fps) * 1000.0);
        }

        private int TimeToFrame(long timeMs)
        {
            if (timeMs <= 0)
            {
                return 0;
            }

            return (int)Math.Round((timeMs / 1000.0) * Fps);
        }

        private void DisposeCapture()
        {
            _capture?.Dispose();
            _capture = null;
            _lastReadFrameIndex = null;
            ClearCachedFrame();
        }

        private void DisposePlayback()
        {
            PlaybackView.MediaPlayer = null;
            PlaybackPlayer?.Dispose();
            PlaybackPlayer = null;
            _media?.Dispose();
            _media = null;
        }

        private void HookPlaybackEvents()
        {
            if (PlaybackPlayer is null)
            {
                return;
            }

            PlaybackPlayer.Opening += (_, _) => AppLog.Write($"{Name}: VLC Opening");
            PlaybackPlayer.Buffering += (_, e) => AppLog.Write($"{Name}: VLC Buffering {e.Cache:0.##}%");
            PlaybackPlayer.Playing += (_, _) => AppLog.Write($"{Name}: VLC Playing");
            PlaybackPlayer.Paused += (_, _) => AppLog.Write($"{Name}: VLC Paused");
            PlaybackPlayer.Stopped += (_, _) => AppLog.Write($"{Name}: VLC Stopped");
            PlaybackPlayer.EndReached += (_, _) => AppLog.Write($"{Name}: VLC EndReached");
            PlaybackPlayer.EncounteredError += (_, _) => AppLog.WriteError($"{Name}: VLC EncounteredError");
        }
    }

    private sealed record SharedComment(int Id, int FrameIndex, string Text, DateTime CreatedAt);

    private sealed record SharedCommentData(int Id, int FrameIndex, string Text, DateTime CreatedAt);

    private sealed record AppSession(
        string? ProjectName,
        string? LeftVideoPath,
        string? RightVideoPath,
        int? LeftTimelineStartFrame,
        int? RightTimelineStartFrame,
        List<int>? LeftMarkers,
        List<int>? RightMarkers,
        int? GlobalTrimInFrame,
        int? GlobalTrimOutFrame,
        int? LeftTrimInFrame,
        int? LeftTrimOutFrame,
        int? RightTrimInFrame,
        int? RightTrimOutFrame,
        List<SharedCommentData>? SharedComments,
        int? SelectedSharedCommentId,
        int? NextSharedCommentId,
        int? LayoutIndex,
        int? SpeedIndex,
        string? ActiveTrack);
}

internal static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "VideoFrameComparer.log");

    public static void Write(string message)
    {
    }

    public static void WriteError(string message, Exception? exception = null)
    {
        try
        {
            string line = exception is null
                ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ERROR {message}{Environment.NewLine}"
                : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ERROR {message} | {exception.GetType().Name}: {exception.Message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
        }
    }
}



