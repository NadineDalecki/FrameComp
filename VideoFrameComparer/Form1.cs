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

    private void BeginExportOperation(int jobCount, string preparingStatus, out int restoreGlobalFrame, out TrimExportProgressForm progressForm)
    {
        StopPlayback();
        restoreGlobalFrame = masterTimeline.Value;
        progressForm = new TrimExportProgressForm(Math.Max(1, jobCount));
        progressForm.Show(this);
        progressForm.UpdateProgress(0, 0d, preparingStatus);
        Application.DoEvents();
        _isSavingTrimmedVideos = true;
        UpdateTrimActionButtonsState();
    }

    private void EndExportOperation(int restoreGlobalFrame, TrimExportProgressForm progressForm)
    {
        _isSavingTrimmedVideos = false;
        UpdateTrimActionButtonsState();
        SetGlobalTimelineFrame(restoreGlobalFrame);
        progressForm.Close();
        progressForm.Dispose();
    }

    private void SaveTrimmedVideosButton_Click(object? sender, EventArgs e)
    {
        if (_isSavingTrimmedVideos || !CanExportTrimRange())
        {
            return;
        }

        bool hasFfmpeg = TryResolveFfmpegExecutable(out string? ffmpegExecutable);
        RecordDiagnostics(ffmpegStatus: hasFfmpeg ? $"Available ({ffmpegExecutable})" : "Missing");
        if (!hasFfmpeg)
        {
            DialogResult installPrompt = MessageBox.Show(
                this,
                "FFmpeg was not found.\n\nDo you want FrameComp to try installing it automatically now?\n\nYes = install FFmpeg\nNo = continue with built-in Accurate export (slower)\nCancel = abort export",
                "FFmpeg Not Found",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (installPrompt == DialogResult.Cancel)
            {
                return;
            }
            if (installPrompt == DialogResult.Yes)
            {
                if (TryInstallFfmpegForUser(out string installMessage))
                {
                    hasFfmpeg = TryResolveFfmpegExecutable(out ffmpegExecutable);
                    RecordDiagnostics(ffmpegStatus: hasFfmpeg ? $"Available ({ffmpegExecutable})" : "Missing");
                }

                if (!hasFfmpeg)
                {
                    MessageBox.Show(this, $"{installMessage}\n\nUsing built-in Accurate export.", "FFmpeg Not Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "Select output folder for trimmed videos",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        string? projectDir = Path.GetDirectoryName(_projectFilePath);
        if (!string.IsNullOrWhiteSpace(projectDir) && Directory.Exists(projectDir))
        {
            folderDialog.SelectedPath = projectDir;
        }

        if (folderDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
        {
            return;
        }

        int trimIn = GetGlobalTrimIn();
        int trimOut = GetGlobalTrimOut();
        var savedFiles = new List<string>();
        var skippedReasons = new List<string>();
        var plans = new List<TrimExportPlan>();
        foreach (VideoTrack track in new[] { _leftTrack, _rightTrack })
        {
            if (!TryGetTrackTrimRange(track, trimIn, trimOut, out int localStart, out int localEnd, out string rangeReason))
            {
                skippedReasons.Add(rangeReason);
                continue;
            }

            plans.Add(new TrimExportPlan(track, localStart, localEnd));
        }

        if (plans.Count == 0)
        {
            string reason = skippedReasons.Count > 0
                ? string.Join("\n", skippedReasons)
                : "No exportable track was found for the selected range.";
            MessageBox.Show(this, reason, "Nothing Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BeginExportOperation(plans.Count, $"Preparing {plans.Count} export job(s)...", out int restoreGlobalFrame, out TrimExportProgressForm progressForm);
        try
        {
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                TrimExportPlan plan = plans[planIndex];
                string modeLabel = "Accurate";
                progressForm.UpdateProgress(planIndex, 0d, $"{modeLabel}: {plan.Track.Name}");
                Application.DoEvents();

                bool exported = false;
                if (hasFfmpeg && ffmpegExecutable is not null)
                {
                    exported = TryExportTrimmedTrackWithFfmpeg(
                        ffmpegExecutable,
                        plan,
                        folderDialog.SelectedPath,
                        ratio =>
                        {
                            progressForm.UpdateProgress(planIndex, ratio, $"{modeLabel}: {plan.Track.Name}");
                            Application.DoEvents();
                        },
                        out string? outputPath,
                        out string message);
                    if (exported)
                    {
                        if (!string.IsNullOrWhiteSpace(outputPath))
                        {
                            savedFiles.Add(outputPath);
                        }
                    }
                    else
                    {
                        skippedReasons.Add(message);
                    }
                }
                else
                {
                    exported = TryExportTrimmedTrackBuiltIn(
                        plan,
                        folderDialog.SelectedPath,
                        ratio =>
                        {
                            progressForm.UpdateProgress(planIndex, ratio, $"{modeLabel}: {plan.Track.Name}");
                            Application.DoEvents();
                        },
                        out string? outputPath,
                        out string message);
                    if (exported)
                    {
                        if (!string.IsNullOrWhiteSpace(outputPath))
                        {
                            savedFiles.Add(outputPath);
                        }
                    }
                    else
                    {
                        skippedReasons.Add(message);
                    }
                }
            }
        }
        finally
        {
            EndExportOperation(restoreGlobalFrame, progressForm);
        }

        if (savedFiles.Count > 0)
        {
            string success = "Saved trimmed videos:\n" + string.Join("\n", savedFiles.Select(Path.GetFileName));
            string skipped = skippedReasons.Count > 0
                ? $"\n\nSkipped:\n{string.Join("\n", skippedReasons)}"
                : string.Empty;
            RecordDiagnostics(exportStatus: $"Trim export OK ({savedFiles.Count} files)", error: skippedReasons.Count > 0 ? skippedReasons[0] : "None");
            MessageBox.Show(this, success + skipped, "Trim Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string failure = skippedReasons.Count > 0
            ? string.Join("\n", skippedReasons)
            : "No exportable track was found for the selected range.";
        RecordDiagnostics(exportStatus: "Trim export failed", error: failure.Split('\n').FirstOrDefault() ?? failure);
        MessageBox.Show(this, failure, "Nothing Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SaveCombinedVideoButton_Click(object? sender, EventArgs e)
    {
        if (_isSavingTrimmedVideos || !CanExportCombinedRange())
        {
            return;
        }

        using var saveDialog = new SaveFileDialog
        {
            Title = "Save Combined Video",
            Filter = "MP4 Video (*.mp4)|*.mp4",
            DefaultExt = "mp4",
            AddExtension = true,
            OverwritePrompt = true
        };
        string? projectDir = Path.GetDirectoryName(_projectFilePath);
        if (!string.IsNullOrWhiteSpace(projectDir) && Directory.Exists(projectDir))
        {
            saveDialog.InitialDirectory = projectDir;
        }

        string layoutName = layoutComboBox.SelectedIndex == 0 ? "side-by-side" : "stacked";
        int trimIn = GetGlobalTrimIn();
        int trimOut = GetGlobalTrimOut();
        saveDialog.FileName = $"{Path.GetFileNameWithoutExtension(_projectFilePath)}_combined_{layoutName}_{trimIn + 1}-{trimOut + 1}.mp4";

        if (saveDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(saveDialog.FileName))
        {
            return;
        }

        BeginExportOperation(1, "Preparing combined export...", out int restoreGlobalFrame, out TrimExportProgressForm progressForm);
        try
        {
            bool isSideBySide = layoutComboBox.SelectedIndex == 0;
            if (TryExportCombinedVideoBuiltIn(
                trimIn,
                trimOut,
                saveDialog.FileName,
                isSideBySide,
                ratio =>
                {
                    progressForm.UpdateProgress(0, ratio, "Saving combined video...");
                    Application.DoEvents();
                },
                out string message))
            {
                progressForm.UpdateProgress(0, 1d, "Combined video saved.");
                Application.DoEvents();
                RecordDiagnostics(exportStatus: $"Combined export OK ({Path.GetFileName(saveDialog.FileName)})", error: "None");
                MessageBox.Show(this, $"Saved combined video:\n{Path.GetFileName(saveDialog.FileName)}", "Combined Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                RecordDiagnostics(exportStatus: "Combined export failed", error: message);
                MessageBox.Show(this, message, "Combined Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            EndExportOperation(restoreGlobalFrame, progressForm);
        }
    }

    private bool TryExportCombinedVideoBuiltIn(
        int trimInGlobal,
        int trimOutGlobal,
        string outputPath,
        bool sideBySide,
        Action<double>? onProgress,
        out string message)
    {
        int leftMaxWidth = _leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource ? Math.Max(1, _leftTrack.FrameSize.Width) : 0;
        int rightMaxWidth = _rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource ? Math.Max(1, _rightTrack.FrameSize.Width) : 0;
        int leftMaxHeight = _leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource ? Math.Max(1, _leftTrack.FrameSize.Height) : 0;
        int rightMaxHeight = _rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource ? Math.Max(1, _rightTrack.FrameSize.Height) : 0;
        int maxWidth = Math.Max(leftMaxWidth, rightMaxWidth);
        int maxHeight = Math.Max(leftMaxHeight, rightMaxHeight);
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            message = "No video frames are available to combine in the selected range.";
            return false;
        }

        const int gap = 1;
        int outputWidth = sideBySide ? (maxWidth * 2) + gap : maxWidth;
        int outputHeight = sideBySide ? maxHeight : (maxHeight * 2) + gap;
        var outputSize = new OpenCvSharp.Size(outputWidth, outputHeight);
        int leftX = 0;
        int leftY = 0;
        int rightX = sideBySide ? maxWidth + gap : 0;
        int rightY = sideBySide ? 0 : maxHeight + gap;
        var leftCell = new OpenCvSharp.Rect(leftX, leftY, maxWidth, maxHeight);
        var rightCell = new OpenCvSharp.Rect(rightX, rightY, maxWidth, maxHeight);

        double fps = 30d;
        if (_leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource && _leftTrack.Fps > 0.001d)
        {
            fps = _leftTrack.Fps;
        }
        if (_rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource && _rightTrack.Fps > fps)
        {
            fps = _rightTrack.Fps;
        }

        int totalFrames = Math.Max(1, trimOutGlobal - trimInGlobal + 1);
        int fourCc = VideoWriter.FourCC('m', 'p', '4', 'v');
        try
        {
            using var writer = new VideoWriter(outputPath, fourCc, fps, outputSize, true);
            if (!writer.IsOpened())
            {
                message = "Could not create the output video file.";
                return false;
            }

            for (int globalFrame = trimInGlobal; globalFrame <= trimOutGlobal; globalFrame++)
            {
                using var canvas = new Mat(outputHeight, outputWidth, MatType.CV_8UC3, Scalar.Black);
                DrawTrackIntoCombinedCell(canvas, _leftTrack, globalFrame, leftCell);
                DrawTrackIntoCombinedCell(canvas, _rightTrack, globalFrame, rightCell);
                writer.Write(canvas);

                int done = (globalFrame - trimInGlobal) + 1;
                onProgress?.Invoke(Math.Clamp(done / (double)totalFrames, 0d, 1d));
            }
        }
        catch (Exception ex)
        {
            message = $"Combined export failed ({ex.Message}).";
            return false;
        }

        message = "Combined video saved.";
        return true;
    }

    private static void DrawTrackIntoCombinedCell(Mat canvas, VideoTrack track, int globalFrame, OpenCvSharp.Rect cellRect)
    {
        if (!track.IsLoaded || track.IsTemporaryWindowSource)
        {
            return;
        }

        int localFrame = globalFrame - track.TimelineStartFrame;
        if (localFrame < 0 || localFrame > track.LastFrameIndex)
        {
            return;
        }

        using Mat? frame = track.ReadFrame(localFrame);
        if (frame is null || frame.Empty())
        {
            return;
        }

        int targetWidth = cellRect.Width;
        int targetHeight = cellRect.Height;
        double scale = Math.Min(targetWidth / (double)frame.Width, targetHeight / (double)frame.Height);
        int drawWidth = Math.Max(1, (int)Math.Round(frame.Width * scale));
        int drawHeight = Math.Max(1, (int)Math.Round(frame.Height * scale));
        int drawX = cellRect.X + ((targetWidth - drawWidth) / 2);
        int drawY = cellRect.Y + ((targetHeight - drawHeight) / 2);
        using var resized = new Mat();
        Cv2.Resize(frame, resized, new OpenCvSharp.Size(drawWidth, drawHeight), 0, 0, InterpolationFlags.Area);
        using Mat roi = new Mat(canvas, new OpenCvSharp.Rect(drawX, drawY, drawWidth, drawHeight));
        resized.CopyTo(roi);
    }

    private bool HasLoadedVideoTrack() => _leftTrack.IsLoaded || _rightTrack.IsLoaded;

    private bool CanExportTrimRange()
    {
        if (_isSavingTrimmedVideos || !HasLoadedVideoTrack() || _globalTrimInFrame is null || _globalTrimOutFrame is null)
        {
            return false;
        }

        int trimIn = GetGlobalTrimIn();
        int trimOut = GetGlobalTrimOut();
        return new[] { _leftTrack, _rightTrack }.Any(track => HasTrackTrimOverlap(track, trimIn, trimOut));
    }

    private bool CanExportCombinedRange()
    {
        if (_isSavingTrimmedVideos || _globalTrimInFrame is null || _globalTrimOutFrame is null)
        {
            return false;
        }

        int trimIn = GetGlobalTrimIn();
        int trimOut = GetGlobalTrimOut();
        return HasTrackTrimOverlap(_leftTrack, trimIn, trimOut) || HasTrackTrimOverlap(_rightTrack, trimIn, trimOut);
    }

    private void UpdateTrimActionButtonsState()
    {
        bool canExport = CanExportTrimRange();
        bool canExportCombined = CanExportCombinedRange();
        if (_saveTrimmedVideosTopBarButton is not null)
        {
            _saveTrimmedVideosTopBarButton.Enabled = canExport;
        }
        if (_saveCombinedVideoTopBarButton is not null)
        {
            _saveCombinedVideoTopBarButton.Enabled = canExportCombined;
        }
    }

    private bool HasTrackTrimOverlap(VideoTrack track, int globalTrimIn, int globalTrimOut)
    {
        return TryGetTrackTrimRange(track, globalTrimIn, globalTrimOut, out _, out _, out _);
    }

    private bool TryGetTrackTrimRange(VideoTrack track, int globalTrimIn, int globalTrimOut, out int localStart, out int localEnd, out string reason)
    {
        localStart = 0;
        localEnd = 0;
        if (!track.IsLoaded)
        {
            reason = $"{track.Name}: not loaded.";
            return false;
        }
        if (track.IsTemporaryWindowSource)
        {
            reason = $"{track.Name}: app-window source can't be saved as trimmed video.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
        {
            reason = $"{track.Name}: source file missing.";
            return false;
        }

        localStart = Math.Max(0, globalTrimIn - track.TimelineStartFrame);
        localEnd = Math.Min(track.LastFrameIndex, globalTrimOut - track.TimelineStartFrame);
        if (localStart > localEnd)
        {
            reason = $"{track.Name}: selected trim range does not overlap this clip.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryInstallFfmpegForUser(out string message)
    {
        message = "FFmpeg installation did not complete.";
        RecordDiagnostics(installStatus: "Running...", error: "None");
        using var installForm = new FfmpegInstallProgressForm();
        installForm.Show(this);
        installForm.UpdateStatus("Starting FFmpeg installation...");
        Application.DoEvents();
        try
        {
            (string Label, string Args)[] installSteps =
            [
                ("Gyan.FFmpeg", "install --id Gyan.FFmpeg --exact --silent --accept-package-agreements --accept-source-agreements"),
                ("BtbN.FFmpeg", "install --id BtbN.FFmpeg --exact --silent --accept-package-agreements --accept-source-agreements"),
                ("ffmpeg (generic)", "install ffmpeg --silent --accept-package-agreements --accept-source-agreements")
            ];
            var details = new List<string>();

            for (int index = 0; index < installSteps.Length; index++)
            {
                (string label, string args) = installSteps[index];
                installForm.UpdateStatus($"Installing FFmpeg ({index + 1}/{installSteps.Length}): {label}...");
                Application.DoEvents();

                if (TryRunProcess("winget", args, 180000, out ProcessRunResult run))
                {
                    if (run.ExitCode == 0 && !run.TimedOut)
                    {
                        message = "FFmpeg installed successfully.";
                        RecordDiagnostics(installStatus: "Success");
                        installForm.UpdateStatus("FFmpeg installed successfully.");
                        Application.DoEvents();
                        return true;
                    }

                    string stderrTail = GetLastNonEmptyLine(run.StdErr);
                    string stdoutTail = GetLastNonEmptyLine(run.StdOut);
                    if (run.TimedOut)
                    {
                        details.Add($"{label}: timed out.");
                    }
                    else
                    {
                        details.Add($"{label}: exit code {run.ExitCode}.");
                    }

                    if (!string.IsNullOrWhiteSpace(stderrTail))
                    {
                        details.Add($"  {stderrTail}");
                    }
                    else if (!string.IsNullOrWhiteSpace(stdoutTail))
                    {
                        details.Add($"  {stdoutTail}");
                    }
                }
                else
                {
                    string errorSummary = string.IsNullOrWhiteSpace(run.ErrorMessage)
                        ? "could not start winget."
                        : run.ErrorMessage;
                    details.Add($"{label}: {errorSummary}");
                }
            }

            string detailText = details.Count > 0
                ? "\n\nDetails:\n" + string.Join("\n", details)
                : string.Empty;
            message = "Automatic install failed. You can continue with Accurate mode, or install ffmpeg manually later." + detailText;
            RecordDiagnostics(installStatus: "Failed", error: details.Count > 0 ? details[0] : "Install failed");
            installForm.UpdateStatus("FFmpeg installation failed.");
            Application.DoEvents();
            return false;
        }
        finally
        {
            installForm.Close();
        }
    }

    private static bool TryRunProcess(string fileName, string arguments, int timeoutMs, out ProcessRunResult result)
    {
        result = new ProcessRunResult(-1, false, string.Empty, string.Empty, string.Empty);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!process.Start())
            {
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
                result = new ProcessRunResult(-1, true, stdout, stderr, string.Empty);
                return false;
            }

            result = new ProcessRunResult(process.ExitCode, false, stdout, stderr, string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            result = new ProcessRunResult(-1, false, string.Empty, string.Empty, ex.Message);
            return false;
        }
    }

    private static string GetLastNonEmptyLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? string.Empty : lines[^1];
    }

    private bool TryResolveFfmpegExecutable(out string? executablePath)
    {
        executablePath = null;
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"),
            "ffmpeg"
        ];

        foreach (string candidate in candidates)
        {
            if (TryProbeFfmpeg(candidate))
            {
                executablePath = candidate;
                RecordDiagnostics(ffmpegStatus: $"Available ({candidate})");
                return true;
            }
        }

        RecordDiagnostics(ffmpegStatus: "Missing");
        return false;
    }

    private static bool TryProbeFfmpeg(string fileName)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("ffmpeg process could not be started.");
            }

            process.WaitForExit(3000);
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool TryExportTrimmedTrackWithFfmpeg(
        string ffmpegExecutable,
        TrimExportPlan plan,
        string outputDirectory,
        Action<double>? onProgress,
        out string? outputPath,
        out string message)
    {
        outputPath = null;
        VideoTrack track = plan.Track;
        string sourcePath = track.FilePath;
        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        string modeSuffix = "accurate";
        string fileName = $"{sourceName}_trim_{plan.LocalStart + 1}-{plan.LocalEnd + 1}_{modeSuffix}.mp4";
        string candidatePath = Path.Combine(outputDirectory, fileName);
        int suffix = 1;
        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(outputDirectory, $"{sourceName}_trim_{plan.LocalStart + 1}-{plan.LocalEnd + 1}_{modeSuffix}_{suffix}.mp4");
            suffix++;
        }

        double fps = track.Fps > 0.001 ? track.Fps : 30d;
        double startSeconds = plan.LocalStart / fps;
        double endSeconds = (plan.LocalEnd + 1) / fps;
        double durationSeconds = Math.Max(0.001d, endSeconds - startSeconds);

        try
        {
            using var process = new Process();
            process.StartInfo = BuildFfmpegStartInfo(ffmpegExecutable, sourcePath, candidatePath, startSeconds, endSeconds);
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }

                if (e.Data.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = e.Data["out_time_ms=".Length..].Trim();
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long outTimeMicros))
                    {
                        double ratio = Math.Clamp(outTimeMicros / (durationSeconds * 1_000_000d), 0d, 1d);
                        onProgress?.Invoke(ratio);
                    }
                }
                else if (e.Data.StartsWith("progress=end", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress?.Invoke(1d);
                }
            };

            string errorOutput = string.Empty;
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    errorOutput = e.Data;
                }
            };

            if (!process.Start())
            {
                message = $"{track.Name}: could not start ffmpeg.";
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            onProgress?.Invoke(1d);

            if (process.ExitCode != 0)
            {
                if (File.Exists(candidatePath))
                {
                    try
                    {
                        File.Delete(candidatePath);
                    }
                    catch
                    {
                    }
                }

                message = string.IsNullOrWhiteSpace(errorOutput)
                    ? $"{track.Name} ({modeSuffix}): ffmpeg failed."
                    : $"{track.Name} ({modeSuffix}): {errorOutput}";
                return false;
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(candidatePath))
            {
                try
                {
                    File.Delete(candidatePath);
                }
                catch
                {
                }
            }

            message = $"{track.Name} ({modeSuffix}): export failed ({ex.Message}).";
            return false;
        }

        outputPath = candidatePath;
        message = $"{track.Name} ({modeSuffix}): saved.";
        return true;
    }

    private bool TryExportTrimmedTrackBuiltIn(
        TrimExportPlan plan,
        string outputDirectory,
        Action<double>? onProgress,
        out string? outputPath,
        out string message)
    {
        outputPath = null;
        VideoTrack track = plan.Track;
        string sourceName = Path.GetFileNameWithoutExtension(track.FilePath);
        string fileName = $"{sourceName}_trim_{plan.LocalStart + 1}-{plan.LocalEnd + 1}_accurate.mp4";
        string candidatePath = Path.Combine(outputDirectory, fileName);
        int suffix = 1;
        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(outputDirectory, $"{sourceName}_trim_{plan.LocalStart + 1}-{plan.LocalEnd + 1}_accurate_{suffix}.mp4");
            suffix++;
        }

        double fps = track.Fps > 0.001 ? track.Fps : 30d;
        var frameSize = new OpenCvSharp.Size(Math.Max(1, track.FrameSize.Width), Math.Max(1, track.FrameSize.Height));
        int fourCc = VideoWriter.FourCC('m', 'p', '4', 'v');
        try
        {
            using var writer = new VideoWriter(candidatePath, fourCc, fps, frameSize, true);
            if (!writer.IsOpened())
            {
                message = $"{track.Name} (accurate): could not open output writer.";
                return false;
            }

            int totalFrames = Math.Max(1, plan.LocalEnd - plan.LocalStart + 1);
            for (int frameIndex = plan.LocalStart; frameIndex <= plan.LocalEnd; frameIndex++)
            {
                using Mat? frame = track.ReadFrame(frameIndex);
                if (frame is null || frame.Empty())
                {
                    message = $"{track.Name} (accurate): failed while reading frame {frameIndex + 1:n0}.";
                    writer.Release();
                    if (File.Exists(candidatePath))
                    {
                        File.Delete(candidatePath);
                    }
                    return false;
                }

                writer.Write(frame);
                int done = (frameIndex - plan.LocalStart) + 1;
                onProgress?.Invoke(Math.Clamp(done / (double)totalFrames, 0d, 1d));
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(candidatePath))
            {
                try
                {
                    File.Delete(candidatePath);
                }
                catch
                {
                }
            }

            message = $"{track.Name} (accurate): export failed ({ex.Message}).";
            return false;
        }

        outputPath = candidatePath;
        message = $"{track.Name} (accurate): saved.";
        return true;
    }

    private static ProcessStartInfo BuildFfmpegStartInfo(
        string ffmpegExecutable,
        string sourcePath,
        string outputPath,
        double startSeconds,
        double endSeconds)
    {
        string start = startSeconds.ToString("0.000000", CultureInfo.InvariantCulture);
        string end = endSeconds.ToString("0.000000", CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-progress");
        psi.ArgumentList.Add("pipe:1");
        psi.ArgumentList.Add("-nostats");

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(sourcePath);
        psi.ArgumentList.Add("-ss");
        psi.ArgumentList.Add(start);
        psi.ArgumentList.Add("-to");
        psi.ArgumentList.Add(end);
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("medium");
        psi.ArgumentList.Add("-crf");
        psi.ArgumentList.Add("17");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("192k");
        psi.ArgumentList.Add(outputPath);
        return psi;
    }

    private sealed record TrimExportPlan(VideoTrack Track, int LocalStart, int LocalEnd);
    private sealed record ProcessRunResult(int ExitCode, bool TimedOut, string StdOut, string StdErr, string ErrorMessage);

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

    private void SharedCommentsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSharedCommentsList)
        {
            return;
        }

        if (_sharedCommentsListBox?.SelectedItem is SharedComment comment)
        {
            SelectSharedComment(comment.Id, moveTimelineToComment: true);
        }
    }

    private void SharedCommentsListBox_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || _sharedCommentsListBox is null || _sharedCommentsListBox.Items[e.Index] is not SharedComment comment)
        {
            e.ItemHeight = 24;
            return;
        }

        int contentWidth = Math.Max(80, _sharedCommentsListBox.ClientSize.Width - 10 - CommentDeleteGlyphSize - (CommentDeleteGlyphPadding * 2));
        string text = $"{FrameToSharedTimestamp(comment.FrameIndex)}  {comment.Text}";
        DrawingSize size = TextRenderer.MeasureText(
            text,
            _sharedCommentsListBox.Font,
            new DrawingSize(contentWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);
        e.ItemHeight = Math.Max(24, size.Height + 8);
    }

    private void SharedCommentsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _sharedCommentsListBox is null || _sharedCommentsListBox.Items[e.Index] is not SharedComment comment)
        {
            return;
        }

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color backColor = selected ? Color.FromArgb(42, 72, 104) : _sharedCommentsListBox.BackColor;
        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        string text = $"{FrameToSharedTimestamp(comment.FrameIndex)}  {comment.Text}";
        Rectangle deleteBounds = GetCommentDeleteBounds(e.Bounds);
        Rectangle textBounds = new Rectangle(
            e.Bounds.X + 4,
            e.Bounds.Y + 4,
            Math.Max(10, deleteBounds.Left - e.Bounds.X - 8),
            Math.Max(10, e.Bounds.Height - 8));
        Color textColor = selected ? Color.FromArgb(235, 245, 255) : _sharedCommentsListBox.ForeColor;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _sharedCommentsListBox.Font,
            textBounds,
            textColor,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);

        bool deleteHover = _hoverDeleteCommentId == comment.Id;
        Color deleteColor = deleteHover
            ? Color.FromArgb(255, 132, 132)
            : selected ? Color.FromArgb(245, 205, 205) : Color.FromArgb(198, 150, 150);
        TextRenderer.DrawText(
            e.Graphics,
            "×",
            _sharedCommentsListBox.Font,
            deleteBounds,
            deleteColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        e.DrawFocusRectangle();
    }

    private void SharedCommentsListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentsListBox is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        int index = _sharedCommentsListBox.IndexFromPoint(e.Location);
        if (index < 0 || index >= _sharedCommentsListBox.Items.Count || _sharedCommentsListBox.Items[index] is not SharedComment comment)
        {
            return;
        }

        Rectangle itemBounds = _sharedCommentsListBox.GetItemRectangle(index);
        Rectangle deleteBounds = GetCommentDeleteBounds(itemBounds);
        if (!deleteBounds.Contains(e.Location))
        {
            return;
        }

        RemoveSharedComment(comment.Id);
    }

    private void SharedCommentsListBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentsListBox is null)
        {
            return;
        }

        int? nextHoverId = null;
        int index = _sharedCommentsListBox.IndexFromPoint(e.Location);
        if (index >= 0 && index < _sharedCommentsListBox.Items.Count && _sharedCommentsListBox.Items[index] is SharedComment comment)
        {
            Rectangle itemBounds = _sharedCommentsListBox.GetItemRectangle(index);
            if (GetCommentDeleteBounds(itemBounds).Contains(e.Location))
            {
                nextHoverId = comment.Id;
            }
        }

        if (_hoverDeleteCommentId != nextHoverId)
        {
            _hoverDeleteCommentId = nextHoverId;
            _sharedCommentsListBox.Invalidate();
            _sharedCommentsListBox.Cursor = nextHoverId.HasValue ? Cursors.Hand : Cursors.Default;
        }
    }

    private void SharedCommentsListBox_MouseLeave(object? sender, EventArgs e)
    {
        if (_sharedCommentsListBox is null || !_hoverDeleteCommentId.HasValue)
        {
            return;
        }

        _hoverDeleteCommentId = null;
        _sharedCommentsListBox.Cursor = Cursors.Default;
        _sharedCommentsListBox.Invalidate();
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

    private void InitializeDiagnosticsUi()
    {
        _diagnosticsTopBarButton = CreateLayoutButton(
            location: new DrawingPoint(0, 12),
            onClick: ToggleDiagnosticsPanel);
        _diagnosticsIcon = CreateDiagnosticsIcon(Color.FromArgb(238, 238, 238));
        _diagnosticsTopBarButton.Image = _diagnosticsIcon;
        _diagnosticsTopBarButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topBarPanel.Controls.Add(_diagnosticsTopBarButton);
        _diagnosticsTopBarButton.BringToFront();

        _diagnosticsLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 8.5f, FontStyle.Regular),
            Padding = new Padding(8),
            BackColor = Color.FromArgb(28, 28, 28)
        };
        _copyDiagnosticsButton = new Button
        {
            Text = "Copy Diagnostics",
            Dock = DockStyle.Bottom,
            Height = 26,
            FlatStyle = FlatStyle.Standard,
            UseVisualStyleBackColor = true
        };
        _copyDiagnosticsButton.Click += (_, _) => CopyDiagnosticsToClipboard();
        _diagnosticsPanel = new Panel
        {
            Visible = false,
            BackColor = Color.FromArgb(28, 28, 28),
            BorderStyle = BorderStyle.FixedSingle,
            Size = new DrawingSize(420, 124),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _diagnosticsPanel.Controls.Add(_copyDiagnosticsButton);
        _diagnosticsPanel.Controls.Add(_diagnosticsLabel);
        Controls.Add(_diagnosticsPanel);
        _diagnosticsPanel.BringToFront();
        UpdateDiagnosticsPanelText();
        LayoutDiagnosticsPanel();
        Resize += (_, _) => LayoutDiagnosticsPanel();
        LayoutTopBarButtons();
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
            UpdateDiagnosticsPanelText();
            _diagnosticsPanel.BringToFront();
        }
    }

    private void LayoutDiagnosticsPanel()
    {
        if (_diagnosticsPanel is null)
        {
            return;
        }

        int margin = 10;
        int x = Math.Max(0, ClientSize.Width - _diagnosticsPanel.Width - margin);
        int y = topBarPanel.Bottom + margin;
        _diagnosticsPanel.Location = new DrawingPoint(x, y);
    }

    private void UpdateDiagnosticsPanelText()
    {
        if (_diagnosticsLabel is null)
        {
            return;
        }

        _diagnosticsLabel.Text =
            $"FFmpeg: {_diagnosticsFfmpegStatus}\r\n" +
            $"Install: {_diagnosticsInstallStatus}\r\n" +
            $"Last export: {_diagnosticsLastExportStatus}\r\n" +
            $"Last error: {_diagnosticsLastError}" +
            (string.IsNullOrWhiteSpace(_diagnosticsCopyStatus) ? string.Empty : $"\r\n{_diagnosticsCopyStatus}");
    }

    private void RecordDiagnostics(string? ffmpegStatus = null, string? installStatus = null, string? exportStatus = null, string? error = null)
    {
        if (ffmpegStatus is not null)
        {
            _diagnosticsFfmpegStatus = ffmpegStatus;
        }
        if (installStatus is not null)
        {
            _diagnosticsInstallStatus = installStatus;
        }
        if (exportStatus is not null)
        {
            _diagnosticsLastExportStatus = exportStatus;
        }
        if (error is not null)
        {
            _diagnosticsLastError = error;
        }
        _diagnosticsCopyStatus = string.Empty;

        UpdateDiagnosticsPanelText();
    }

    private void CopyDiagnosticsToClipboard()
    {
        string payload =
            $"FFmpeg: {_diagnosticsFfmpegStatus}\r\n" +
            $"Install: {_diagnosticsInstallStatus}\r\n" +
            $"Last export: {_diagnosticsLastExportStatus}\r\n" +
            $"Last error: {_diagnosticsLastError}";
        try
        {
            Clipboard.SetText(payload);
            _diagnosticsCopyStatus = "Diagnostics copied.";
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
        using var brush = new SolidBrush(strokeColor);
        g.DrawRectangle(pen, 3, 3, 12, 12);
        g.DrawLine(pen, 6, 7, 12, 7);
        g.DrawLine(pen, 6, 10, 12, 10);
        g.FillEllipse(brush, 7, 13, 4, 3);
        return bmp;
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

    private bool AddSharedCommentAtCurrentTimeline()
    {
        if (!PromptForComment(out string commentText))
        {
            return false;
        }

        var comment = new SharedComment(_nextSharedCommentId++, masterTimeline.Value, commentText.Trim(), DateTime.Now);
        _sharedComments.Add(comment);
        _sharedComments.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        _isCommentsSidebarCollapsed = false;
        UpdateLayoutMode();
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
        if (_sharedCommentsListBox is null)
        {
            return;
        }

        _isUpdatingSharedCommentsList = true;
        _sharedCommentsListBox.BeginUpdate();
        try
        {
            _sharedCommentsListBox.Items.Clear();
            foreach (SharedComment comment in _sharedComments)
            {
                _sharedCommentsListBox.Items.Add(comment);
            }

            if (_selectedSharedCommentId is int selectedId)
            {
                for (int i = 0; i < _sharedCommentsListBox.Items.Count; i++)
                {
                    if (_sharedCommentsListBox.Items[i] is SharedComment comment && comment.Id == selectedId)
                    {
                        _sharedCommentsListBox.SelectedIndex = i;
                        _sharedCommentsListBox.TopIndex = Math.Max(0, i - 2);
                        break;
                    }
                }
            }
        }
        finally
        {
            _sharedCommentsListBox.EndUpdate();
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
        using var selectedMarkerBrush = new SolidBrush(Color.FromArgb(255, 154, 52));
        using var selectedMarkerOutlinePen = new Pen(Color.FromArgb(255, 226, 172), 1.5f);
        using var commentBrush = new SolidBrush(Color.FromArgb(115, 196, 255));
        using var selectedCommentBrush = new SolidBrush(Color.FromArgb(255, 126, 198));
        using var commentOutlinePen = new Pen(Color.FromArgb(42, 70, 92), 1f);
        using var playheadPen = new Pen(Color.FromArgb(74, 158, 255), 2f);
        using var playheadHandleBrush = new SolidBrush(Color.FromArgb(74, 158, 255));
        using var playheadHandleOutline = new Pen(Color.FromArgb(210, 235, 255), 1f);
        using var trimRangeBrush = new SolidBrush(Color.FromArgb(60, 190, 190, 190));
        using var trimInPen = new Pen(Color.FromArgb(150, 228, 162), 2f);
        using var trimOutPen = new Pen(Color.FromArgb(255, 164, 164), 2f);
        using var trimInTextBrush = new SolidBrush(Color.FromArgb(175, 238, 182));
        using var trimOutTextBrush = new SolidBrush(Color.FromArgb(255, 186, 186));
        using var font = new Font("Segoe UI", 8f, FontStyle.Regular);

        DrawTimeRuler(graphics, width, rulerPen, textBrush, font);

        if (_globalTrimInFrame is not null || _globalTrimOutFrame is not null)
        {
            int trimInX = Math.Clamp(FrameToTimelineX(GetGlobalTrimIn()), 0, width - 1);
            int trimOutX = Math.Clamp(FrameToTimelineX(GetGlobalTrimOut()), 0, width - 1);
            if (trimOutX < trimInX)
            {
                (trimInX, trimOutX) = (trimOutX, trimInX);
            }

            int overlayWidth = Math.Max(1, trimOutX - trimInX + 1);
            graphics.FillRectangle(trimRangeBrush, new Rectangle(trimInX, 0, overlayWidth, ClipTimelineCanvasHeight - 1));
            graphics.DrawLine(trimInPen, trimInX, 0, trimInX, ClipTimelineCanvasHeight - 1);
            graphics.DrawLine(trimOutPen, trimOutX, 0, trimOutX, ClipTimelineCanvasHeight - 1);
            graphics.DrawString("I", font, trimInTextBrush, Math.Min(width - 10, trimInX + 2), 2);
            graphics.DrawString("O", font, trimOutTextBrush, Math.Min(width - 10, trimOutX + 2), 2);
        }

        DrawTimelineLane(graphics, ClipTimelineHeaderHeight, laneBrush, laneOutlinePen);
        DrawTrackClip(graphics, _leftTrack, ClipTimelineHeaderHeight, clipBrush, clipOutlinePen, markerBrush, markerOutlinePen, selectedMarkerBrush, selectedMarkerOutlinePen, textBrush, font);
        int secondTrackY = ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
        DrawTimelineLane(graphics, secondTrackY, laneBrush, laneOutlinePen);
        DrawTrackClip(graphics, _rightTrack, secondTrackY, clipBrush, clipOutlinePen, markerBrush, markerOutlinePen, selectedMarkerBrush, selectedMarkerOutlinePen, textBrush, font);

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
        Brush selectedMarkerBrush,
        Pen selectedMarkerOutlinePen,
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

            bool isSelected = track.SelectedMarker == marker;
            bool isSnapPreview = _snapPreviewTrack == track && _snapPreviewMarker == marker;
            bool emphasize = isSelected || isSnapPreview;
            int halfWidth = emphasize ? 5 : 4;
            int topY = y + (emphasize ? 3 : 4);
            DrawingPoint[] markerShape =
            [
                new DrawingPoint(markerX, y + ClipTimelineTrackHeight - 2),
                new DrawingPoint(markerX - halfWidth, topY),
                new DrawingPoint(markerX + halfWidth, topY)
            ];
            Brush fillBrush = emphasize ? selectedMarkerBrush : markerBrush;
            Pen outlinePen = emphasize ? selectedMarkerOutlinePen : markerOutlinePen;
            graphics.FillPolygon(fillBrush, markerShape);
            graphics.DrawPolygon(outlinePen, markerShape);
        }
    }

    private void ClipTimelineCanvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_clipTimelineCanvas is null)
        {
            return;
        }

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
            SetGlobalTimelineFrame(TimelineXToFrame(e.X));
            return;
        }

        _draggingTimelineTrack = GetTimelineTrackAtPosition(e.Location);
        if (_draggingTimelineTrack is not null)
        {
            _dragStartMouseX = e.X;
            _dragStartTrackOffset = _draggingTimelineTrack.TimelineStartFrame;
            ClearTrackDragSnap();
            SetActiveTrack(_draggingTimelineTrack);
            _clipTimelineCanvas.Cursor = Cursors.SizeWE;
            _clipTimelineCanvas?.Invalidate();
            return;
        }

        // Avoid accidental playhead jumps when clicking inside timeline lanes.
        int leftLaneY = ClipTimelineHeaderHeight;
        int rightLaneY = ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
        bool clickedTrackLane =
            (e.Y >= leftLaneY && e.Y <= leftLaneY + ClipTimelineTrackHeight) ||
            (e.Y >= rightLaneY && e.Y <= rightLaneY + ClipTimelineTrackHeight);
        if (clickedTrackLane)
        {
            return;
        }

        SetGlobalTimelineFrame(TimelineXToFrame(e.X));
        _isDraggingPlayhead = true;
        _clipTimelineCanvas.Cursor = Cursors.SizeWE;
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
        int requestedStart = Math.Max(0, _dragStartTrackOffset + delta);
        int nextStart = GetSnappedTimelineStartForTrackDrag(_draggingTimelineTrack, requestedStart);
        if (_draggingTimelineTrack.TimelineStartFrame == nextStart)
        {
            return;
        }

        _draggingTimelineTrack.TimelineStartFrame = nextStart;
        EnsureClipTimelineCanvasWidth();
        _clipTimelineCanvas?.Invalidate();

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
        VideoTrack? bestTrack = null;
        int bestMarker = -1;
        int bestDistance = int.MaxValue;

        foreach (VideoTrack track in new[] { _leftTrack, _rightTrack })
        {
            if (!track.IsLoaded)
            {
                continue;
            }

            int laneY = track == _leftTrack
                ? ClipTimelineHeaderHeight
                : ClipTimelineHeaderHeight + ClipTimelineTrackHeight + ClipTimelineTrackGap;
            int markerCenterY = laneY + 8;
            int verticalDistance = Math.Abs(point.Y - markerCenterY);
            if (verticalDistance > MarkerSelectionThresholdPixels)
            {
                continue;
            }

            int clipX = FrameToTimelineX(track.TimelineStartFrame);
            int clipWidth = Math.Max(40, FramesToPixels(track.FrameCount));
            int clipRight = clipX + clipWidth;
            foreach (int marker in track.Markers)
            {
                int markerX = clipX + FramesToPixels(marker);
                if (markerX < clipX - MarkerSelectionThresholdPixels || markerX > clipRight + MarkerSelectionThresholdPixels)
                {
                    continue;
                }

                int horizontalDistance = Math.Abs(markerX - point.X);
                if (horizontalDistance > MarkerSelectionThresholdPixels)
                {
                    continue;
                }

                int weightedDistance = horizontalDistance + (verticalDistance * 2);
                if (weightedDistance < bestDistance)
                {
                    bestDistance = weightedDistance;
                    bestTrack = track;
                    bestMarker = marker;
                }
            }
        }

        if (bestTrack is null)
        {
            return false;
        }

        SetActiveTrack(bestTrack);
        bestTrack.SelectedMarker = bestMarker;
        _clipTimelineCanvas?.Invalidate();
        return true;
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
        int requestedGlobal = Math.Clamp(frameIndex, 0, Math.Max(0, masterTimeline.Maximum));
        int snappedGlobal = requestedGlobal;
        if (TryGetSnappedGlobalMarker(requestedGlobal, out VideoTrack? snappedTrack, out int snappedMarker, out int snappedGlobalMarkerFrame))
        {
            _snapPreviewTrack = snappedTrack;
            _snapPreviewMarker = snappedMarker;
            snappedGlobal = snappedGlobalMarkerFrame;
        }
        else
        {
            ClearSnapPreview();
        }

        UpdateMasterTimelineFromFrame(snappedGlobal);
        // Marker-first scrubbing: move playhead immediately and defer frame rendering
        // until mouse release for precise, low-latency positioning.
    }

    private void ApplyGlobalFrame(int frameIndex, bool isDrag)
    {
        if (isDrag)
        {
            SetGlobalTimelineFrameDuringDrag(frameIndex);
            return;
        }

        ClearSnapPreview();
        EnsureClipTimelineCanvasWidth();
        int safeGlobal = Math.Clamp(frameIndex, 0, Math.Max(0, masterTimeline.Maximum));
        UpdateMasterTimelineFromFrame(safeGlobal);
        RenderTrackForGlobalFrame(_leftTrack, safeGlobal);
        RenderTrackForGlobalFrame(_rightTrack, safeGlobal);
        _clipTimelineCanvas?.Invalidate();
    }

    private bool TryGetSnappedGlobalMarker(int requestedGlobalFrame, out VideoTrack? snappedTrack, out int snappedMarkerFrame, out int snappedGlobalFrame)
    {
        snappedTrack = null;
        snappedMarkerFrame = -1;
        snappedGlobalFrame = requestedGlobalFrame;

        int requestedX = FrameToTimelineX(requestedGlobalFrame);
        int bestDistance = int.MaxValue;

        foreach (VideoTrack track in new[] { _leftTrack, _rightTrack })
        {
            if (!track.IsLoaded || track.IsTemporaryWindowSource || track.Markers.Count == 0)
            {
                continue;
            }

            foreach (int marker in track.Markers)
            {
                int globalMarkerFrame = track.TimelineStartFrame + marker;
                int markerX = FrameToTimelineX(globalMarkerFrame);
                int distance = Math.Abs(markerX - requestedX);
                if (distance > GlobalPlayheadSnapThresholdPixels)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    snappedTrack = track;
                    snappedMarkerFrame = marker;
                    snappedGlobalFrame = globalMarkerFrame;
                }
            }
        }

        return snappedTrack is not null;
    }

    private void ClearSnapPreview()
    {
        _snapPreviewTrack = null;
        _snapPreviewMarker = null;
    }

    private int GetSnappedTimelineStartForTrackDrag(VideoTrack draggingTrack, int requestedStartFrame)
    {
        VideoTrack otherTrack = draggingTrack == _leftTrack ? _rightTrack : _leftTrack;
        if (!draggingTrack.IsLoaded || !otherTrack.IsLoaded || draggingTrack.Markers.Count == 0 || otherTrack.Markers.Count == 0)
        {
            ClearTrackDragSnap();
            return requestedStartFrame;
        }

        double pixelsPerFrame = Math.Max(0.0001d, _timelinePixelsPerFrame);
        int lockThresholdFrames = Math.Max(1, (int)Math.Round(TrackDragMarkerSnapThresholdPixels / pixelsPerFrame));
        int releaseThresholdFrames = Math.Max(lockThresholdFrames + 1, (int)Math.Round(TrackDragMarkerSnapReleaseThresholdPixels / pixelsPerFrame));

        if (_trackDragSnapOwnMarker is int lockedOwnMarker &&
            _trackDragSnapTargetGlobalMarkerFrame is int lockedTargetGlobalFrame)
        {
            int lockedStart = Math.Max(0, lockedTargetGlobalFrame - lockedOwnMarker);
            if (Math.Abs(requestedStartFrame - lockedStart) <= releaseThresholdFrames)
            {
                return lockedStart;
            }

            ClearTrackDragSnap();
        }

        int bestDistance = int.MaxValue;
        int bestOwnMarker = -1;
        int bestTargetGlobalFrame = -1;
        int bestSnappedStart = requestedStartFrame;
        foreach (int ownMarker in draggingTrack.Markers)
        {
            foreach (int otherMarker in otherTrack.Markers)
            {
                int targetGlobalFrame = otherTrack.TimelineStartFrame + otherMarker;
                int snappedStart = targetGlobalFrame - ownMarker;
                if (snappedStart < 0)
                {
                    continue;
                }

                int distance = Math.Abs(requestedStartFrame - snappedStart);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestOwnMarker = ownMarker;
                    bestTargetGlobalFrame = targetGlobalFrame;
                    bestSnappedStart = snappedStart;
                }
            }
        }

        if (bestDistance <= lockThresholdFrames)
        {
            _trackDragSnapOwnMarker = bestOwnMarker;
            _trackDragSnapTargetGlobalMarkerFrame = bestTargetGlobalFrame;
            return bestSnappedStart;
        }

        ClearTrackDragSnap();
        return requestedStartFrame;
    }

    private void ClearTrackDragSnap()
    {
        _trackDragSnapOwnMarker = null;
        _trackDragSnapTargetGlobalMarkerFrame = null;
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
            RenderTrack(track, localFrame, updateMasterTimeline: false, lightweight: true);
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

    private int GetGlobalTrimIn()
    {
        return Math.Clamp(_globalTrimInFrame ?? 0, 0, Math.Max(0, masterTimeline.Maximum));
    }

    private int GetGlobalTrimOut()
    {
        int trimOut = Math.Clamp(_globalTrimOutFrame ?? Math.Max(0, masterTimeline.Maximum), 0, Math.Max(0, masterTimeline.Maximum));
        int trimIn = GetGlobalTrimIn();
        return Math.Max(trimIn, trimOut);
    }

    private void ClearTrackPreview(VideoTrack track, bool lightweight = false)
    {
        if (lightweight && track.PictureBox.Image is null)
        {
            return;
        }

        if (track.PictureBox.Image is Bitmap previousBitmap)
        {
            track.PictureBox.Image = null;
            previousBitmap.Dispose();
        }

        track.ShowStillFrame();
        if (!lightweight)
        {
            ApplyScale(track);
            UpdatePlaybackStatus();
            UpdateAlignmentPreview();
        }
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

            _globalTrimInFrame = ResolveGlobalTrimFrame(session.GlobalTrimInFrame, session.LeftTrimInFrame, session.RightTrimInFrame);
            _globalTrimOutFrame = ResolveGlobalTrimFrame(session.GlobalTrimOutFrame, session.LeftTrimOutFrame, session.RightTrimOutFrame);
            if (_globalTrimInFrame is int gIn && _globalTrimOutFrame is int gOut && gOut < gIn)
            {
                _globalTrimOutFrame = gIn;
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

    private int? ResolveGlobalTrimFrame(int? globalFrame, int? leftTrackFrame, int? rightTrackFrame)
    {
        int maxGlobal = Math.Max(0, masterTimeline.Maximum);
        if (globalFrame is int explicitGlobal)
        {
            return Math.Clamp(explicitGlobal, 0, maxGlobal);
        }

        int? leftGlobal = null;
        if (leftTrackFrame is int leftLocal && _leftTrack.IsLoaded)
        {
            leftGlobal = Math.Clamp(leftLocal + _leftTrack.TimelineStartFrame, 0, maxGlobal);
        }

        int? rightGlobal = null;
        if (rightTrackFrame is int rightLocal && _rightTrack.IsLoaded)
        {
            rightGlobal = Math.Clamp(rightLocal + _rightTrack.TimelineStartFrame, 0, maxGlobal);
        }

        return leftGlobal ?? rightGlobal;
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
                GlobalTrimInFrame: _globalTrimInFrame,
                GlobalTrimOutFrame: _globalTrimOutFrame,
                LeftTrimInFrame: null,
                LeftTrimOutFrame: null,
                RightTrimInFrame: null,
                RightTrimOutFrame: null,
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
