using OpenCvSharp;
using System.Diagnostics;
using System.Globalization;

namespace VideoFrameComparer;

public partial class Form1
{
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

    private bool EnsureFfmpegForExport(string exportName, bool allowSlowFallback, out string? ffmpegExecutable, out bool usingSlowFallback)
    {
        usingSlowFallback = false;
        if (TryResolveFfmpegExecutable(out ffmpegExecutable))
        {
            return true;
        }

        DialogResult installPrompt = MessageBox.Show(
            this,
            $"FFmpeg was not found.\n\n{exportName} is much faster and usually higher quality with FFmpeg.\n\nYes = install FFmpeg now\nNo = continue with built-in slow mode\nCancel = abort",
            "FFmpeg Not Found",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);
        if (installPrompt == DialogResult.Cancel)
        {
            return false;
        }

        if (installPrompt == DialogResult.Yes)
        {
            bool installOk = TryInstallFfmpegForUser(out string installMessage);
            if (installOk && TryResolveFfmpegExecutable(out ffmpegExecutable))
            {
                return true;
            }

            if (!allowSlowFallback)
            {
                MessageBox.Show(this, $"{installMessage}\n\nExport canceled.", "FFmpeg Not Available", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            usingSlowFallback = true;
            RecordDiagnostics(ffmpegStatus: "Missing (slow fallback)");
            MessageBox.Show(this, $"{installMessage}\n\nFalling back to built-in slow mode for this export.", "Slow Fallback Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ffmpegExecutable = null;
            return true;
        }

        if (!allowSlowFallback)
        {
            MessageBox.Show(this, "Export canceled because FFmpeg is required for this operation.", "Export Canceled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        usingSlowFallback = true;
        RecordDiagnostics(ffmpegStatus: "Missing (slow fallback)");
        MessageBox.Show(this, "Falling back to built-in slow mode because FFmpeg is not installed.", "Slow Fallback Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        ffmpegExecutable = null;
        return true;
    }

    private bool TrySelectFfmpegVideoEncoder(
        string ffmpegExecutable,
        int outputWidth,
        int outputHeight,
        out string encoderName,
        out string encoderDetail)
    {
        encoderName = FfmpegEncoderX264;
        encoderDetail = "x264 (CPU)";
        if (string.IsNullOrWhiteSpace(ffmpegExecutable))
        {
            return false;
        }

        if (_cachedFfmpegEncodersOutput is null)
        {
            if (!TryRunProcess(ffmpegExecutable, "-hide_banner -encoders", 15000, out ProcessRunResult result) || result.ExitCode != 0)
            {
                return true;
            }

            _cachedFfmpegEncodersOutput = (result.StdOut + "\n" + result.StdErr).ToLowerInvariant();
        }

        bool hasH264Nvenc = _cachedFfmpegEncodersOutput.Contains(FfmpegEncoderNvenc, StringComparison.Ordinal);
        bool hasHevcNvenc = _cachedFfmpegEncodersOutput.Contains(FfmpegEncoderHevcNvenc, StringComparison.Ordinal);

        // NVENC H.264 is commonly limited to 4096px width; combined exports can exceed that (e.g. 5120x1440).
        if (hasH264Nvenc && outputWidth <= NvencH264MaxWidth && outputHeight <= NvencH264MaxHeight)
        {
            encoderName = FfmpegEncoderNvenc;
            encoderDetail = "h264_nvenc (GPU)";
            _diagnosticsEncoderStatus = encoderDetail;
            return true;
        }

        // If H.264 NVENC is unavailable due to size, prefer HEVC NVENC if present (typically supports wider frames).
        if (hasHevcNvenc && outputWidth <= NvencHevcMaxWidth && outputHeight <= NvencHevcMaxHeight)
        {
            encoderName = FfmpegEncoderHevcNvenc;
            encoderDetail = "hevc_nvenc (GPU)";
            _diagnosticsEncoderStatus = encoderDetail;
            return true;
        }

        _diagnosticsEncoderStatus = encoderDetail;
        return true;
    }

    private string AddFfmpegVideoEncodingArguments(ProcessStartInfo psi, string ffmpegExecutable, int outputWidth, int outputHeight)
    {
        TrySelectFfmpegVideoEncoder(ffmpegExecutable, outputWidth, outputHeight, out string encoderName, out string encoderDetail);

        if (string.Equals(encoderName, FfmpegEncoderNvenc, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(encoderName, FfmpegEncoderHevcNvenc, StringComparison.OrdinalIgnoreCase))
        {
            const int qp = 18;

            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add(encoderName);
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("p4");
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("constqp");
            psi.ArgumentList.Add("-qp");
            psi.ArgumentList.Add(qp.ToString(CultureInfo.InvariantCulture));
            if (string.Equals(encoderName, FfmpegEncoderNvenc, StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("-profile:v");
                psi.ArgumentList.Add("high");
            }
            else
            {
                psi.ArgumentList.Add("-profile:v");
                psi.ArgumentList.Add("main");
                // Better MP4 compatibility for HEVC.
                psi.ArgumentList.Add("-tag:v");
                psi.ArgumentList.Add("hvc1");
            }
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            return encoderDetail;
        }

        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add(FfmpegEncoderX264);
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("faster");
        psi.ArgumentList.Add("-crf");
        psi.ArgumentList.Add("20");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        return encoderDetail;
    }

    private void SaveTrimmedVideosButton_Click(object? sender, EventArgs e)
    {
        if (_isSavingTrimmedVideos || !CanExportTrimRange())
        {
            return;
        }

        if (!EnsureFfmpegForExport("Trim export", allowSlowFallback: true, out string? ffmpegExecutable, out bool _))
        {
            return;
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
        string? ffmpegEncoderUsed = null;
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
                if (!string.IsNullOrWhiteSpace(ffmpegExecutable))
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
                        out string? encoderUsed,
                        out string? outputPath,
                        out string message);
                    if (exported)
                    {
                        if (!string.IsNullOrWhiteSpace(encoderUsed) && ffmpegEncoderUsed is null)
                        {
                            ffmpegEncoderUsed = encoderUsed;
                        }
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
            string exportPathText = !string.IsNullOrWhiteSpace(ffmpegExecutable)
                ? $"FFmpeg {ffmpegEncoderUsed ?? "unknown"}"
                : "Built-in (mp4v)";
            RecordDiagnostics(
                exportStatus: $"Trim export OK [{exportPathText}] ({savedFiles.Count} files)",
                error: skippedReasons.Count > 0 ? skippedReasons[0] : "None");
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
            if (!EnsureFfmpegForExport("Combined export", allowSlowFallback: true, out string? ffmpegExecutable, out bool _))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(ffmpegExecutable))
            {
                progressForm.UpdateProgress(0, 0d, "Checking FFmpeg combined export...");
                Application.DoEvents();
                if (!TryPreflightCombinedExportWithFfmpeg(ffmpegExecutable, trimIn, trimOut, isSideBySide, out string preflightError))
                {
                    DialogResult fallbackPrompt = MessageBox.Show(
                        this,
                        "FFmpeg combined export failed a quick preflight check.\n\nReason:\n" + preflightError + "\n\nContinue with built-in slow mode instead?",
                        "FFmpeg Export Failed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (fallbackPrompt != DialogResult.Yes)
                    {
                        RecordDiagnostics(exportStatus: "Combined export canceled", error: preflightError);
                        return;
                    }

                    ffmpegExecutable = null;
                }
            }

            bool exported = false;
            string message = "Combined export failed.";
            string ffmpegFailureMessage = string.Empty;
            string? ffmpegEncoderUsed = null;
            if (!string.IsNullOrWhiteSpace(ffmpegExecutable))
            {
                exported = TryExportCombinedVideoWithFfmpeg(
                    ffmpegExecutable,
                    trimIn,
                    trimOut,
                    saveDialog.FileName,
                    isSideBySide,
                    ratio =>
                    {
                        progressForm.UpdateProgress(0, ratio, "Saving combined video...");
                        Application.DoEvents();
                    },
                    out ffmpegEncoderUsed,
                    out message);
                if (!exported)
                {
                    ffmpegFailureMessage = message;
                }
            }

            if (!exported)
            {
                if (!string.IsNullOrWhiteSpace(ffmpegFailureMessage))
                {
                    DialogResult fallbackPrompt = MessageBox.Show(
                        this,
                        "FFmpeg failed, so FrameComp can fall back to built-in slow mode (codec mp4v).\n\nReason:\n" + ffmpegFailureMessage + "\n\nContinue with slow mode?",
                        "FFmpeg Export Failed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (fallbackPrompt != DialogResult.Yes)
                    {
                        RecordDiagnostics(exportStatus: "Combined export canceled", error: ffmpegFailureMessage);
                        return;
                    }
                }

                exported = TryExportCombinedVideoBuiltIn(
                    trimIn,
                    trimOut,
                    saveDialog.FileName,
                    isSideBySide,
                    ratio =>
                    {
                        progressForm.UpdateProgress(0, ratio, "Saving combined video...");
                        Application.DoEvents();
                    },
                    out message);
            }

            if (exported)
            {
                progressForm.UpdateProgress(0, 1d, "Combined video saved.");
                Application.DoEvents();
                bool usedBuiltInFallback = !string.IsNullOrWhiteSpace(ffmpegFailureMessage);
                string exportPathText = usedBuiltInFallback
                    ? "Built-in fallback (mp4v)"
                    : $"FFmpeg {ffmpegEncoderUsed ?? "unknown"}";
                RecordDiagnostics(
                    exportStatus: $"Combined export OK [{exportPathText}] ({Path.GetFileName(saveDialog.FileName)})",
                    error: usedBuiltInFallback ? ffmpegFailureMessage : "None");
                if (usedBuiltInFallback)
                {
                    MessageBox.Show(
                        this,
                        "FFmpeg failed and FrameComp fell back to built-in slow mode (codec mp4v).\n\nReason:\n" + ffmpegFailureMessage,
                        "FFmpeg Fallback Used",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
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
            writer.Set(VideoWriterProperties.Quality, ExportWriterQualityPercent);

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

    private bool TryBuildCombinedFfmpegPlan(
        int trimInGlobal,
        int trimOutGlobal,
        bool sideBySide,
        out List<string> inputPaths,
        out string filterComplex,
        out double fps,
        out double durationSeconds,
        out int outputWidth,
        out int outputHeight,
        out string failureReason)
    {
        inputPaths = [];
        filterComplex = string.Empty;
        fps = 30d;
        durationSeconds = 0.001d;
        outputWidth = 0;
        outputHeight = 0;
        failureReason = string.Empty;

        int totalFrames = Math.Max(1, trimOutGlobal - trimInGlobal + 1);
        int leftMaxWidth = _leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource ? Math.Max(1, _leftTrack.FrameSize.Width) : 0;
        int rightMaxWidth = _rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource ? Math.Max(1, _rightTrack.FrameSize.Width) : 0;
        int leftMaxHeight = _leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource ? Math.Max(1, _leftTrack.FrameSize.Height) : 0;
        int rightMaxHeight = _rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource ? Math.Max(1, _rightTrack.FrameSize.Height) : 0;
        int maxWidth = Math.Max(leftMaxWidth, rightMaxWidth);
        int maxHeight = Math.Max(leftMaxHeight, rightMaxHeight);
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            failureReason = "No video frames are available to combine in the selected range.";
            return false;
        }

        outputWidth = sideBySide ? maxWidth * 2 : maxWidth;
        outputHeight = sideBySide ? maxHeight : maxHeight * 2;

        if (_leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource && _leftTrack.Fps > 0.001d)
        {
            fps = _leftTrack.Fps;
        }
        if (_rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource && _rightTrack.Fps > fps)
        {
            fps = _rightTrack.Fps;
        }

        durationSeconds = Math.Max(0.001d, totalFrames / Math.Max(0.001d, fps));
        string durationText = durationSeconds.ToString("0.000000", CultureInfo.InvariantCulture);
        string fpsText = fps.ToString("0.###", CultureInfo.InvariantCulture);

        double fpsLocal = fps;
        var filters = new List<string>();
        string BuildTrackFilter(VideoTrack track, string inputLabel, string outputLabel)
        {
            if (TryGetTrackTrimRange(track, trimInGlobal, trimOutGlobal, out int localStart, out int localEnd, out _))
            {
                int globalStart = track.TimelineStartFrame + localStart;
                int leadingFrames = Math.Max(0, globalStart - trimInGlobal);
                int overlapFrames = Math.Max(0, localEnd - localStart + 1);
                int trailingFrames = Math.Max(0, totalFrames - leadingFrames - overlapFrames);
                string leadSeconds = (leadingFrames / fpsLocal).ToString("0.######", CultureInfo.InvariantCulture);
                string trailSeconds = (trailingFrames / fpsLocal).ToString("0.######", CultureInfo.InvariantCulture);
                string tpadSegment = (leadingFrames > 0 || trailingFrames > 0)
                    ? $",tpad=start_duration={leadSeconds}:stop_duration={trailSeconds}:stop_mode=add"
                    : string.Empty;
                return $"{inputLabel}trim=start_frame={localStart}:end_frame={localEnd + 1},setpts=PTS-STARTPTS{tpadSegment},scale={maxWidth}:{maxHeight}:force_original_aspect_ratio=decrease,pad={maxWidth}:{maxHeight}:(ow-iw)/2:(oh-ih)/2:black,setsar=1,format=yuv420p{outputLabel}";
            }

            return $"color=c=black:s={maxWidth}x{maxHeight}:d={durationText},format=yuv420p{outputLabel}";
        }

        int inputIndex = 0;
        if (_leftTrack.IsLoaded && !_leftTrack.IsTemporaryWindowSource && File.Exists(_leftTrack.FilePath))
        {
            inputPaths.Add(_leftTrack.FilePath);
            filters.Add(BuildTrackFilter(_leftTrack, $"[{inputIndex}:v]", "[vleft]"));
            inputIndex++;
        }
        else
        {
            filters.Add($"color=c=black:s={maxWidth}x{maxHeight}:d={durationText},format=yuv420p[vleft]");
        }

        if (_rightTrack.IsLoaded && !_rightTrack.IsTemporaryWindowSource && File.Exists(_rightTrack.FilePath))
        {
            inputPaths.Add(_rightTrack.FilePath);
            filters.Add(BuildTrackFilter(_rightTrack, $"[{inputIndex}:v]", "[vright]"));
        }
        else
        {
            filters.Add($"color=c=black:s={maxWidth}x{maxHeight}:d={durationText},format=yuv420p[vright]");
        }

        string stackFilter = sideBySide
            ? "[vleft][vright]hstack=inputs=2,fps=" + fpsText + "[vout]"
            : "[vleft][vright]vstack=inputs=2,fps=" + fpsText + "[vout]";
        filters.Add(stackFilter);

        filterComplex = string.Join(";", filters);
        return true;
    }

    private bool TryExportCombinedVideoWithFfmpeg(
        string ffmpegExecutable,
        int trimInGlobal,
        int trimOutGlobal,
        string outputPath,
        bool sideBySide,
        Action<double>? onProgress,
        out string? encoderUsed,
        out string message)
    {
        encoderUsed = null;
        if (!TryBuildCombinedFfmpegPlan(
                trimInGlobal,
                trimOutGlobal,
                sideBySide,
                out List<string> inputs,
                out string filterComplex,
                out double fps,
                out double durationSeconds,
                out int outputWidth,
                out int outputHeight,
                out string reason))
        {
            message = reason;
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-v");
            process.StartInfo.ArgumentList.Add("error");
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-progress");
            process.StartInfo.ArgumentList.Add("pipe:1");
            process.StartInfo.ArgumentList.Add("-nostats");

            foreach (string input in inputs)
            {
                process.StartInfo.ArgumentList.Add("-i");
                process.StartInfo.ArgumentList.Add(input);
            }

            process.StartInfo.ArgumentList.Add("-filter_complex");
            process.StartInfo.ArgumentList.Add(filterComplex);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("[vout]");
            encoderUsed = AddFfmpegVideoEncodingArguments(process.StartInfo, ffmpegExecutable, outputWidth, outputHeight);
            process.StartInfo.ArgumentList.Add("-an");
            process.StartInfo.ArgumentList.Add(outputPath);

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

            var errorLines = new List<string>();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    if (errorLines.Count < 80)
                    {
                        errorLines.Add(e.Data.Trim());
                    }
                }
            };

            if (!process.Start())
            {
                message = "Combined export: could not start ffmpeg.";
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            onProgress?.Invoke(1d);

            if (process.ExitCode != 0)
            {
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Delete(outputPath);
                    }
                    catch
                    {
                    }
                }

                string errorOutput = string.Join("\n", errorLines.TakeLast(16));
                message = string.IsNullOrWhiteSpace(errorOutput)
                    ? "Combined export failed in ffmpeg."
                    : $"Combined export failed in ffmpeg: {errorOutput}";
                return false;
            }

            message = "Combined video saved.";
            return true;
        }
        catch (Exception ex)
        {
            if (File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch
                {
                }
            }

            message = $"Combined export failed ({ex.Message}).";
            return false;
        }
    }

    private bool TryPreflightCombinedExportWithFfmpeg(
        string ffmpegExecutable,
        int trimInGlobal,
        int trimOutGlobal,
        bool sideBySide,
        out string message)
    {
        message = string.Empty;
        if (!TryBuildCombinedFfmpegPlan(
                trimInGlobal,
                trimOutGlobal,
                sideBySide,
                out List<string> inputs,
                out string filterComplex,
                out _,
                out _,
                out _,
                out _,
                out string reason))
        {
            message = reason;
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-v");
            process.StartInfo.ArgumentList.Add("error");
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-nostats");

            foreach (string input in inputs)
            {
                process.StartInfo.ArgumentList.Add("-i");
                process.StartInfo.ArgumentList.Add(input);
            }

            process.StartInfo.ArgumentList.Add("-filter_complex");
            process.StartInfo.ArgumentList.Add(filterComplex);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("[vout]");
            process.StartInfo.ArgumentList.Add("-frames:v");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-an");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("null");
            process.StartInfo.ArgumentList.Add("-");

            string stderr = string.Empty;
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    stderr = e.Data.Trim();
                }
            };

            if (!process.Start())
            {
                message = "Could not start ffmpeg for preflight.";
                return false;
            }

            process.BeginErrorReadLine();
            process.WaitForExit(8000);
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
                message = "FFmpeg preflight timed out.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                message = string.IsNullOrWhiteSpace(stderr) ? "FFmpeg preflight failed." : stderr;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
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
        if (_isSavingTrimmedVideos || !HasLoadedVideoTrack())
        {
            return false;
        }

        int trimIn = GetGlobalTrimIn();
        int trimOut = GetGlobalTrimOut();
        return new[] { _leftTrack, _rightTrack }.Any(track => HasTrackTrimOverlap(track, trimIn, trimOut));
    }

    private bool CanExportCombinedRange()
    {
        if (_isSavingTrimmedVideos)
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
                ("Gyan.FFmpeg", "install --id Gyan.FFmpeg --exact --scope user --silent --disable-interactivity --accept-package-agreements --accept-source-agreements"),
                ("BtbN.FFmpeg", "install --id BtbN.FFmpeg --exact --scope user --silent --disable-interactivity --accept-package-agreements --accept-source-agreements"),
                ("ffmpeg (generic)", "install ffmpeg --scope user --silent --disable-interactivity --accept-package-agreements --accept-source-agreements")
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
        foreach (string candidate in EnumerateLikelyFfmpegExecutables())
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

    private static IEnumerable<string> EnumerateLikelyFfmpegExecutables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Enumerate()
        {
            yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            yield return Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe");
            string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
                string packagesRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(packagesRoot))
                {
                    foreach (string packageDir in Directory.GetDirectories(packagesRoot, "*ffmpeg*", SearchOption.TopDirectoryOnly))
                    {
                        IEnumerable<string> buildDirs = Enumerable.Empty<string>();
                        try
                        {
                            buildDirs = Directory.EnumerateDirectories(packageDir, "ffmpeg*", SearchOption.TopDirectoryOnly);
                        }
                        catch
                        {
                        }

                        foreach (string buildDir in buildDirs)
                        {
                            string knownBinPath = Path.Combine(buildDir, "bin", "ffmpeg.exe");
                            yield return knownBinPath;
                            string directPath = Path.Combine(buildDir, "ffmpeg.exe");
                            yield return directPath;
                        }

                        IEnumerable<string> files = Enumerable.Empty<string>();
                        try
                        {
                            files = Directory.EnumerateFiles(packageDir, "ffmpeg.exe", SearchOption.AllDirectories);
                        }
                        catch
                        {
                        }

                        foreach (string file in files)
                        {
                            yield return file;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe");
                yield return Path.Combine(programFiles, "ffmpeg", "ffmpeg.exe");
            }

            yield return Path.Combine("C:\\ProgramData", "chocolatey", "bin", "ffmpeg.exe");

            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                yield return Path.Combine(userProfile, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe");
            }

            yield return "ffmpeg";
        }

        foreach (string candidate in Enumerate())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
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
        out string? encoderUsed,
        out string? outputPath,
        out string message)
    {
        encoderUsed = null;
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
            int outW = Math.Max(1, track.FrameSize.Width);
            int outH = Math.Max(1, track.FrameSize.Height);
            process.StartInfo = BuildFfmpegStartInfo(ffmpegExecutable, sourcePath, candidatePath, startSeconds, endSeconds, outW, outH, out encoderUsed);
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

            var errorLines = new List<string>();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    if (errorLines.Count < 80)
                    {
                        errorLines.Add(e.Data.Trim());
                    }
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

                string errorOutput = string.Join("\n", errorLines.TakeLast(12));
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
            writer.Set(VideoWriterProperties.Quality, ExportWriterQualityPercent);

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

    private ProcessStartInfo BuildFfmpegStartInfo(
        string ffmpegExecutable,
        string sourcePath,
        string outputPath,
        double startSeconds,
        double endSeconds,
        int outputWidth,
        int outputHeight,
        out string encoderUsed)
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
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
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
        encoderUsed = AddFfmpegVideoEncodingArguments(psi, ffmpegExecutable, outputWidth, outputHeight);
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("192k");
        psi.ArgumentList.Add(outputPath);
        return psi;
    }

    private sealed record TrimExportPlan(VideoTrack Track, int LocalStart, int LocalEnd);
    private sealed record ProcessRunResult(int ExitCode, bool TimedOut, string StdOut, string StdErr, string ErrorMessage);
}
