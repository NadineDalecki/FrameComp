using DrawingPoint = System.Drawing.Point;

namespace VideoFrameComparer;

public partial class Form1
{
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
}
