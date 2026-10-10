using System.IO;
using System.Windows;

namespace RCConfigurator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (ValueOf(e.Args, "--profile") is { } profilePath) ProfileStore.FilePath = Path.GetFullPath(profilePath);

        // Development aids: "--screenshot file.png" saves a picture of the window and exits,
        // and "--demo" makes a few sample changes first and writes the resulting config beside it.
        new MainWindow(ValueOf(e.Args, "--screenshot"), demo: e.Args.Contains("--demo")).Show();
    }

    private static string? ValueOf(string[] args, string option)
    {
        var at = Array.IndexOf(args, option);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
