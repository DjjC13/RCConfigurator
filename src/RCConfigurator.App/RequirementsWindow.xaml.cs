using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RCConfigurator.App;

/// <summary>
/// Shown at start-up while Nilesoft Shell is missing or Windows 11 is still using its new menu.
/// The app only opens once both are in place.
/// </summary>
public partial class RequirementsWindow : Window
{
    private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xA4, 0x6C));

    private readonly string? _screenshotPath;

    public RequirementsWindow(string? screenshotPath = null)
    {
        _screenshotPath = screenshotPath;
        InitializeComponent();
        Refresh();

        // The user may install the engine themselves and come back.
        Activated += (_, _) => Refresh();
        Loaded += OnLoaded;
    }

    public static bool AreMet => Engine.Find().IsInstalled && ClassicMenu.IsSatisfied;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_screenshotPath is null) return;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        WindowPicture.Save(this, _screenshotPath);
        Close();
    }

    private void Refresh()
    {
        var engine = Engine.Find();
        Mark(EngineMark, engine.IsInstalled);
        EngineButton.Visibility = engine.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
        EngineText.Text = engine.IsInstalled
            ? $"Installed, version {engine.Version}."
            : "Not installed. This is the program that applies your changes inside Explorer. Installing it asks for administrator permission and restarts Explorer.";

        Mark(ClassicMark, ClassicMenu.IsSatisfied);
        ClassicButton.Visibility = ClassicMenu.IsSatisfied ? Visibility.Collapsed : Visibility.Visible;
        ClassicText.Text =
            !ClassicMenu.IsNeeded ? "Not needed on this version of Windows."
            : ClassicMenu.IsOn ? "Windows is set to show the classic menu."
            : "Windows 11 is showing its newer, shorter menu. Your changes appear in the classic one, so Windows needs to show that instead. This changes a setting for your account only.";

        ContinueButton.IsEnabled = engine.IsInstalled && ClassicMenu.IsSatisfied;
    }

    private static void Mark(System.Windows.Controls.TextBlock mark, bool done)
    {
        mark.Text = done ? "✓" : "○";
        if (done) mark.Foreground = DoneBrush;
        else mark.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
    }

    private async void OnInstallEngine(object sender, RoutedEventArgs e)
    {
        EngineButton.IsEnabled = false;
        StatusText.Text = "Installing. Follow the prompts in the window that opens.";
        try
        {
            if (!await EngineInstaller.RunWingetAsync())
            {
                EngineInstaller.OpenDownloadPage();
                StatusText.Text = "The Windows package manager is not available, so the download page has been opened instead. Come back here once it is installed.";
                return;
            }

            Refresh();
            StatusText.Text = Engine.Find().IsInstalled
                ? "Nilesoft Shell is installed."
                : $"Nilesoft Shell is still not installed. You can also download it from {EngineInstaller.DownloadPage}.";
        }
        finally
        {
            EngineButton.IsEnabled = true;
        }
    }

    private void OnUseClassicMenu(object sender, RoutedEventArgs e)
    {
        try
        {
            ClassicMenu.TurnOn();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            StatusText.Text = $"Could not change the setting. {error.Message}";
            return;
        }

        Refresh();
        StatusText.Text = Engine.Find().IsInstalled
            ? "Done. The classic menu appears once Explorer restarts: sign out and back in, or restart Windows Explorer from Task Manager."
            : "Done. The classic menu appears once Explorer restarts, which installing Nilesoft Shell does for you.";
    }

    private void OnContinue(object sender, RoutedEventArgs e) => DialogResult = true;
}
