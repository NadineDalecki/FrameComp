namespace VideoFrameComparer;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        using var projectSelectionForm = new ProjectSelectionForm();
        if (projectSelectionForm.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(projectSelectionForm.SelectedProjectPath))
        {
            return;
        }

        using var splashHost = new LoadingSplashHost();

        Form1 mainForm;
        try
        {
            mainForm = new Form1(projectSelectionForm.SelectedProjectPath);
        }
        catch (Exception ex)
        {
            splashHost.Close();
            MessageBox.Show(
                $"Could not open the selected project.\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        splashHost.Close();
        Application.Run(mainForm);
    }

    private sealed class LoadingSplashHost : IDisposable
    {
        private readonly ManualResetEventSlim _shownSignal = new(false);
        private readonly Thread _uiThread;
        private LoadingForm? _loadingForm;

        public LoadingSplashHost()
        {
            _uiThread = new Thread(RunSplashThread)
            {
                IsBackground = true
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _shownSignal.Wait();
        }

        private void RunSplashThread()
        {
            _loadingForm = new LoadingForm();
            _loadingForm.Shown += (_, _) => _shownSignal.Set();
            Application.Run(_loadingForm);
        }

        public void Close()
        {
            if (_loadingForm is null || _loadingForm.IsDisposed)
            {
                return;
            }

            try
            {
                _loadingForm.BeginInvoke(new Action(() => _loadingForm.Close()));
            }
            catch
            {
                // No-op: splash may already be closing.
            }

            if (_uiThread.IsAlive)
            {
                _uiThread.Join(1000);
            }
        }

        public void Dispose()
        {
            Close();
            _shownSignal.Dispose();
        }
    }
}
