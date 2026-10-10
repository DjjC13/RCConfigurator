using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RCConfigurator.App;

/// <summary>
/// The Windows 11 setting that makes a right-click show the classic menu straight away, instead
/// of the shorter menu with "Show more options". The rules edit the classic menu.
/// </summary>
public static class ClassicMenu
{
    // An empty default value here stops Windows loading its new menu, for this user only.
    private const string KeyPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const int FirstWindows11Build = 22000;

    /// <summary>False on Windows 10, which only has the classic menu.</summary>
    public static bool IsNeeded => Environment.OSVersion.Version.Build >= FirstWindows11Build;

    public static bool IsOn
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("") is string value && value.Length == 0;
        }
    }

    public static bool IsSatisfied => !IsNeeded || IsOn;

    public static void TurnOn()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("", "");
    }
}

/// <summary>Gets Nilesoft Shell onto the PC.</summary>
public static class EngineInstaller
{
    public const string DownloadPage = "https://nilesoft.org/download";

    private const string Package = "--id Nilesoft.Shell --exact --source winget";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Installs the engine. Returns false if the Windows package manager is not available.</summary>
    public static Task<bool> InstallAsync() => RunInWindowAsync($"install {Package}");

    /// <summary>Updates the engine to the newest version. Returns false if the Windows package manager is not available.</summary>
    public static Task<bool> UpgradeAsync() => RunInWindowAsync($"upgrade {Package}");

    /// <summary>Asks the Windows package manager whether a newer engine exists. False if it cannot say.</summary>
    public static async Task<bool> IsUpdateAvailableAsync()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("winget", $"list {Package} --upgrade-available --disable-interactivity")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return false;

            using var timeout = new CancellationTokenSource(CheckTimeout);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            // The package is listed only when an upgrade exists; otherwise the list is empty.
            return output.Contains("Nilesoft.Shell", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the Windows package manager in its own window, so the user sees its progress and any
    /// questions it asks. Returns false if the package manager is not available.
    /// </summary>
    private static async Task<bool> RunInWindowAsync(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("winget", arguments) { UseShellExecute = true });
            if (process is null) return false;

            await process.WaitForExitAsync();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static void OpenDownloadPage() =>
        Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true })?.Dispose();
}

/// <summary>Saves a picture of a window's content. A development aid.</summary>
public static class WindowPicture
{
    public static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var margin = content.Margin;
        var size = new Size(content.ActualWidth + margin.Left + margin.Right, content.ActualHeight + margin.Top + margin.Bottom);

        // The themed window background is drawn by Windows, so paint one in for the picture:
        // dark behind light text and light behind dark text.
        var text = (window.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
        var isDarkTheme = text.R + text.G + text.B > 3 * 128;
        var picture = new DrawingVisual();
        using (var drawing = picture.RenderOpen())
        {
            drawing.DrawRectangle(isDarkTheme ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) : Brushes.White, null, new Rect(size));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(margin.Left, margin.Top, content.ActualWidth, content.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap(
            (int)(size.Width * dpi.DpiScaleX), (int)(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(picture);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
