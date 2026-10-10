using System.IO;
using System.Windows;

namespace RCConfigurator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (ValueOf(e.Args, "--profile") is { } profilePath) ProfileStore.FilePath = Path.GetFullPath(profilePath);

        // Development aids: "--screenshot file.png" saves a picture of the first window and exits,
        // "--demo" makes a few sample changes first and writes the resulting config beside it, and
        // "--skip-checks" opens the editor without the start-up requirements.
        var screenshotPath = ValueOf(e.Args, "--screenshot");

        // Closing the requirements window must not end the app before the editor opens.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!e.Args.Contains("--skip-checks") && !RequirementsWindow.AreMet)
        {
            if (new RequirementsWindow(screenshotPath).ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var editor = new MainWindow(screenshotPath, demo: e.Args.Contains("--demo"));
        MainWindow = editor;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        editor.Show();
    }

    private static string? ValueOf(string[] args, string option)
    {
        var at = Array.IndexOf(args, option);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
