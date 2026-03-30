using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using System.Text.Json;

namespace VideoFrameComparer;

public partial class Form1 : Form
{
    private const int LargeStep = 20;
    private const int MarkerSnapThresholdPixels = 18;
    private const int MarkerSelectionThresholdPixels = 12;
    private const int TimelineEdgePadding = 8;
    private const float WheelZoomStep = 1.1f;
    private const float MinZoomMultiplier = 1.0f;
    private const float MaxZoomMultiplier = 8.0f;
    private const int TopBarHeight = 64;
    private const int BottomBarHeight = 71;
    private const int PreviewTitleHeight = 28;
    private const int PreviewMarkerHeight = 24;
    private const int PreviewTimelineHeight = 52;
    private const int BetweenPreviewGap = 1;
    private static readonly string[] SupportedExtensions = [".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v"];

    private readonly LibVLC _libVlc;
    private readonly string _projectFilePath;
    private readonly VideoTrack _leftTrack;
    private readonly VideoTrack _rightTrack;
    private VideoTrack? _activeTrack;
    private readonly System.Windows.Forms.Timer _playbackTimer;
    private bool _isPlaying;
    private bool _isUpdatingMasterTimeline;
    private float _playbackSpeedMultiplier = 1.0f;
    private bool _isRestoringSession;

    public Form1(string projectFilePath)
    {
        _projectFilePath = projectFilePath;
        AppLog.Write("App starting.");
        Core.Initialize();
        AppLog.Write("LibVLCSharp core initialized.");
        _libVlc = new LibVLC("--no-video-title-show");
        AppLog.Write("LibVLC instance created.");

        InitializeComponent();

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

        speedComboBox.SelectedIndex = 2;
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
        _leftTrack.Dispose();
        _rightTrack.Dispose();
        _libVlc.Dispose();
        base.OnFormClosing(e);
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
        track.Timeline.Scroll += (_, _) => SeekToFrame(track, track.Timeline.Value);
        track.Timeline.MouseDown += (_, _) => SetActiveTrack(track);
        track.MarkerPanel.Paint += (_, e) => DrawMarkers(track, e.Graphics);
        track.MarkerPanel.Resize += (_, _) => track.MarkerPanel.Invalidate();
        track.MarkerPanel.MouseDown += (_, e) => HandleMarkerPanelMouseDown(track, e);
    }

    private static void ConfigureTrackLayout(VideoTrack track)
    {
        track.InfoLabel.Visible = false;
        track.Timeline.AutoSize = false;
        track.Timeline.Height = PreviewTimelineHeight;
        track.MarkerPanel.Height = PreviewMarkerHeight;
        track.FooterPanel.Height = PreviewTimelineHeight + PreviewMarkerHeight;
        track.FooterPanel.Visible = true;
        track.Timeline.Visible = true;
        track.MarkerPanel.Visible = true;
    }

    private void LoadVideoIntoTrack(VideoTrack track, string filePath)
    {
        AppLog.Write($"Loading {track.Name}: {filePath}");
        StopPlayback();
        track.Load(filePath, _libVlc);
        AppLog.Write($"Loaded {track.Name}: fps={track.Fps:0.###}, frames={track.FrameCount}, size={track.FrameSize.Width}x{track.FrameSize.Height}");
        RenderTrack(track, track.CurrentFrameIndex, updateMasterTimeline: false);
        track.MarkerPanel.Invalidate();
        SetActiveTrack(track);
        UpdateVideoFit();
        UpdateMasterTimelineBounds();
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
            string details = ex.InnerException is null ? ex.Message : $"{ex.Message}\n\n{ex.InnerException.Message}";
            MessageBox.Show(this, details, "Could not load dropped video", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        activeVideoLabel.Text = $"Active: {track.Name}";
        track.MarkerPanel.Invalidate();
        (_activeTrack == _leftTrack ? _rightTrack : _leftTrack).MarkerPanel.Invalidate();
        UpdatePlaybackStatus();
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
        using Mat? frame = track.ReadFrame(safeFrameIndex);
        if (frame is null || frame.Empty())
        {
            AppLog.Write($"RenderTrack failed for {track.Name} at frame {safeFrameIndex}.");
            return;
        }

        Bitmap nextBitmap = BitmapConverter.ToBitmap(frame);
        Bitmap? previousBitmap = track.PictureBox.Image as Bitmap;
        track.PictureBox.Image = nextBitmap;
        previousBitmap?.Dispose();

        track.CurrentFrameIndex = safeFrameIndex;
        if (track.Timeline.Value != safeFrameIndex)
        {
            track.Timeline.Value = safeFrameIndex;
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
    }

    private void layoutComboBox_SelectedIndexChanged(object sender, EventArgs e) => UpdateLayoutMode();

    private void speedComboBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        _playbackSpeedMultiplier = speedComboBox.SelectedIndex switch
        {
            0 => 0.25f,
            1 => 0.5f,
            _ => 1.0f
        };

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

        if (horizontal)
        {
            videosTableLayout.ColumnCount = 2;
            videosTableLayout.RowCount = 1;
            videosTableLayout.ColumnStyles.Clear();
            videosTableLayout.RowStyles.Clear();
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            videosTableLayout.SetCellPosition(leftHostPanel, new TableLayoutPanelCellPosition(0, 0));
            videosTableLayout.SetCellPosition(rightHostPanel, new TableLayoutPanelCellPosition(1, 0));
        }
        else
        {
            videosTableLayout.ColumnCount = 1;
            videosTableLayout.RowCount = 2;
            videosTableLayout.ColumnStyles.Clear();
            videosTableLayout.RowStyles.Clear();
            videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            videosTableLayout.SetCellPosition(leftHostPanel, new TableLayoutPanelCellPosition(0, 0));
            videosTableLayout.SetCellPosition(rightHostPanel, new TableLayoutPanelCellPosition(0, 1));
        }

        videosTableLayout.ResumeLayout();
        UpdateVideoFit();
        UpdateWindowToContent();
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

        if (track.FrameSize.Width <= 0 || track.FrameSize.Height <= 0)
        {
            track.PictureBox.Size = new DrawingSize(320, 180);
            track.PictureBox.Location = new DrawingPoint(12, 12);
            return;
        }

        int availableWidth = Math.Max(1, track.ImagePanel.ClientSize.Width - track.ImagePanel.Padding.Horizontal);
        int availableHeight = Math.Max(1, track.ImagePanel.ClientSize.Height - track.ImagePanel.Padding.Vertical);
        float fitScale = Math.Min(
            availableWidth / (float)track.FrameSize.Width,
            availableHeight / (float)track.FrameSize.Height);
        float appliedScale = fitScale * track.ZoomMultiplier;

        track.PictureBox.Size = new DrawingSize(
            Math.Max(1, (int)Math.Round(track.FrameSize.Width * appliedScale)),
            Math.Max(1, (int)Math.Round(track.FrameSize.Height * appliedScale)));
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
        int maxFrame = Math.Max(_leftTrack.LastFrameIndex, _rightTrack.LastFrameIndex);
        _isUpdatingMasterTimeline = true;
        masterTimeline.Minimum = 0;
        masterTimeline.Maximum = Math.Max(maxFrame, 0);
        masterTimeline.Enabled = _leftTrack.IsLoaded || _rightTrack.IsLoaded;
        masterTimeline.LargeChange = Math.Min(LargeStep, Math.Max(1, Math.Max(maxFrame, 1) / 50));
        masterTimeline.SmallChange = 1;
        UpdateMasterTimelineFromTrack(_activeTrack ?? (_leftTrack.IsLoaded ? _leftTrack : _rightTrack));
        _isUpdatingMasterTimeline = false;
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
            UpdateMasterTimelineFromFrame(renderedTrack.CurrentFrameIndex);
        }
    }

    private void UpdateMasterTimelineFromTrack(VideoTrack? track)
    {
        if (track is null || !track.IsLoaded)
        {
            UpdateMasterTimelineFromFrame(0);
            return;
        }

        UpdateMasterTimelineFromFrame(track.CurrentFrameIndex);
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
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
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
        if (_activeTrack is null)
        {
            return false;
        }

        StopPlayback();
        int nextFrameIndex = _activeTrack.CurrentFrameIndex + delta;
        RenderTrack(_activeTrack, nextFrameIndex, updateMasterTimeline: false);
        return true;
    }

    private bool AddMarkerToActiveTrack()
    {
        if (_activeTrack is null || !_activeTrack.IsLoaded)
        {
            return false;
        }

        if (_activeTrack.Markers.Add(_activeTrack.CurrentFrameIndex))
        {
            _activeTrack.SelectedMarker = _activeTrack.CurrentFrameIndex;
            AppLog.Write($"Marker added for {_activeTrack.Name} at frame {_activeTrack.CurrentFrameIndex}.");
            _activeTrack.MarkerPanel.Invalidate();
            SaveSession();
        }
        else
        {
            _activeTrack.SelectedMarker = _activeTrack.CurrentFrameIndex;
            _activeTrack.MarkerPanel.Invalidate();
        }

        return true;
    }

    private bool DeleteSelectedMarkerOnActiveTrack()
    {
        if (_activeTrack is null || !_activeTrack.IsLoaded || _activeTrack.SelectedMarker is null)
        {
            return false;
        }

        int markerToDelete = _activeTrack.SelectedMarker.Value;
        if (_activeTrack.Markers.Remove(markerToDelete))
        {
            AppLog.Write($"Marker deleted for {_activeTrack.Name} at frame {markerToDelete}.");
            _activeTrack.SelectedMarker = null;
            _activeTrack.MarkerPanel.Invalidate();
            SaveSession();
            return true;
        }

        return false;
    }

    private bool StepBothTracks(int delta)
    {
        bool stepped = false;
        StopPlayback();

        if (_leftTrack.IsLoaded)
        {
            RenderTrack(_leftTrack, _leftTrack.CurrentFrameIndex + delta, updateMasterTimeline: false);
            stepped = true;
        }

        if (_rightTrack.IsLoaded)
        {
            RenderTrack(_rightTrack, _rightTrack.CurrentFrameIndex + delta, updateMasterTimeline: false);
            stepped = true;
        }

        return stepped;
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
        UpdateMasterTimelineFromFrame(masterTimeline.Value);
        if (_leftTrack.IsLoaded)
        {
            RenderTrack(_leftTrack, masterTimeline.Value, updateMasterTimeline: false);
        }

        if (_rightTrack.IsLoaded)
        {
            RenderTrack(_rightTrack, masterTimeline.Value, updateMasterTimeline: false);
        }
    }

    private void StartPlayback()
    {
        if (!_leftTrack.IsLoaded && !_rightTrack.IsLoaded)
        {
            AppLog.Write("StartPlayback ignored because no tracks are loaded.");
            return;
        }

        AppLog.Write($"StartPlayback requested at speed {_playbackSpeedMultiplier:0.##}x.");
        PrepareTrackForPlayback(_leftTrack);
        PrepareTrackForPlayback(_rightTrack);

        _playbackTimer.Start();
        _isPlaying = true;
        playPauseButton.Text = "Pause";
        UpdatePlaybackStatus();
    }

    private void PrepareTrackForPlayback(VideoTrack track)
    {
        if (!track.IsLoaded)
        {
            return;
        }

        AppLog.Write($"Preparing playback for {track.Name} at frame {track.CurrentFrameIndex}.");
        track.ShowPlayback();
        if (!track.PlayFromCurrentFrame())
        {
            AppLog.Write($"Playback failed to start for {track.Name}.");
            track.ShowStillFrame();
            return;
        }

        ApplyPlaybackRate(track);
        AppLog.Write($"Playback started for {track.Name}.");
        UpdateTrackInfo(track);
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
        playPauseButton.Text = "Play Both";
        UpdatePlaybackStatus();
    }

    private void SyncTrackFromPlayback(VideoTrack track)
    {
        if (!track.IsLoaded)
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
            UpdateMasterTimelineFromFrame(timelineTrack.GetCurrentPlaybackFrame());
        }

        if (!anyPlaying)
        {
            StopPlayback();
        }

        UpdatePlaybackStatus();
    }

    private void UpdateTrackPlaybackState(VideoTrack track, ref bool anyPlaying)
    {
        if (!track.IsLoaded || !track.IsPlaybackVisible)
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
            track.Timeline.Value = playbackFrame;
        }

        track.MarkerPanel.Invalidate();
        UpdateTrackInfo(track);
    }

    private void UpdateTrackInfo(VideoTrack track)
    {
        if (!track.IsLoaded)
        {
            return;
        }
    }

    private void UpdatePlaybackStatus()
    {
        string leftStatus = _leftTrack.IsLoaded ? $"A {FormatTrackFrame(_leftTrack)}" : "A not loaded";
        string rightStatus = _rightTrack.IsLoaded ? $"B {FormatTrackFrame(_rightTrack)}" : "B not loaded";
        string playback = _isPlaying ? "Playing" : "Paused";
        playbackStatusLabel.Text = $"{playback} {_playbackSpeedMultiplier:0.##}x | Shared timeline | {leftStatus} | {rightStatus}";
    }

    private void DrawMarkers(VideoTrack track, Graphics graphics)
    {
        graphics.Clear(track.MarkerPanel.BackColor);
        if (track.MarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var topBorderPen = new Pen(Color.FromArgb(96, 96, 96), 1f);
        using var baselinePen = new Pen(Color.FromArgb(110, 110, 110), 1f);
        using var markerLinePen = new Pen(Color.FromArgb(255, 208, 64), 2f);
        using var selectedMarkerLinePen = new Pen(Color.FromArgb(255, 145, 77), 3f);
        using var markerBrush = new SolidBrush(Color.FromArgb(255, 208, 64));
        using var markerOutlinePen = new Pen(Color.FromArgb(126, 96, 18), 1f);
        using var selectedBrush = new SolidBrush(Color.FromArgb(255, 145, 77));
        using var selectedOutlinePen = new Pen(Color.FromArgb(255, 214, 181), 1f);
        using var currentBrush = new SolidBrush(Color.FromArgb(74, 158, 255));
        int width = track.MarkerPanel.ClientSize.Width - 1;
        int height = track.MarkerPanel.ClientSize.Height - 1;
        int baselineY = height - 5;

        graphics.DrawLine(topBorderPen, 0, 0, width, 0);
        graphics.DrawLine(baselinePen, 0, baselineY, width, baselineY);

        if (!track.IsLoaded)
        {
            return;
        }

        foreach (int marker in track.Markers)
        {
            int x = MarkerFrameToX(track, marker, width);
            bool isSelected = track.SelectedMarker == marker;
            graphics.DrawLine(isSelected ? selectedMarkerLinePen : markerLinePen, x, 2, x, baselineY - 2);
            DrawingPoint[] markerShape =
            [
                new DrawingPoint(x, 3),
                new DrawingPoint(Math.Max(0, x - 7), baselineY - 1),
                new DrawingPoint(Math.Min(width, x + 7), baselineY - 1)
            ];
            graphics.FillPolygon(isSelected ? selectedBrush : markerBrush, markerShape);
            graphics.DrawPolygon(isSelected ? selectedOutlinePen : markerOutlinePen, markerShape);
        }

        int currentX = MarkerFrameToX(track, track.CurrentFrameIndex, width);
        graphics.FillRectangle(currentBrush, Math.Max(0, currentX - 1), 0, 3, height);
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
        if (track.Markers.Count == 0 || track.LastFrameIndex <= 0 || track.Timeline.ClientSize.Width <= 0)
        {
            return requestedFrameIndex;
        }

        int usableWidth = GetTimelineUsableWidth(track.Timeline.ClientSize.Width);
        double pixelsPerFrame = usableWidth / (double)Math.Max(1, track.LastFrameIndex);
        int thresholdFrames = Math.Max(1, (int)Math.Round(MarkerSnapThresholdPixels / pixelsPerFrame));

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

        return nearestDistance <= thresholdFrames ? nearestMarker : requestedFrameIndex;
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
        int maxVideoWidth = Math.Max(
            _leftTrack.IsLoaded ? _leftTrack.PictureBox.Width : 0,
            _rightTrack.IsLoaded ? _rightTrack.PictureBox.Width : 0);
        int maxVideoHeight = Math.Max(
            _leftTrack.IsLoaded ? _leftTrack.PictureBox.Height : 0,
            _rightTrack.IsLoaded ? _rightTrack.PictureBox.Height : 0);

        if (maxVideoWidth <= 0 || maxVideoHeight <= 0)
        {
            return;
        }

        bool horizontal = layoutComboBox.SelectedIndex == 0;
        int previewWidth = horizontal ? (maxVideoWidth * 2) + BetweenPreviewGap : maxVideoWidth;
        int previewHeight = horizontal ? maxVideoHeight : (maxVideoHeight * 2) + BetweenPreviewGap;
        previewHeight += PreviewTitleHeight + PreviewMarkerHeight + PreviewTimelineHeight;

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
            speedComboBox.SelectedIndex = session.SpeedIndex is >= 0 and <= 2 ? session.SpeedIndex.Value : 2;

            if (!string.IsNullOrWhiteSpace(session.LeftVideoPath) && File.Exists(session.LeftVideoPath))
            {
                LoadVideoIntoTrack(_leftTrack, session.LeftVideoPath);
                ApplyMarkers(_leftTrack, session.LeftMarkers);
            }

            if (!string.IsNullOrWhiteSpace(session.RightVideoPath) && File.Exists(session.RightVideoPath))
            {
                LoadVideoIntoTrack(_rightTrack, session.RightVideoPath);
                ApplyMarkers(_rightTrack, session.RightMarkers);
            }

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
        }
        finally
        {
            _isRestoringSession = false;
        }
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
                LeftMarkers: [.. _leftTrack.Markers],
                RightMarkers: [.. _rightTrack.Markers],
                LayoutIndex: layoutComboBox.SelectedIndex,
                SpeedIndex: speedComboBox.SelectedIndex,
                ActiveTrack: _activeTrack == _rightTrack ? "right" : "left");
            File.WriteAllText(_projectFilePath, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
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

    private sealed class VideoTrack : IDisposable
    {
        private VideoCapture? _capture;
        private Media? _media;

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

        public SortedSet<int> Markers { get; }

        public int? SelectedMarker { get; set; }

        public float ZoomMultiplier { get; set; }

        public int FrameCount { get; private set; }

        public int LastFrameIndex => Math.Max(FrameCount - 1, 0);

        public double Fps { get; private set; }

        public DrawingSize FrameSize { get; private set; }

        public int CurrentFrameIndex { get; set; }

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
            DisposeCapture();
            DisposePlayback();

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

        public bool PlayFromCurrentFrame()
        {
            if (PlaybackPlayer is null)
            {
                AppLog.Write($"PlayFromCurrentFrame aborted for {Name}: no playback player.");
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
            PlaybackPlayer.EncounteredError += (_, _) => AppLog.Write($"{Name}: VLC EncounteredError");
        }
    }

    private sealed record AppSession(
        string? ProjectName,
        string? LeftVideoPath,
        string? RightVideoPath,
        List<int>? LeftMarkers,
        List<int>? RightMarkers,
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
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
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
