using System.Text.Json;

namespace VideoFrameComparer;

public partial class Form1
{
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
}
