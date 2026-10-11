using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RCConfigurator.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly string? _screenshotPath;
    private readonly bool _demo;
    private readonly string? _showFileChoice;
    private readonly bool _offline;

    public MainWindow(string? screenshotPath = null, bool demo = false, string? showFileChoice = null, bool offline = false)
    {
        _screenshotPath = screenshotPath;
        _demo = demo;
        _showFileChoice = showFileChoice;
        _offline = offline;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.AskForNewItem = (existing, groups, kinds) =>
        {
            var dialog = new NewItemWindow(existing, groups, kinds) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        };
        _viewModel.ShowCleanup = () =>
        {
            var dialog = new CleanupWindow { Owner = this };
            dialog.ShowDialog();
            return dialog.ChangedSomething;
        };
        _viewModel.AskForGroups = (mine, taken) =>
        {
            var dialog = new GroupsWindow(mine, taken) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        };
        Loaded += OnLoaded;
    }

    private void OnNewItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.EditItemCommand.CanExecute(null)) _viewModel.EditItemCommand.Execute(null);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_showFileChoice is null)
        {
            // Show the first menu straight away; the groups fill out once the lists arrive.
            await _viewModel.RefreshAsync();
            await _viewModel.LoadOnlineFileTypesAsync(_offline);
        }
        else
        {
            await _viewModel.LoadOnlineFileTypesAsync(_offline);
            await _viewModel.ShowFileChoiceAsync(_showFileChoice);
        }

        await _viewModel.CheckForLeftoversAsync();

        if (_screenshotPath is null)
        {
            await _viewModel.UpdateEngineIfNeededAsync();
            return;
        }

        if (_demo) MakeDemoChanges();

        // Select something so the picture shows the editing panel too.
        _viewModel.SelectedItem ??= _viewModel.Items.FirstOrDefault(i => !i.IsSeparator);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        WindowPicture.Save(this, _screenshotPath);
        if (_demo) File.WriteAllText(Path.ChangeExtension(_screenshotPath, ".nss"), _viewModel.BuildConfig());
        Close();
    }

    private void MakeDemoChanges()
    {
        Change("Ask Copilot", i => i.IsHidden = true);
        Change("Print", i =>
        {
            i.Scope = _viewModel.ScopeOptions[0].Value;
            i.IsHidden = true;
        });
        Change("Share", i => i.Group = "Extras");
        Change("Restore previous versions", i => i.Group = "Extras");
        Change("Copy as path", i => i.Placement = Core.Placement.Bottom);
        Change("Delete", i =>
        {
            _viewModel.SelectedItem = i;
            _viewModel.MoveUpCommand.Execute(null);
            _viewModel.MoveUpCommand.Execute(null);
        });

        _viewModel.AskForNewItem = (_, _, _) => new Core.NewItem
        {
            Title = "Open in my editor",
            Program = @"C:\Tools\editor.exe",
            Arguments = "\"%1\"",
            Scope = Core.Scope.ForFile(null),
        };
        _viewModel.AddItemCommand.Execute(null);

        Change("Open with Code", i =>
        {
            i.NewTitle = "VS Code";
            i.Placement = Core.Placement.Top;
            _viewModel.SelectedItem = i;
        });

        void Change(string title, Action<MenuItemViewModel> change)
        {
            if (_viewModel.Items.FirstOrDefault(i => i.OriginalTitle == title) is { } item) change(item);
        }
    }

    private void OnItemSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedItem = e.NewValue as MenuItemViewModel;
    }

    private void OnOpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        // Opens the link in the user's own browser.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }

    private void OnGroupPicked(object? sender, EventArgs e)
    {
        // Typed text is saved when the box loses focus; a pick from the list should count straight away.
        (sender as System.Windows.Controls.ComboBox)?.GetBindingExpression(System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();
    }
}
