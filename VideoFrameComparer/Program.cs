namespace VideoFrameComparer;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            try
            {
                AppLog.WriteError("Unhandled UI exception", e.Exception);
            }
            finally
            {
                MessageBox.Show(
                    "FrameComp hit an unexpected error and needs to close.\n\n" +
                    "A log file was written next to the app:\n" +
                    Path.Combine(AppContext.BaseDirectory, "FrameComp.log") +
                    "\n\n" +
                    e.Exception.Message,
                    "Unexpected Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Exception? ex = e.ExceptionObject as Exception;
            AppLog.WriteError("Unhandled non-UI exception", ex);
            try
            {
                MessageBox.Show(
                    "FrameComp hit an unexpected error and needs to close.\n\n" +
                    "A log file was written next to the app:\n" +
                    Path.Combine(AppContext.BaseDirectory, "FrameComp.log"),
                    "Unexpected Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.WriteError("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

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
            AppLog.WriteError("Startup error while creating main form", ex);
            MessageBox.Show(
                $"Could not open the selected project.\n\n{ex.Message}\n\nLog:\n{Path.Combine(AppContext.BaseDirectory, "FrameComp.log")}",
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
