namespace VideoFrameComparer;

public partial class Form1
{
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
    }
}
