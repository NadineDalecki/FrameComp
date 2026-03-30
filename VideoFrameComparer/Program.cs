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

        Application.Run(new Form1(projectSelectionForm.SelectedProjectPath));
    }    
}
