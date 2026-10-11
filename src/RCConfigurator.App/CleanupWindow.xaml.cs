using System.Windows;
using System.Windows.Threading;

namespace RCConfigurator.App;

/// <summary>Lists what uninstalled programs left in the registry and removes what the user ticks.</summary>
public partial class CleanupWindow : Window
{
    private readonly string? _screenshotPath;

    public CleanupWindow(string? screenshotPath = null)
    {
        _screenshotPath = screenshotPath;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>True once anything has been removed or put back, so the menus on show are out of date.</summary>
    public bool ChangedSomething { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ScanAsync();
        if (_screenshotPath is null) return;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        WindowPicture.Save(this, _screenshotPath);
        Close();
    }

    private async Task ScanAsync(string? thenSay = null)
    {
        SetBusy(true);
        StatusText.Text = "Scanning the registry…";
        var programs = await CleanupService.ScanAsync();
        ProgramList.ItemsSource = programs;
        StatusText.Text = thenSay ?? (programs.Count == 0
            ? "No obsolete entries were found. Every program that menus and file types refer to is installed."
            : "Select the programs to clean up after. Nothing is selected by default.");
        SetBusy(false);
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        var chosen = (ProgramList.ItemsSource as IReadOnlyList<LeftoverProgram> ?? []).Where(p => p.IsSelected).ToList();
        if (chosen.Count == 0)
        {
            StatusText.Text = "Select at least one program first.";
            return;
        }

        SetBusy(true);
        StatusText.Text = chosen.Any(p => p.NeedsAdministrator)
            ? "Removing. Windows will ask for permission, because some of these entries apply to the whole PC."
            : "Removing…";
        try
        {
            var outcome = await CleanupService.RemoveAsync(chosen);
            ChangedSomething |= outcome.Removed > 0;
            var summary = $"Removed {outcome.Removed} {(outcome.Removed == 1 ? "entry" : "entries")}. A backup is in {CleanupService.BackupFolder}.";
            if (outcome.Problems.Count > 0) summary += $" {outcome.Problems.Count} could not be removed: {outcome.Problems[0]}";
            await ScanAsync(summary);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            StatusText.Text = $"Could not finish. {error.Message}";
            SetBusy(false);
        }
    }

    private async void OnUndo(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        StatusText.Text = "Restoring the entries. Windows may ask for permission.";
        var result = await CleanupService.UndoAsync();
        ChangedSomething = true;
        await ScanAsync(result);
    }

    private void SetBusy(bool busy)
    {
        RemoveButton.IsEnabled = !busy;
        UndoButton.IsEnabled = !busy && CleanupService.LastBackups().Count > 0;
        ProgramList.IsEnabled = !busy;
    }
}
