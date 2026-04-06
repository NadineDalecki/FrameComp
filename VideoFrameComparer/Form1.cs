using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Runtime.InteropServices;
using System.Diagnostics;
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
    private const int MarkerSelectionThresholdPixels = 18;
    private const int SharedCommentSelectionThresholdPixels = 16;
    private const int TimelineEdgePadding = 8;
    private const float WheelZoomStep = 1.1f;
    private const float MinZoomMultiplier = 1.0f;
    private const float MaxZoomMultiplier = 8.0f;
    private const int TopBarHeight = 64;
    private const int BottomBarHeight = 104;
    private const int PreviewTitleHeight = 28;
    private const int PreviewMarkerHeight = 24;
    private const int PreviewTimelineHeight = 52;
    private const int BetweenPreviewGap = 1;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int GlobalToggleDebounceMs = 250;
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
    private ListView? _sharedCommentsListView;
    private Panel? _sharedCommentMarkerPanel;
    private Button? _toggleCommentsSidebarButton;
    private Button? _expandCommentsEdgeButton;
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
    private long _lastTimelineWheelTick;
    private int _lastTimelineWheelDelta;
    private bool _timelineZoomInitialized;
    private long _lastDragPreviewTick;
    private long _lastDragStatusTick;
    private long _lastPlayheadPreviewTick;
    private bool _showTimelineDebug;
    private Panel? _transportHostPanel;
    private Panel? _transportLeftPanel;
    private Panel? _transportRightPanel;

    public Form1(string projectFilePath)
    {
        _projectFilePath = projectFilePath;
        AppLog.Write("App starting.");
        Core.Initialize();
        AppLog.Write("LibVLCSharp core initialized.");
        _libVlc = new LibVLC("--no-video-title-show");
        AppLog.Write("LibVLC instance created.");

        InitializeComponent();
        bottomTransportPanel.Height = BottomBarHeight;
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
        _libVlc.Dispose();
        base.OnFormClosing(e);
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

        var sidebarHeaderPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 24
        };
        var sidebarHeader = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Text = "Discussion",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var sidebarHint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            ForeColor = Color.FromArgb(170, 170, 170),
            Text = "Press C to add comment",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro
        };
        listView.Columns.Add("Time", 84, HorizontalAlignment.Left);
        listView.Columns.Add("Comment", 400, HorizontalAlignment.Left);
        listView.SelectedIndexChanged += SharedCommentsListView_SelectedIndexChanged;
        _sharedCommentsListView = listView;

        sidebar.Controls.Add(listView);
        sidebar.Controls.Add(sidebarHint);
        sidebar.Controls.Add(sidebarHeaderPanel);

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

        _toggleCommentsSidebarButton = new Button
        {
            Name = "toggleCommentsSidebarButton",
            Dock = DockStyle.Right,
            Width = 26,
            TabIndex = 9,
            Text = "<",
            UseVisualStyleBackColor = true
        };
        _toggleCommentsSidebarButton.Click += (_, _) =>
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
        };
        sidebarHeaderPanel.Controls.Add(_toggleCommentsSidebarButton);
        sidebarHeaderPanel.Controls.Add(sidebarHeader);

        _expandCommentsEdgeButton = new Button
        {
            Name = "expandCommentsEdgeButton",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new DrawingSize(20, 24),
            Location = new DrawingPoint(topBarPanel.Width - 24, 20),
            Width = 20,
            Text = "<",
            Visible = false,
            FlatStyle = FlatStyle.Popup,
            TabStop = false
        };
        _expandCommentsEdgeButton.Click += (_, _) =>
        {
            _isCommentsSidebarCollapsed = false;
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
        };
        topBarPanel.Controls.Add(_expandCommentsEdgeButton);
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
            // Always start from global frame 0 on open so both previews reflect the same startup playhead.
            SetGlobalTimelineFrame(0);
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

    private void SharedCommentsListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSharedCommentsList)
        {
            return;
        }

        if (_sharedCommentsListView is null || _sharedCommentsListView.SelectedItems.Count == 0)
        {
            return;
        }

        if (_sharedCommentsListView.SelectedItems[0].Tag is not SharedComment comment)
        {
            return;
        }

        SelectSharedComment(comment.Id, moveTimelineToComment: true);
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
        _timelineZoomInitialized = false;
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
        VideoTrack? trackToRestore = _activeTrack?.IsTemporaryWindowSource == true
            ? _activeTrack
            : _leftTrack.IsTemporaryWindowSource ? _leftTrack
            : _rightTrack.IsTemporaryWindowSource ? _rightTrack
            : null;
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

        restoreVideoSourceButton.Enabled = hasTemporarySource;
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
        restoreVideoSourceButton.Enabled = _leftTrack.IsTemporaryWindowSource || _rightTrack.IsTemporaryWindowSource;
        useWindowSourceButton.Enabled = true;
        track.MarkerPanel.Invalidate();
        (_activeTrack == _leftTrack ? _rightTrack : _leftTrack).MarkerPanel.Invalidate();
        _leftTrack.ImagePanel.Invalidate();
        _rightTrack.ImagePanel.Invalidate();
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

    private void SeekToFrame(VideoTrack track, int frameIndex)
    {
        if (!track.IsLoaded)
        {
            return;
        }

        StopPlayback();
        RenderTrack(track, GetSnappedFrame(track, frameIndex), updateMasterTimeline: false);
    }

    private void RenderTrack(VideoTrack track, int requestedFrameIndex, bool updateMasterTimeline = false)
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
                ClearTrackPreview(track);
                return;
            }

            nextBitmap = BitmapConverter.ToBitmap(frame);
            track.CacheFrameBitmap(safeFrameIndex, nextBitmap);
        }
        else
        {
            nextBitmap = cachedBitmap;
        }
        Bitmap? previousBitmap = track.PictureBox.Image as Bitmap;
        track.PictureBox.Image = nextBitmap;
        previousBitmap?.Dispose();

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

        UpdatePlaybackStatus();
    }

    private void UpdateLayoutMode()
    {
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
        if (_toggleCommentsSidebarButton is null)
        {
            return;
        }

        _toggleCommentsSidebarButton.Text = _isCommentsSidebarCollapsed ? ">" : "<";
        _toggleCommentsSidebarButton.AccessibleName = _isCommentsSidebarCollapsed ? "Show Comments" : "Hide Comments";
        if (_expandCommentsEdgeButton is not null)
        {
            _expandCommentsEdgeButton.Visible = _isCommentsSidebarCollapsed;
            _expandCommentsEdgeButton.BringToFront();
        }
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
        track.ImagePanel.AutoScrollMinSize = track.PictureBox.Size;
        UpdateTrackPreviewLayout(track);
    }

    private static void UpdateTrackPreviewLayout(VideoTrack track)
    {
        if (track.PictureBox.Width <= 0 || track.PictureBox.Height <= 0)
        {
            return;
        }

        int viewportWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int viewportHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        DrawingPoint scrollOffset = track.ImagePanel.AutoScrollPosition;
        int x = track.PictureBox.Width <= viewportWidth
            ? track.ImagePanel.Padding.Left + ((viewportWidth - track.PictureBox.Width) / 2)
            : track.ImagePanel.Padding.Left + scrollOffset.X;
        int y = track.PictureBox.Height <= viewportHeight
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

        track.ZoomMultiplier = nextZoom;
        ApplyScale(track);
        RestoreZoomAnchor(track, mousePoint, anchorX, anchorY);
        UpdateTrackInfo(track);
        AppLog.Write($"Zoom updated for {track.Name}: {track.ZoomMultiplier:0.##}x");
    }

    private static void RestoreZoomAnchor(VideoTrack track, DrawingPoint mousePoint, float anchorX, float anchorY)
    {
        int viewportWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int viewportHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);

        int scrollX = 0;
        int scrollY = 0;

        if (track.PictureBox.Width > viewportWidth)
        {
            double targetImageX = anchorX * track.PictureBox.Width;
            scrollX = (int)Math.Round(targetImageX - mousePoint.X);
            scrollX = Math.Clamp(scrollX, 0, Math.Max(0, track.PictureBox.Width - viewportWidth));
        }

        if (track.PictureBox.Height > viewportHeight)
        {
            double targetImageY = anchorY * track.PictureBox.Height;
            scrollY = (int)Math.Round(targetImageY - mousePoint.Y);
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

        _isPlaying = false;
        playPauseButton.Text = PlayIcon;
        UpdatePlaybackStatus();
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
        playbackStatusLabel.Text = $"{playback} {speedPercent}% | {leftStatus} | {rightStatus}";
        if (_showTimelineDebug)
        {
            int global = masterTimeline.Value;
            int leftLocal = global - _leftTrack.TimelineStartFrame;
            int rightLocal = global - _rightTrack.TimelineStartFrame;
            string leftRange = _leftTrack.IsLoaded ? (leftLocal >= 0 && leftLocal <= _leftTrack.LastFrameIndex ? "in" : "out") : "-";
            string rightRange = _rightTrack.IsLoaded ? (rightLocal >= 0 && rightLocal <= _rightTrack.LastFrameIndex ? "in" : "out") : "-";
            playbackStatusLabel.Text += $" | dbg g:{global} A:{leftLocal}({leftRange}) B:{rightLocal}({rightRange})";
        }
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

    private bool AddSharedCommentAtCurrentTimeline()
    {
        if (!PromptForComment(out string commentText))
        {
            return false;
        }

        var comment = new SharedComment(_nextSharedCommentId++, masterTimeline.Value, commentText.Trim(), DateTime.Now);
        _sharedComments.Add(comment);
        _sharedComments.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        SelectSharedComment(comment.Id, moveTimelineToComment: false);
        UpdateSharedCommentsUi();
        SaveSession();
        return true;
    }

    private static bool PromptForComment(out string commentText)
    {
        commentText = string.Empty;
        using var promptForm = new Form
        {
            Text = "Add Comment",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Width = 540,
            Height = 190
        };

        var messageLabel = new Label
        {
            Text = "Comment text",
            Left = 12,
            Top = 10,
            Width = 500
        };
        var inputBox = new TextBox
        {
            Left = 12,
            Top = 34,
            Width = 500,
            Height = 64,
            Multiline = true
        };
        var okButton = new Button
        {
            Text = "Add",
            Left = 352,
            Top = 110,
            Width = 75,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 437,
            Top = 110,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        promptForm.Controls.Add(messageLabel);
        promptForm.Controls.Add(inputBox);
        promptForm.Controls.Add(okButton);
        promptForm.Controls.Add(cancelButton);
        promptForm.AcceptButton = okButton;
        promptForm.CancelButton = cancelButton;

        if (promptForm.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        commentText = inputBox.Text.Trim();
        return !string.IsNullOrWhiteSpace(commentText);
    }

    private void UpdateSharedCommentsUi()
    {
        if (_sharedCommentsListView is null)
        {
            return;
        }

        _isUpdatingSharedCommentsList = true;
        _sharedCommentsListView.BeginUpdate();
        try
        {
            _sharedCommentsListView.Items.Clear();
            foreach (SharedComment comment in _sharedComments)
            {
                var item = new ListViewItem(FrameToSharedTimestamp(comment.FrameIndex))
                {
                    Tag = comment
                };
                item.SubItems.Add(comment.Text);
                _sharedCommentsListView.Items.Add(item);
            }

            if (_selectedSharedCommentId is int selectedId)
            {
                foreach (ListViewItem item in _sharedCommentsListView.Items)
                {
                    if (item.Tag is SharedComment comment && comment.Id == selectedId)
                    {
                        item.Selected = true;
                        item.Focused = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
        }
        finally
        {
            _sharedCommentsListView.EndUpdate();
            _isUpdatingSharedCommentsList = false;
        }
        _sharedCommentMarkerPanel?.Invalidate();
        UpdatePlaybackStatus();
    }

    private void SelectSharedComment(int commentId, bool moveTimelineToComment)
    {
        SharedComment? comment = _sharedComments.FirstOrDefault(c => c.Id == commentId);
        if (comment is null)
        {
            return;
        }

        _selectedSharedCommentId = comment.Id;
        if (moveTimelineToComment)
        {
            StopPlayback();
            SetGlobalTimelineFrame(comment.FrameIndex);
        }

        UpdateSharedCommentsUi();
    }

    private void SharedCommentMarkerPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentMarkerPanel is null || _sharedComments.Count == 0 || _sharedCommentMarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        int width = _sharedCommentMarkerPanel.ClientSize.Width - 1;
        SharedComment? nearest = null;
        int bestDistance = int.MaxValue;
        foreach (SharedComment comment in _sharedComments)
        {
            int markerX = SharedCommentFrameToX(comment.FrameIndex, width);
            int distance = Math.Abs(markerX - e.X);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = comment;
            }
        }

        if (nearest is not null && bestDistance <= SharedCommentSelectionThresholdPixels)
        {
            SelectSharedComment(nearest.Id, moveTimelineToComment: true);
        }
    }

    private void DrawSharedCommentMarkers(Graphics graphics)
    {
        if (_sharedCommentMarkerPanel is null)
        {
            return;
        }

        graphics.Clear(_sharedCommentMarkerPanel.BackColor);
        if (_sharedCommentMarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var markerPen = new Pen(Color.FromArgb(200, 200, 200), 2f);
        using var selectedPen = new Pen(Color.FromArgb(255, 184, 84), 3f);
        using var markerBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        using var selectedBrush = new SolidBrush(Color.FromArgb(255, 184, 84));
        int width = _sharedCommentMarkerPanel.ClientSize.Width - 1;
        int height = _sharedCommentMarkerPanel.ClientSize.Height - 1;
        int tipY = Math.Max(2, height - 1);
        int baseY = 1;

        foreach (SharedComment comment in _sharedComments)
        {
            int x = SharedCommentFrameToX(comment.FrameIndex, width);
            bool isSelected = _selectedSharedCommentId == comment.Id;
            graphics.DrawLine(isSelected ? selectedPen : markerPen, x, baseY, x, tipY - 2);
            DrawingPoint[] shape =
            [
                new DrawingPoint(Math.Max(0, x - 6), baseY),
                new DrawingPoint(Math.Min(width, x + 6), baseY),
                new DrawingPoint(x, tipY)
            ];
            graphics.FillPolygon(isSelected ? selectedBrush : markerBrush, shape);
        }
    }

    private int SharedCommentFrameToX(int frameIndex, int width)
    {
        _ = width;
        return FrameToTimelineX(frameIndex);
    }

    private static string FrameToSharedTimestamp(int frameIndex)
    {
        int fps = 30;
        int totalSeconds = Math.Max(0, frameIndex / fps);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private void DrawClipTimeline(Graphics graphics)
    {
        if (_clipTimelineCanvas is null)
        {
            return;
        }

        graphics.Clear(_clipTimelineCanvas.BackColor);
        int width = _clipTimelineCanvas.ClientSize.Width;
        if (width <= 0)
        {
            return;
        }

        using var rulerPen = new Pen(Color.FromArgb(80, 80, 80), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(190, 190, 190));
        using var laneBrush = new SolidBrush(Color.FromArgb(44, 44, 44));
        using var laneOutlinePen = new Pen(Color.FromArgb(62, 62, 62), 1f);
        using var clipBrush = new SolidBrush(Color.FromArgb(24, 112, 156));
        using var clipOutlinePen = new Pen(Color.FromArgb(80, 170, 220), 1f);
        using var markerBrush = new SolidBrush(Color.FromArgb(255, 208, 64));
        using var markerOutlinePen = new Pen(Color.FromArgb(126, 96, 18), 1f);
        using var commentBrush = new SolidBrush(Color.FromArgb(115, 196, 255));
        using var selectedCommentBrush = new SolidBrush(Color.FromArgb(255, 126, 198));
        using var commentOutlinePen = new Pen(Color.FromArgb(42, 70, 92), 1f);
        using var playheadPen = new Pen(Color.FromArgb(74, 158, 255), 2f);
        using var playheadHandleBrush = new SolidBrush(Color.FromArgb(74, 158, 255));
        using var playheadHandleOutline = new Pen(Color.FromArgb(210, 235, 255), 1f);
        using var font = new Font("Segoe UI", 8f, FontStyle.Regular);

        DrawTimeRuler(graphics, width, rulerPen, textBrush, font);

        DrawTimelineLane(graphics, ClipTimelineHeaderHeight, laneBrush, laneOutlinePen);
        DrawTrackClip(graphics, _leftTrack, ClipTimelineHeaderHeight, clipBrush, clipOutlinePen, markerBrush, markerOutlinePen, textBrush, font);
        int secondTrackY = ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
        DrawTimelineLane(graphics, secondTrackY, laneBrush, laneOutlinePen);
        DrawTrackClip(graphics, _rightTrack, secondTrackY, clipBrush, clipOutlinePen, markerBrush, markerOutlinePen, textBrush, font);

        DrawSharedCommentsOnTimeline(graphics, commentBrush, selectedCommentBrush, commentOutlinePen);

        int playheadX = Math.Clamp(FrameToTimelineX(masterTimeline.Value), 0, width - 1);
        graphics.DrawLine(playheadPen, playheadX, 0, playheadX, ClipTimelineCanvasHeight - 1);
        DrawingPoint[] playheadHandle =
        [
            new DrawingPoint(Math.Max(0, playheadX - 6), 1),
            new DrawingPoint(Math.Min(width - 1, playheadX + 6), 1),
            new DrawingPoint(playheadX, Math.Min(ClipTimelineHeaderHeight - 1, 10))
        ];
        graphics.FillPolygon(playheadHandleBrush, playheadHandle);
        graphics.DrawPolygon(playheadHandleOutline, playheadHandle);
    }

    private void DrawTimeRuler(Graphics graphics, int width, Pen rulerPen, Brush textBrush, Font font)
    {
        double pixelsPerSecond = Math.Max(1d, _timelinePixelsPerFrame * 30d);
        int maxSeconds = Math.Max(0, masterTimeline.Maximum / 30);

        int[] preferredSteps = [1, 5, 10, 30, 60];
        int labelStepSeconds = preferredSteps.First();
        foreach (int step in preferredSteps)
        {
            labelStepSeconds = step;
            if ((pixelsPerSecond * step) >= 70d)
            {
                break;
            }
        }

        int minorStepSeconds = labelStepSeconds >= 60 ? 10 :
            labelStepSeconds >= 30 ? 5 :
            labelStepSeconds >= 10 ? 1 : 1;

        for (int second = 0; second <= maxSeconds; second += minorStepSeconds)
        {
            int x = FrameToTimelineX(second * 30);
            if (x > width)
            {
                break;
            }

            bool isMajor = second % labelStepSeconds == 0;
            int tickBottom = isMajor ? ClipTimelineHeaderHeight : Math.Max(6, ClipTimelineHeaderHeight - 6);
            graphics.DrawLine(rulerPen, x, 0, x, tickBottom);

            if (!isMajor)
            {
                continue;
            }

            string label = $"{second / 60:00}:{second % 60:00}";
            var labelSize = graphics.MeasureString(label, font);
            bool fitsToNext = second + labelStepSeconds > maxSeconds ||
                (FrameToTimelineX((second + labelStepSeconds) * 30) - x) >= (int)labelSize.Width + 6;
            if (fitsToNext)
            {
                graphics.DrawString(label, font, textBrush, x + 2, 2);
            }
        }
    }

    private void DrawSharedCommentsOnTimeline(Graphics graphics, Brush commentBrush, Brush selectedCommentBrush, Pen outlinePen)
    {
        if (_sharedComments.Count == 0)
        {
            return;
        }

        int markerTop = 2;
        int markerBottom = Math.Max(markerTop + 4, ClipTimelineHeaderHeight - 2);
        int width = _clipTimelineCanvas?.ClientSize.Width ?? 0;
        foreach (SharedComment comment in _sharedComments)
        {
            int x = Math.Clamp(FrameToTimelineX(comment.FrameIndex), 0, Math.Max(0, width - 1));
            var points = new[]
            {
                new DrawingPoint(x - 5, markerTop),
                new DrawingPoint(x + 5, markerTop),
                new DrawingPoint(x, markerBottom)
            };
            Brush fill = _selectedSharedCommentId == comment.Id ? selectedCommentBrush : commentBrush;
            graphics.FillPolygon(fill, points);
            graphics.DrawPolygon(outlinePen, points);
        }
    }

    private static void DrawTimelineLane(Graphics graphics, int y, Brush laneBrush, Pen laneOutlinePen)
    {
        var laneRect = new Rectangle(0, y, Math.Max(1, (int)graphics.ClipBounds.Width), ClipTimelineTrackHeight);
        graphics.FillRectangle(laneBrush, laneRect);
        graphics.DrawRectangle(laneOutlinePen, laneRect);
    }

    private void DrawTrackClip(
        Graphics graphics,
        VideoTrack track,
        int y,
        Brush clipBrush,
        Pen clipOutlinePen,
        Brush markerBrush,
        Pen markerOutlinePen,
        Brush textBrush,
        Font font)
    {
        if (!track.IsLoaded)
        {
            graphics.DrawString($"{track.Name} (not loaded)", font, textBrush, 8, y + 3);
            return;
        }

        int clipX = Math.Max(0, FrameToTimelineX(track.TimelineStartFrame));
        int clipWidth = Math.Max(40, FramesToPixels(track.FrameCount));
        var clipRect = new Rectangle(clipX, y, clipWidth, ClipTimelineTrackHeight);
        bool isActive = _activeTrack == track;
        bool isDragging = _draggingTimelineTrack == track;
        bool isHover = _hoverTimelineTrack == track;
        if (isDragging)
        {
            using var dragBrush = new SolidBrush(Color.FromArgb(34, 139, 186));
            using var dragOutline = new Pen(Color.FromArgb(170, 215, 245), 2f);
            graphics.FillRectangle(dragBrush, clipRect);
            graphics.DrawRectangle(dragOutline, clipRect);
        }
        else if (isHover)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(30, 122, 170));
            using var hoverOutline = new Pen(Color.FromArgb(120, 190, 230), 2f);
            graphics.FillRectangle(hoverBrush, clipRect);
            graphics.DrawRectangle(hoverOutline, clipRect);
        }
        else
        {
            if (isActive)
            {
                using var activeBrush = new SolidBrush(Color.FromArgb(38, 142, 196));
                using var activeOutline = new Pen(Color.FromArgb(140, 214, 252), 2f);
                graphics.FillRectangle(activeBrush, clipRect);
                graphics.DrawRectangle(activeOutline, clipRect);
            }
            else
            {
                using var inactiveBrush = new SolidBrush(Color.FromArgb(18, 92, 128));
                using var inactiveOutline = new Pen(Color.FromArgb(84, 150, 184), 1f);
                graphics.FillRectangle(inactiveBrush, clipRect);
                graphics.DrawRectangle(inactiveOutline, clipRect);
            }
        }
        graphics.DrawString(Path.GetFileName(track.FilePath), font, textBrush, clipRect.X + 6, clipRect.Y + 3);

        foreach (int marker in track.Markers)
        {
            int markerX = clipX + FramesToPixels(marker);
            if (markerX < clipRect.Left || markerX > clipRect.Right)
            {
                continue;
            }

            DrawingPoint[] markerShape =
            [
                new DrawingPoint(markerX, y + ClipTimelineTrackHeight - 2),
                new DrawingPoint(markerX - 4, y + 4),
                new DrawingPoint(markerX + 4, y + 4)
            ];
            graphics.FillPolygon(markerBrush, markerShape);
            graphics.DrawPolygon(markerOutlinePen, markerShape);
        }
    }

    private void ClipTimelineCanvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (TrySelectSharedCommentAtPosition(e.Location))
        {
            return;
        }

        if (TrySelectTrackMarkerAtPosition(e.Location))
        {
            return;
        }

        int playheadX = FrameToTimelineX(masterTimeline.Value);
        if (Math.Abs(e.X - playheadX) <= PlayheadDragThresholdPixels)
        {
            _isDraggingPlayhead = true;
            _clipTimelineCanvas.Cursor = Cursors.SizeWE;
            _lastPlayheadPreviewTick = 0;
            SetGlobalTimelineFrame(TimelineXToFrame(e.X));
            return;
        }

        _draggingTimelineTrack = GetTimelineTrackAtPosition(e.Location);
        if (_draggingTimelineTrack is not null)
        {
            _dragStartMouseX = e.X;
            _dragStartTrackOffset = _draggingTimelineTrack.TimelineStartFrame;
            SetActiveTrack(_draggingTimelineTrack);
            _clipTimelineCanvas.Cursor = Cursors.SizeWE;
            _lastDragPreviewTick = 0;
            _clipTimelineCanvas?.Invalidate();
            return;
        }

        SetGlobalTimelineFrame(TimelineXToFrame(e.X));
    }

    private void ClipTimelineCanvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_clipTimelineCanvas is null)
        {
            return;
        }

        if (_draggingTimelineTrack is null)
        {
            VideoTrack? hoverTrack = GetTimelineTrackAtPosition(e.Location);
            if (_hoverTimelineTrack != hoverTrack)
            {
                _hoverTimelineTrack = hoverTrack;
                _clipTimelineCanvas.Cursor = hoverTrack is not null ? Cursors.SizeWE : Cursors.Default;
                _clipTimelineCanvas?.Invalidate();
            }
        }

        if (_isDraggingPlayhead && e.Button == MouseButtons.Left)
        {
            int targetFrame = TimelineXToFrame(e.X);
            ApplyGlobalFrame(targetFrame, isDrag: true);
            long now = Environment.TickCount64;
            if (now - _lastDragStatusTick >= 120)
            {
                UpdatePlaybackStatus();
                _lastDragStatusTick = now;
            }
            return;
        }

        if (_draggingTimelineTrack is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        int delta = TimelineXToFrame(e.X) - TimelineXToFrame(_dragStartMouseX);
        int nextStart = Math.Max(0, _dragStartTrackOffset + delta);
        if (_draggingTimelineTrack.TimelineStartFrame == nextStart)
        {
            return;
        }

        _draggingTimelineTrack.TimelineStartFrame = nextStart;
        EnsureClipTimelineCanvasWidth();
        _clipTimelineCanvas.Invalidate();

        long dragNow = Environment.TickCount64;
        if (dragNow - _lastDragStatusTick >= 120)
        {
            UpdatePlaybackStatus();
            _lastDragStatusTick = dragNow;
        }
    }

    private void ClipTimelineCanvas_MouseWheel(object? sender, MouseEventArgs e)
    {
        // Keep timeline always fully visible; suppress scroll takeover.
        EnsureClipTimelineCanvasWidth();
        _clipTimelineCanvas?.Invalidate();

        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    private VideoTrack? GetTimelineTrackAtPosition(DrawingPoint point)
    {
        if (!_leftTrack.IsLoaded && !_rightTrack.IsLoaded)
        {
            return null;
        }

        int leftY = ClipTimelineHeaderHeight;
        int rightY = ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
        if (_leftTrack.IsLoaded && point.Y >= leftY && point.Y <= leftY + ClipTimelineTrackHeight)
        {
            int leftX = FrameToTimelineX(_leftTrack.TimelineStartFrame);
            int leftW = Math.Max(40, FramesToPixels(_leftTrack.FrameCount));
            if (point.X >= leftX && point.X <= leftX + leftW)
            {
                return _leftTrack;
            }
        }

        if (_rightTrack.IsLoaded && point.Y >= rightY && point.Y <= rightY + ClipTimelineTrackHeight)
        {
            int rightX = FrameToTimelineX(_rightTrack.TimelineStartFrame);
            int rightW = Math.Max(40, FramesToPixels(_rightTrack.FrameCount));
            if (point.X >= rightX && point.X <= rightX + rightW)
            {
                return _rightTrack;
            }
        }

        return null;
    }

    private bool TrySelectTrackMarkerAtPosition(DrawingPoint point)
    {
        foreach (VideoTrack track in new[] { _leftTrack, _rightTrack })
        {
            if (!track.IsLoaded)
            {
                continue;
            }

            int laneY = track == _leftTrack
                ? ClipTimelineHeaderHeight
                : ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
            if (point.Y < laneY || point.Y > laneY + ClipTimelineTrackHeight)
            {
                continue;
            }

            int clipX = FrameToTimelineX(track.TimelineStartFrame);
            foreach (int marker in track.Markers)
            {
                int markerX = clipX + FramesToPixels(marker);
                if (Math.Abs(markerX - point.X) <= MarkerSelectionThresholdPixels)
                {
                    SetActiveTrack(track);
                    track.SelectedMarker = marker;
                    _clipTimelineCanvas?.Invalidate();
                    return true;
                }
            }
        }

        return false;
    }

    private bool TrySelectSharedCommentAtPosition(DrawingPoint point)
    {
        if (_sharedComments.Count == 0 || point.Y < 0 || point.Y > ClipTimelineHeaderHeight)
        {
            return false;
        }

        SharedComment? best = null;
        int bestDistance = int.MaxValue;
        foreach (SharedComment comment in _sharedComments)
        {
            int x = FrameToTimelineX(comment.FrameIndex);
            int distance = Math.Abs(x - point.X);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = comment;
            }
        }

        if (best is null || bestDistance > SharedCommentSelectionThresholdPixels)
        {
            return false;
        }

        SelectSharedComment(best.Id, moveTimelineToComment: true);
        return true;
    }

    private void EnsureClipTimelineCanvasWidth()
    {
        if (_clipTimelineCanvas is null || _clipTimelineViewport is null)
        {
            return;
        }

        int contentEndFrame = Math.Max(
            _leftTrack.IsLoaded ? _leftTrack.TimelineStartFrame + _leftTrack.FrameCount : 0,
            _rightTrack.IsLoaded ? _rightTrack.TimelineStartFrame + _rightTrack.FrameCount : 0);
        int maxFrame = Math.Max(1, (int)Math.Ceiling(contentEndFrame * TimelineMinPaddingFactor));
        masterTimeline.Maximum = Math.Max(1, maxFrame);
        masterTimeline.LargeChange = Math.Max(1, masterTimeline.Maximum / 100);
        masterTimeline.SmallChange = 1;

        double fitZoom = GetMinimumTimelineZoom();
        _timelinePixelsPerFrame = fitZoom;
        _timelineZoomInitialized = true;

        int targetWidth = Math.Max(1, _clipTimelineViewport.ClientSize.Width);
        _clipTimelineCanvas.Width = Math.Max(1, targetWidth);
        _clipTimelineCanvas.Height = ClipTimelineCanvasHeight - 2;

        _isUpdatingMasterTimeline = true;
        _isUpdatingMasterTimeline = false;
    }

    private double GetMinimumTimelineZoom()
    {
        int viewportWidth = Math.Max(1, _clipTimelineViewport?.ClientSize.Width ?? 1);
        int contentEndFrame = Math.Max(
            _leftTrack.IsLoaded ? _leftTrack.TimelineStartFrame + _leftTrack.FrameCount : 0,
            _rightTrack.IsLoaded ? _rightTrack.TimelineStartFrame + _rightTrack.FrameCount : 0);
        int paddedFrameSpan = Math.Max(1, (int)Math.Ceiling(contentEndFrame * TimelineMinPaddingFactor));
        return Math.Max(0.0001d, viewportWidth / (double)paddedFrameSpan);
    }

    private void SetGlobalTimelineFrame(int frameIndex)
    {
        ApplyGlobalFrame(frameIndex, isDrag: false);
    }

    private void SetGlobalTimelineFrameDuringDrag(int frameIndex)
    {
        EnsureClipTimelineCanvasWidth();
        int safeGlobal = Math.Clamp(frameIndex, 0, Math.Max(0, masterTimeline.Maximum));
        UpdateMasterTimelineFromFrame(safeGlobal);

        // Keep marker movement responsive while still providing live preview.
        // We clear out-of-range tracks immediately and throttle expensive in-range frame decoding.
        long now = Environment.TickCount64;
        bool shouldRenderInRange = now - _lastPlayheadPreviewTick >= 33;
        PreviewTrackDuringDrag(_leftTrack, safeGlobal, shouldRenderInRange);
        PreviewTrackDuringDrag(_rightTrack, safeGlobal, shouldRenderInRange);
        if (shouldRenderInRange)
        {
            _lastPlayheadPreviewTick = now;
        }

        _clipTimelineCanvas?.Invalidate();
    }

    private void ApplyGlobalFrame(int frameIndex, bool isDrag)
    {
        if (isDrag)
        {
            SetGlobalTimelineFrameDuringDrag(frameIndex);
            return;
        }

        EnsureClipTimelineCanvasWidth();
        int safeGlobal = Math.Clamp(frameIndex, 0, Math.Max(0, masterTimeline.Maximum));
        UpdateMasterTimelineFromFrame(safeGlobal);
        RenderTrackForGlobalFrame(_leftTrack, safeGlobal);
        RenderTrackForGlobalFrame(_rightTrack, safeGlobal);
        _clipTimelineCanvas?.Invalidate();
    }

    private void PreviewTrackDuringDrag(VideoTrack track, int globalFrame, bool shouldRenderInRange)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        int localFrame = globalFrame - track.TimelineStartFrame;
        if (localFrame < 0 || localFrame > track.LastFrameIndex)
        {
            ClearTrackPreview(track);
            return;
        }

        if (shouldRenderInRange)
        {
            RenderTrack(track, localFrame, updateMasterTimeline: false);
        }
    }

    private void RenderTrackForGlobalFrame(VideoTrack track, int globalFrame)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        int localFrame = globalFrame - track.TimelineStartFrame;
        if (localFrame < 0 || localFrame > track.LastFrameIndex)
        {
            ClearTrackPreview(track);
            return;
        }

        RenderTrack(track, localFrame, updateMasterTimeline: false);
    }

    private void ClearTrackPreview(VideoTrack track)
    {
        if (track.PictureBox.Image is Bitmap previousBitmap)
        {
            track.PictureBox.Image = null;
            previousBitmap.Dispose();
        }

        track.ShowStillFrame();
        ApplyScale(track);
        UpdatePlaybackStatus();
        UpdateAlignmentPreview();
    }

    private int FrameToTimelineX(int frame)
    {
        return Math.Max(0, (int)Math.Round(frame * _timelinePixelsPerFrame));
    }

    private int TimelineXToFrame(int x)
    {
        if (_timelinePixelsPerFrame <= 0.0001d)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Round(x / _timelinePixelsPerFrame));
    }

    private int FramesToPixels(int frames)
    {
        return Math.Max(1, (int)Math.Round(frames * _timelinePixelsPerFrame));
    }

    private static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(control, true, null);
    }

    private void DrawMarkers(VideoTrack track, Graphics graphics)
    {
        graphics.Clear(track.MarkerPanel.BackColor);
        if (track.MarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var markerLinePen = new Pen(Color.FromArgb(255, 208, 64), 2f);
        using var selectedMarkerLinePen = new Pen(Color.FromArgb(255, 145, 77), 3f);
        using var snapLockedLinePen = new Pen(Color.FromArgb(110, 190, 255), 3f);
        using var markerBrush = new SolidBrush(Color.FromArgb(255, 208, 64));
        using var markerOutlinePen = new Pen(Color.FromArgb(126, 96, 18), 1f);
        using var selectedBrush = new SolidBrush(Color.FromArgb(255, 145, 77));
        using var selectedOutlinePen = new Pen(Color.FromArgb(255, 214, 181), 1f);
        using var snapLockedBrush = new SolidBrush(Color.FromArgb(110, 190, 255));
        using var snapLockedOutlinePen = new Pen(Color.FromArgb(185, 225, 255), 1f);
        int width = track.MarkerPanel.ClientSize.Width - 1;
        int height = track.MarkerPanel.ClientSize.Height - 1;
        int baselineY = height - 5;

        if (!track.IsLoaded)
        {
            return;
        }

        foreach (int marker in track.Markers)
        {
            int x = MarkerFrameToX(track, marker, width);
            bool isSelected = track.SelectedMarker == marker;
            bool isSnapLocked = track.SnapLockedMarker == marker && track.IsScrubbingTimeline;
            Pen linePen = isSnapLocked ? snapLockedLinePen : isSelected ? selectedMarkerLinePen : markerLinePen;
            Brush fillBrush = isSnapLocked ? snapLockedBrush : isSelected ? selectedBrush : markerBrush;
            Pen outlinePen = isSnapLocked ? snapLockedOutlinePen : isSelected ? selectedOutlinePen : markerOutlinePen;
            graphics.DrawLine(linePen, x, 2, x, baselineY - 2);
            DrawingPoint[] markerShape =
            [
                new DrawingPoint(x, 3),
                new DrawingPoint(Math.Max(0, x - 7), baselineY - 1),
                new DrawingPoint(Math.Min(width, x + 7), baselineY - 1)
            ];
            graphics.FillPolygon(fillBrush, markerShape);
            graphics.DrawPolygon(outlinePen, markerShape);
        }

    }

    private static int MarkerFrameToX(VideoTrack track, int frameIndex, int width)
    {
        int usableWidth = GetTimelineUsableWidth(width);
        if (track.LastFrameIndex <= 0 || usableWidth <= 0)
        {
            return TimelineEdgePadding;
        }

        double ratio = Math.Clamp(frameIndex / (double)track.LastFrameIndex, 0d, 1d);
        return TimelineEdgePadding + (int)Math.Round(ratio * usableWidth);
    }

    private int GetSnappedFrame(VideoTrack track, int requestedFrameIndex)
    {
        int referenceWidth = track.Timeline.ClientSize.Width;
        if (referenceWidth <= 0 && _clipTimelineCanvas is not null)
        {
            referenceWidth = _clipTimelineCanvas.ClientSize.Width;
        }

        if (track.Markers.Count == 0 || track.LastFrameIndex <= 0 || referenceWidth <= 0)
        {
            track.SnapLockedMarker = null;
            return requestedFrameIndex;
        }

        int usableWidth = GetTimelineUsableWidth(referenceWidth);
        double pixelsPerFrame = usableWidth / (double)Math.Max(1, track.LastFrameIndex);
        int lockThresholdFrames = Math.Max(1, (int)Math.Round(MarkerSnapThresholdPixels / pixelsPerFrame));
        int releaseThresholdFrames = Math.Max(lockThresholdFrames + 1, (int)Math.Round(MarkerSnapReleaseThresholdPixels / pixelsPerFrame));

        if (track.SnapLockedMarker is int lockedMarker)
        {
            int lockDistance = Math.Abs(lockedMarker - requestedFrameIndex);
            if (lockDistance <= releaseThresholdFrames)
            {
                return lockedMarker;
            }

            track.SnapLockedMarker = null;
        }

        int nearestMarker = requestedFrameIndex;
        int nearestDistance = int.MaxValue;
        foreach (int marker in track.Markers)
        {
            int distance = Math.Abs(marker - requestedFrameIndex);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestMarker = marker;
            }
        }

        if (nearestDistance <= lockThresholdFrames)
        {
            track.SnapLockedMarker = nearestMarker;
            return nearestMarker;
        }

        track.SnapLockedMarker = null;
        return requestedFrameIndex;
    }

    private void HandleMarkerPanelMouseDown(VideoTrack track, MouseEventArgs e)
    {
        SetActiveTrack(track);
        if (!track.IsLoaded || track.Markers.Count == 0 || track.MarkerPanel.ClientSize.Width <= 0)
        {
            track.SelectedMarker = null;
            track.MarkerPanel.Invalidate();
            return;
        }

        int width = track.MarkerPanel.ClientSize.Width - 1;
        int? selectedMarker = null;
        int bestDistance = int.MaxValue;

        foreach (int marker in track.Markers)
        {
            int markerX = MarkerFrameToX(track, marker, width);
            int distance = Math.Abs(markerX - e.X);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                selectedMarker = marker;
            }
        }

        track.SelectedMarker = bestDistance <= MarkerSelectionThresholdPixels ? selectedMarker : null;
        track.MarkerPanel.Invalidate();
    }

    private static int GetTimelineUsableWidth(int totalWidth)
    {
        return Math.Max(1, totalWidth - (TimelineEdgePadding * 2) - 1);
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

    private void RestoreSession()
    {
        try
        {
            if (!File.Exists(_projectFilePath))
            {
                return;
            }

            AppSession? session = JsonSerializer.Deserialize<AppSession>(File.ReadAllText(_projectFilePath));
            if (session is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.ProjectName))
            {
                Text = $"Video Frame Comparer - {session.ProjectName}";
            }

            _isRestoringSession = true;
            layoutComboBox.SelectedIndex = session.LayoutIndex is >= 0 and <= 1 ? session.LayoutIndex.Value : 0;
            speedComboBox.SelectedIndex = session.SpeedIndex is >= 0 and <= 6 ? session.SpeedIndex.Value : 3;
            _nextSharedCommentId = session.NextSharedCommentId is > 0 ? session.NextSharedCommentId.Value : 1;
            _sharedComments.Clear();
            if (session.SharedComments is not null)
            {
                foreach (SharedCommentData comment in session.SharedComments)
                {
                    if (string.IsNullOrWhiteSpace(comment.Text))
                    {
                        continue;
                    }

                    _sharedComments.Add(new SharedComment(comment.Id, Math.Max(0, comment.FrameIndex), comment.Text, comment.CreatedAt));
                    _nextSharedCommentId = Math.Max(_nextSharedCommentId, comment.Id + 1);
                }
            }
            _selectedSharedCommentId = session.SelectedSharedCommentId;
            UpdateSharedCommentsUi();

            string? leftVideoPath = ResolveVideoPath(session.LeftVideoPath, _leftTrack.Name);
            if (!string.IsNullOrWhiteSpace(leftVideoPath))
            {
                LoadVideoIntoTrack(_leftTrack, leftVideoPath);
                _leftTrack.TimelineStartFrame = Math.Max(0, session.LeftTimelineStartFrame ?? 0);
                ApplyMarkers(_leftTrack, session.LeftMarkers);
            }

            string? rightVideoPath = ResolveVideoPath(session.RightVideoPath, _rightTrack.Name);
            if (!string.IsNullOrWhiteSpace(rightVideoPath))
            {
                LoadVideoIntoTrack(_rightTrack, rightVideoPath);
                _rightTrack.TimelineStartFrame = Math.Max(0, session.RightTimelineStartFrame ?? 0);
                ApplyMarkers(_rightTrack, session.RightMarkers);
            }

            EnsureClipTimelineCanvasWidth();
            _clipTimelineCanvas?.Invalidate();

            if (session.ActiveTrack == "right" && _rightTrack.IsLoaded)
            {
                SetActiveTrack(_rightTrack);
            }
            else if (_leftTrack.IsLoaded)
            {
                SetActiveTrack(_leftTrack);
            }
        }
        catch
        {
            AppLog.WriteError($"Failed to restore session from {_projectFilePath}.");
        }
        finally
        {
            _isRestoringSession = false;
        }
    }

    private string? ResolveVideoPath(string? savedPath, string trackName)
    {
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return null;
        }

        if (File.Exists(savedPath))
        {
            return savedPath;
        }

        DialogResult relinkPrompt = MessageBox.Show(
            this,
            $"{trackName} could not find the saved video file:\n\n{savedPath}\n\nDo you want to locate the file manually?",
            "Video File Missing",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (relinkPrompt != DialogResult.Yes)
        {
            return null;
        }

        using var openDialog = new OpenFileDialog
        {
            Title = $"Locate {trackName}",
            Filter = VideoFileDialogFilter,
            CheckFileExists = true,
            Multiselect = false,
            FileName = Path.GetFileName(savedPath)
        };

        string? savedDirectory = Path.GetDirectoryName(savedPath);
        if (!string.IsNullOrWhiteSpace(savedDirectory) && Directory.Exists(savedDirectory))
        {
            openDialog.InitialDirectory = savedDirectory;
        }

        return openDialog.ShowDialog(this) == DialogResult.OK ? openDialog.FileName : null;
    }

    private void SaveSession()
    {
        if (_isRestoringSession)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_projectFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var session = new AppSession(
                ProjectName: Path.GetFileNameWithoutExtension(_projectFilePath),
                LeftVideoPath: _leftTrack.IsLoaded ? _leftTrack.FilePath : null,
                RightVideoPath: _rightTrack.IsLoaded ? _rightTrack.FilePath : null,
                LeftTimelineStartFrame: _leftTrack.IsLoaded ? _leftTrack.TimelineStartFrame : 0,
                RightTimelineStartFrame: _rightTrack.IsLoaded ? _rightTrack.TimelineStartFrame : 0,
                LeftMarkers: [.. _leftTrack.Markers],
                RightMarkers: [.. _rightTrack.Markers],
                SharedComments: [.. _sharedComments.Select(c => new SharedCommentData(c.Id, c.FrameIndex, c.Text, c.CreatedAt))],
                SelectedSharedCommentId: _selectedSharedCommentId,
                NextSharedCommentId: _nextSharedCommentId,
                LayoutIndex: layoutComboBox.SelectedIndex,
                SpeedIndex: speedComboBox.SelectedIndex,
                ActiveTrack: _activeTrack == _rightTrack ? "right" : "left");
            File.WriteAllText(_projectFilePath, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            AppLog.WriteError($"Failed to save session to {_projectFilePath}.");
        }
    }

    private static void ApplyMarkers(VideoTrack track, List<int>? markers)
    {
        track.Markers.Clear();
        track.SelectedMarker = null;
        if (markers is not null)
        {
            foreach (int marker in markers)
            {
                if (marker >= 0 && marker <= track.LastFrameIndex)
                {
                    track.Markers.Add(marker);
                }
            }
        }

        track.MarkerPanel.Invalidate();
    }

    private static string FormatTrackFrame(VideoTrack track)
    {
        return $"frame {track.CurrentFrameIndex + 1:n0}/{track.FrameCount:n0}";
    }

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
            GetWindowText(handle, builder, builder.Capacity);
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

    [StructLayout(LayoutKind.Sequential)]
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
            ZoomMultiplier = 1.0f;

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
            _capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            var frame = new Mat();
            return _capture.Read(frame) ? frame : null;
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
