using System.IO;
using System.Windows;

namespace RCConfigurator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Started by the app itself, with administrator rights, to remove machine-wide leftovers.
        // It shows nothing and exits when done.
        if (ValueOf(e.Args, CleanupService.ElevatedOption) is { } cleanupRequest)
        {
            Shutdown(RCConfigurator.Cleanup.ElevatedCleanup.Run(cleanupRequest));
            return;
        }

        if (ValueOf(e.Args, "--profile") is { } profilePath) ProfileStore.FilePath = Path.GetFullPath(profilePath);

        // Development aids: "--screenshot file.png" saves a picture of the first window and exits,
        // "--demo" makes a few sample changes first and writes the resulting config beside it, and
        // "--skip-checks" opens the editor without the start-up requirements, and "--show-checks"
        // shows the set-up window even when everything is already in place. "--show Images" opens
        // the editor on the file-type entry whose name starts that way. "--offline" keeps the app
        // from downloading the public file-type lists.
        var screenshotPath = ValueOf(e.Args, "--screenshot");

        // Closing the requirements window must not end the app before the editor opens.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Development aid: "--show-cleanup" opens only the clean-up window.
        if (e.Args.Contains("--show-cleanup"))
        {
            new CleanupWindow(screenshotPath).ShowDialog();
            Shutdown();
            return;
        }
        if (!e.Args.Contains("--skip-checks") && (e.Args.Contains("--show-checks") || !RequirementsWindow.AreMet))
        {
            if (new RequirementsWindow(screenshotPath).ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var editor = new MainWindow(screenshotPath, demo: e.Args.Contains("--demo"), showFileChoice: ValueOf(e.Args, "--show"), offline: e.Args.Contains("--offline"));
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
