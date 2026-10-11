using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>The dialog for adding or editing an item of the user's own.</summary>
public partial class NewItemWindow : Window
{
    private const string DefaultArguments = "\"" + NewItem.PathPlaceholder + "\"";

    private static readonly IReadOnlyList<Choice<Placement>> Placements =
    [
        new("Middle of the menu", Placement.Default),
        new("Top of the menu", Placement.Top),
        new("Bottom of the menu", Placement.Bottom),
    ];

    // The entry with no scope of its own is "Files of one type", which takes its type from the box beside it.
    private readonly List<Choice<Scope?>> _scopes;

    public NewItemWindow(NewItem? existing, IReadOnlyList<string> groups, IReadOnlyList<FileKind> kinds)
    {
        InitializeComponent();
        _scopes =
        [
            new("Any file", Scope.ForFile(null)),
            new("Files of one type", null),
            .. kinds.Select(k => new Choice<Scope?>($"Files in {k.Name}", Scope.ForGroup(k.Name))),
            new("A folder", new Scope(ScopeKind.Folder)),
            new("A drive", new Scope(ScopeKind.Drive)),
            new("Empty space in a folder", new Scope(ScopeKind.FolderBackground)),
            new("Empty space on the desktop", new Scope(ScopeKind.Desktop)),
            new("Anything", Scope.Everywhere),
        ];
        ScopeBox.ItemsSource = _scopes;
        PlacementBox.ItemsSource = Placements;
        GroupBox.ItemsSource = groups;

        TitleBox.Text = existing?.Title ?? "";
        ProgramBox.Text = existing?.Program ?? "";
        ArgumentsBox.Text = existing?.Arguments ?? DefaultArguments;
        ExtensionBox.Text = existing?.Scope.Extension ?? ".txt";
        GroupBox.Text = existing?.Group ?? "";
        PlacementBox.SelectedItem = Placements.FirstOrDefault(p => p.Value == existing?.Placement) ?? Placements[0];

        var scope = (existing?.Scope ?? Scope.ForFile(null)) with { Count = SelectionCount.Any };
        ScopeBox.SelectedItem = scope.Extension is not null
            ? _scopes[1]
            : _scopes.FirstOrDefault(s => s.Value == scope) ?? _scopes[0];

        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>The item the user described, once the dialog has been saved.</summary>
    public NewItem? Result { get; private set; }

    private bool IsOneFileType => ScopeBox.SelectedItem is Choice<Scope?> { Value: null };

    private void OnScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        ExtensionBox.Visibility = IsOneFileType ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the program to run",
            Filter = "Programs and scripts|*.exe;*.cmd;*.bat;*.ps1|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true) ProgramBox.Text = dialog.FileName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        var program = ProgramBox.Text.Trim().Trim('"');
        var extension = ExtensionBox.Text.Trim().ToLowerInvariant();

        var problem =
            title.Length == 0 ? "Give the item a name."
            : program.Length == 0 ? "Choose the program to run."
            : IsOneFileType && !SampleFiles.IsValidExtension(extension) ? "Type a file extension such as .txt."
            : null;
        if (problem is not null)
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }

        var group = GroupBox.Text.Replace("/", "").Trim();
        Result = new NewItem
        {
            Title = title,
            Program = program,
            Arguments = ArgumentsBox.Text.Trim(),
            Scope = ((Choice<Scope?>)ScopeBox.SelectedItem).Value ?? Scope.ForFile(extension),
            Placement = ((Choice<Placement>)PlacementBox.SelectedItem).Value,
            Group = group.Length > 0 ? group : null,
        };
        DialogResult = true;
    }
}
