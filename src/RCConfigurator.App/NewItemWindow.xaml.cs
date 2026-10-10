using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>The dialog for adding or editing an item of the user's own.</summary>
public partial class NewItemWindow : Window
{
    private const string DefaultArguments = "\"" + NewItem.PathPlaceholder + "\"";

    private static readonly IReadOnlyList<Choice<ScopeKind?>> Scopes =
    [
        new("Any file", ScopeKind.File),
        new("Files of one type", null),
        new("A folder", ScopeKind.Folder),
        new("A drive", ScopeKind.Drive),
        new("Empty space in a folder", ScopeKind.FolderBackground),
        new("Empty space on the desktop", ScopeKind.Desktop),
        new("Anything", ScopeKind.Everywhere),
    ];

    private static readonly IReadOnlyList<Choice<Placement>> Placements =
    [
        new("Middle of the menu", Placement.Default),
        new("Top of the menu", Placement.Top),
        new("Bottom of the menu", Placement.Bottom),
    ];

    public NewItemWindow(NewItem? existing, IReadOnlyList<string> groups)
    {
        InitializeComponent();
        ScopeBox.ItemsSource = Scopes;
        PlacementBox.ItemsSource = Placements;
        GroupBox.ItemsSource = groups;

        TitleBox.Text = existing?.Title ?? "";
        ProgramBox.Text = existing?.Program ?? "";
        ArgumentsBox.Text = existing?.Arguments ?? DefaultArguments;
        ExtensionBox.Text = existing?.Scope.Extension ?? ".txt";
        GroupBox.Text = existing?.Group ?? "";
        PlacementBox.SelectedItem = Placements.FirstOrDefault(p => p.Value == existing?.Placement) ?? Placements[0];

        // "Files of one type" is the entry with no kind of its own.
        var scope = existing?.Scope ?? Scope.ForFile(null);
        ScopeBox.SelectedItem = scope is { Kind: ScopeKind.File, Extension: not null }
            ? Scopes[1]
            : Scopes.First(s => s.Value == scope.Kind);

        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>The item the user described, once the dialog has been saved.</summary>
    public NewItem? Result { get; private set; }

    private bool IsOneFileType => ReferenceEquals(ScopeBox.SelectedItem, Scopes[1]);

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

        var kind = ((Choice<ScopeKind?>)ScopeBox.SelectedItem).Value;
        var group = GroupBox.Text.Replace("/", "").Trim();
        Result = new NewItem
        {
            Title = title,
            Program = program,
            Arguments = ArgumentsBox.Text.Trim(),
            Scope = kind is null ? Scope.ForFile(extension) : new Scope(kind.Value),
            Placement = ((Choice<Placement>)PlacementBox.SelectedItem).Value,
            Group = group.Length > 0 ? group : null,
        };
        DialogResult = true;
    }
}
