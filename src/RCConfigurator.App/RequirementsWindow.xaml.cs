using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RCConfigurator.App;

/// <summary>
/// Shown at start-up while something the app depends on is missing: Nilesoft Shell installed,
/// Windows 11 showing its classic menu, and the engine registered and loaded in Explorer.
/// The editor only opens once all of them are in place.
/// </summary>
public partial class RequirementsWindow : Window
{
    private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xA4, 0x6C));

    private readonly string? _screenshotPath;
    private bool _settingUp;

    public RequirementsWindow(string? screenshotPath = null)
    {
        _screenshotPath = screenshotPath;
        InitializeComponent();
        Refresh();

        // The user may do a step themselves and come back.
        Activated += (_, _) => Refresh();
        Loaded += OnLoaded;
    }

    public static bool AreMet
    {
        get
        {
            var engine = Engine.Find();
            return engine.IsInstalled && ClassicMenu.IsSatisfied && IsRunning(engine);
        }
    }

    // When the loaded state cannot be seen, being registered is taken as enough.
    private static bool IsRunning(Engine engine) => engine.IsInstalled && Engine.IsRegistered && engine.IsLoaded() != false;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_screenshotPath is null) return;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        WindowPicture.Save(this, _screenshotPath);
        Close();
    }

    private void Refresh()
    {
        if (_settingUp) return;

        var engine = Engine.Find();
        Mark(EngineMark, engine.IsInstalled);
        EngineText.Text = engine.IsInstalled
            ? $"Version {engine.Version}."
            : "Not installed. This is the program that applies your changes inside Explorer. Installing it asks for administrator permission.";

        Mark(ClassicMark, ClassicMenu.IsSatisfied);
        ClassicText.Text =
            !ClassicMenu.IsNeeded ? "Not needed on this version of Windows."
            : ClassicMenu.IsOn ? "Yes."
            : "Windows 11 is showing its newer, shorter menu. Your changes appear in the classic one, so Windows needs to show that instead. This is a setting for your account only.";

        var running = IsRunning(engine);
        Mark(RunningMark, running);
        RunningText.Text =
            running ? "Yes. It starts with Windows by itself."
            : !engine.IsInstalled ? "It needs installing first."
            : !Engine.IsRegistered ? "It is installed but not registered with Windows. Registering it asks for administrator permission."
            : "It is registered but Explorer has not loaded it yet. Restarting Explorer fixes that.";

        var ready = engine.IsInstalled && ClassicMenu.IsSatisfied && running;
        ContinueButton.IsEnabled = ready;
        SetUpButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void Mark(TextBlock mark, bool done)
    {
        mark.Text = done ? "✓" : "○";
        if (done) mark.Foreground = DoneBrush;
        else mark.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>Does every missing step in order, restarting Explorer once at the end if anything needs it.</summary>
    private async void OnSetUp(object sender, RoutedEventArgs e)
    {
        _settingUp = true;
        SetUpButton.IsEnabled = false;
        try
        {
            StatusText.Text = await SetUpAsync();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            StatusText.Text = $"Could not finish setting up. {error.Message}";
        }
        finally
        {
            _settingUp = false;
            SetUpButton.IsEnabled = true;
            Refresh();
        }
    }

    private async Task<string> SetUpAsync()
    {
        var engine = Engine.Find();
        if (!engine.IsInstalled)
        {
            StatusText.Text = "Installing Nilesoft Shell. Follow the prompts in the window that opens.";
            if (!await EngineInstaller.InstallAsync())
            {
                EngineInstaller.OpenDownloadPage();
                return "The Windows package manager is not available, so the download page has been opened instead. Come back here once it is installed.";
            }

            engine = Engine.Find();
            if (!engine.IsInstalled)
            {
                return $"Nilesoft Shell is still not installed. You can also download it from {EngineInstaller.DownloadPage}.";
            }
        }

        var restartNeeded = false;
        if (!ClassicMenu.IsSatisfied)
        {
            ClassicMenu.TurnOn();
            restartNeeded = true;
        }

        if (!Engine.IsRegistered)
        {
            StatusText.Text = "Registering Nilesoft Shell with Windows. Windows will ask for permission.";
            if (!await engine.RegisterAsync())
            {
                return "Nilesoft Shell could not be registered because permission was not given. Choose the button again to retry.";
            }

            // Registering restarts Explorer, which also picks up the menu setting.
            restartNeeded = false;
        }

        if (restartNeeded || engine.IsLoaded() == false)
        {
            StatusText.Text = "Restarting Explorer…";
            if (!await Task.Run(ExplorerRestart.Restart))
            {
                return "Explorer did not come back by itself. Press Ctrl+Shift+Esc, choose Run new task and type explorer.";
            }
        }

        return IsRunning(engine) && ClassicMenu.IsSatisfied ? "Everything is ready." : "Something is still missing. Choose the button again to retry.";
    }

    private void OnContinue(object sender, RoutedEventArgs e) => DialogResult = true;
}
