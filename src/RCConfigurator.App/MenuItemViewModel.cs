using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>One row of the menu Windows built, together with the user's changes to it.</summary>
public sealed class MenuItemViewModel : Observable
{
    // A prefix shorter than this would match too many unrelated items.
    private const int MinimumPrefixLength = 3;

    private readonly Action<MenuItemViewModel, Scope> _changed;
    private bool _loading;
    private bool _isHidden;
    private string _newTitle = "";
    private string _group = "";
    private Placement _placement;
    private Scope _scope;

    public MenuItemViewModel(MenuEntry entry, string parentPath, string sampleName, Scope defaultScope, Action<MenuItemViewModel, Scope> changed)
    {
        Entry = entry;
        ParentPath = parentPath;
        _scope = defaultScope;
        _changed = changed;

        // Some titles include the name of what was clicked, as in Add to "sample.zip". Those are
        // matched by the part before the name so the rule still works on other files.
        var name = EngineName.From(entry.RawText);
        var sampleAt = sampleName.Length == 0 ? -1 : name.IndexOf(sampleName, StringComparison.OrdinalIgnoreCase);
        if (sampleAt >= MinimumPrefixLength)
        {
            MatchName = name[..sampleAt];
            Match = MatchKind.StartsWith;
        }
        else
        {
            MatchName = name;
            Match = MatchKind.Exact;
        }

        FullName = name;
    }

    public MenuEntry Entry { get; }

    public string ParentPath { get; }

    public string FullName { get; }

    public string MatchName { get; }

    public MatchKind Match { get; }

    public List<MenuItemViewModel> Children { get; } = [];

    public bool IsSeparator => Entry.IsSeparator;

    public string OriginalTitle => Entry.Text ?? "(no text)";

    public string Title => _newTitle.Length > 0 && !_isHidden ? _newTitle : OriginalTitle;

    public string? Verb => Entry.Verb;

    public bool IsShown => !_isHidden;

    /// <summary>A short description of the changes, shown beside the title.</summary>
    public string Note
    {
        get
        {
            if (_isHidden) return "hidden";

            var notes = new List<string>();
            if (_newTitle.Length > 0) notes.Add($"was {OriginalTitle}");
            if (_group.Length > 0) notes.Add($"moved into {_group}");
            if (_placement == Placement.Top) notes.Add("pinned to top");
            if (_placement == Placement.Bottom) notes.Add("pinned to bottom");
            return string.Join(", ", notes);
        }
    }

    public bool IsHidden
    {
        get => _isHidden;
        set => Change(ref _isHidden, value);
    }

    public string NewTitle
    {
        get => _newTitle;
        set => Change(ref _newTitle, value ?? "");
    }

    public string Group
    {
        get => _group;
        set => Change(ref _group, (value ?? "").Replace("/", "").Trim());
    }

    public Placement Placement
    {
        get => _placement;
        set => Change(ref _placement, value);
    }

    public Scope Scope
    {
        get => _scope;
        set
        {
            // A drop-down clears its selection while its list is being replaced.
            if (value is null) return;
            Change(ref _scope, value);
        }
    }

    public bool HasChanges => !ToRule().HasNoEffect;

    public ItemRule ToRule() => new()
    {
        Name = MatchName,
        Match = Match,
        ParentPath = ParentPath,
        Scope = _scope,
        Hidden = _isHidden,
        NewTitle = _newTitle.Trim().Length > 0 ? _newTitle.Trim() : null,
        Group = _group.Length > 0 ? _group : null,
        Placement = _placement,
    };

    /// <summary>Shows the rule the profile already holds for this item, picking the narrowest of the offered scopes.</summary>
    public void Load(Profile profile, IEnumerable<Scope> offeredScopes)
    {
        var rule = profile.Rules
            .Where(r => offeredScopes.Contains(r.Scope) && r.Match == Match
                && string.Equals(r.Name, MatchName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ParentPath, ParentPath, StringComparison.OrdinalIgnoreCase))
            .MaxBy(Profile.Specificity);
        if (rule is null) return;

        _loading = true;
        Scope = rule.Scope;
        IsHidden = rule.Hidden;
        NewTitle = rule.NewTitle ?? "";
        Group = rule.Group ?? "";
        Placement = rule.Placement;
        _loading = false;
    }

    public void Reset()
    {
        var scope = _scope;
        _loading = true;
        IsHidden = false;
        NewTitle = "";
        Group = "";
        Placement = Placement.Default;
        _loading = false;
        _changed(this, scope);
    }

    private void Change<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var previousScope = _scope;
        if (!Set(ref field, value, name)) return;

        Raise(nameof(Title));
        Raise(nameof(Note));
        Raise(nameof(IsShown));
        Raise(nameof(HasChanges));
        if (!_loading) _changed(this, previousScope);
    }
}
