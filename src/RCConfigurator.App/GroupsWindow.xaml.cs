using System.Windows;
using System.Windows.Controls;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>The dialog where the user makes their own groups of file types.</summary>
public partial class GroupsWindow : Window
{
    private static readonly char[] Separators = [' ', ',', ';', '\t'];

    private readonly List<FileKind> _groups;
    private readonly IReadOnlyList<string> _takenNames;

    /// <param name="groups">The user's groups as they are now.</param>
    /// <param name="takenNames">Names of Windows' own groups, which cannot be reused.</param>
    public GroupsWindow(IReadOnlyList<FileKind> groups, IReadOnlyList<string> takenNames)
    {
        InitializeComponent();
        _groups = groups.ToList();
        _takenNames = takenNames;
        ShowGroups(selected: null);
    }

    /// <summary>The user's groups after editing, once the dialog has been closed with Done.</summary>
    public IReadOnlyList<FileKind>? Result { get; private set; }

    private void ShowGroups(string? selected)
    {
        var rows = _groups
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new Choice<FileKind>($"{g.Name}: {string.Join(' ', g.Extensions)}", g))
            .ToList();
        GroupList.ItemsSource = rows;
        GroupList.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Value.Name, selected, StringComparison.OrdinalIgnoreCase));
    }

    private void OnGroupSelected(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = GroupList.SelectedItem is not null;
        if (GroupList.SelectedItem is not Choice<FileKind> row) return;

        NameBox.Text = row.Value.Name;
        TypesBox.Text = string.Join(' ', row.Value.Extensions);
    }

    private void OnAddOrUpdate(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var types = TypesBox.Text
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => (t.StartsWith('.') ? t : "." + t).ToLowerInvariant())
            .Distinct()
            .ToList();

        var problem =
            name.Length == 0 ? "Give the group a name."
            : _takenNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? $"Windows already has a group called {name}. Choose another name."
            : types.Count == 0 ? "List at least one file type, such as .psd."
            : types.FirstOrDefault(t => !SampleFiles.IsValidExtension(t)) is { } bad ? $"{bad} is not a file type."
            : null;
        ProblemText.Text = problem ?? "";
        ProblemText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (problem is not null) return;

        _groups.RemoveAll(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        _groups.Add(new FileKind { Name = name, Extensions = types });
        ShowGroups(selected: name);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not Choice<FileKind> row) return;

        _groups.Remove(row.Value);
        NameBox.Text = "";
        TypesBox.Text = "";
        ShowGroups(selected: null);
    }

    private void OnDone(object sender, RoutedEventArgs e)
    {
        Result = _groups;
        DialogResult = true;
    }
}
