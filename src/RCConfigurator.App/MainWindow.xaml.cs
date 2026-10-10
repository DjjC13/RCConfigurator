using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RCConfigurator.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly string? _screenshotPath;
    private readonly bool _demo;

    public MainWindow(string? screenshotPath = null, bool demo = false)
    {
        _screenshotPath = screenshotPath;
        _demo = demo;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.AskForNewItem = (existing, groups) =>
        {
            var dialog = new NewItemWindow(existing, groups) { Owner = this };
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
        await _viewModel.RefreshAsync();
        if (_screenshotPath is null) return;

        if (_demo) MakeDemoChanges();

        // Select something so the picture shows the editing panel too.
        _viewModel.SelectedItem ??= _viewModel.Items.FirstOrDefault(i => !i.IsSeparator);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveScreenshot(_screenshotPath);
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

        _viewModel.AskForNewItem = (_, _) => new Core.NewItem
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

    private void OnGroupPicked(object? sender, EventArgs e)
    {
        // Typed text is saved when the box loses focus; a pick from the list should count straight away.
        (sender as System.Windows.Controls.ComboBox)?.GetBindingExpression(System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();
    }

    private void SaveScreenshot(string path)
    {
        var content = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = content.Margin;
        var size = new Size(content.ActualWidth + margin.Left + margin.Right, content.ActualHeight + margin.Top + margin.Bottom);

        // The themed window background is drawn by Windows, so paint one in for the picture:
        // dark behind light text and light behind dark text.
        var text = (Foreground as SolidColorBrush)?.Color ?? Colors.Black;
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
