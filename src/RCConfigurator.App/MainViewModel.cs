using System.Collections.ObjectModel;
using System.Windows.Input;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>Something the user can right-click.</summary>
public sealed record Target(string Label, ScopeKind Kind, bool Several = false);

/// <summary>Which files the "A file" target looks at: one type, a group of types, or every type on the PC.</summary>
public sealed record FileChoice(string Label, FileKind? Kind = null, bool EveryType = false)
{
    public bool IsOneType => Kind is null && !EveryType;
}

public sealed class MainViewModel : Observable
{
    private const int ExtensionTypingDelayMs = 400;

    private Profile _profile = ProfileStore.Load();
    private MenuSnapshot? _snapshot;
    private Scope _context = Scope.Everywhere;
    private int _refreshVersion;
    private CancellationTokenSource? _extensionDelay;

    // New changes start with the scope the user last picked; "everything" until they pick one.
    private int _preferredSpecificity;

    private Target _selectedTarget;
    private string _extension = ".txt";
    private bool _includeShiftItems;
    private MenuItemViewModel? _selectedItem;
    private NewItem? _selectedNewItem;
    private IReadOnlyList<Choice<Scope>> _scopeOptions = [];
    private string _status = "";
    private string _engineStatus = "";
    private bool _isConnected;
    private bool _isBusy;
    private bool _isChangingExplorer;

    private readonly MenuScanner _scanner = new();
    private readonly IReadOnlyList<string> _allTypes = WindowsFileTypes.AllExtensions();
    private FileChoice _selectedFileChoice;

    // Set while a group of file types is shown: which types each item was seen on, and how many were read.
    private IReadOnlyDictionary<MenuEntry, IReadOnlyList<string>>? _seenOn;
    private int _typeCount = 1;

    public MainViewModel()
    {
        _selectedTarget = Targets[0];

        // Windows' own groups are refreshed every time, so they follow what is installed.
        foreach (var kind in WindowsFileTypes.BuiltInKinds()) _profile = _profile.WithKind(kind);
        RebuildFileChoices();
        _selectedFileChoice = FileChoices[0];
        ManageGroupsCommand = new RelayCommand(ManageGroups);

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(fresh: true));
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => !_isChangingExplorer);
        DisconnectCommand = new RelayCommand(() => _ = DisconnectAsync(), () => !_isChangingExplorer);
        ResetCommand = new RelayCommand(() => SelectedItem?.Reset(), () => SelectedItem?.HasChanges == true);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedItem?.CanBeMoved == true);
        MoveDownCommand = new RelayCommand(() => Move(+1), () => SelectedItem?.CanBeMoved == true);
        AddItemCommand = new RelayCommand(() => EditNewItem(existing: null));
        EditItemCommand = new RelayCommand(() => EditNewItem(SelectedNewItem), () => SelectedNewItem is not null);
        RemoveItemCommand = new RelayCommand(RemoveNewItem, () => SelectedNewItem is not null);
        foreach (var item in _profile.Items) NewItems.Add(item);
        UpdateEngineStatus();
    }

    /// <summary>
    /// Shows the dialog for adding or editing an item and returns the result, or null if the user
    /// cancelled. Set by the window.
    /// </summary>
    public Func<NewItem?, IReadOnlyList<string>, IReadOnlyList<FileKind>, NewItem?>? AskForNewItem { get; set; }

    /// <summary>
    /// Shows the dialog for the user's own groups of file types. It is given the current groups
    /// and the names already taken, and returns the new list, or null if the user cancelled.
    /// Set by the window.
    /// </summary>
    public Func<IReadOnlyList<FileKind>, IReadOnlyList<string>, IReadOnlyList<FileKind>?>? AskForGroups { get; set; }

    public ObservableCollection<FileChoice> FileChoices { get; } = [];

    public ICommand ManageGroupsCommand { get; }

    public FileChoice SelectedFileChoice
    {
        get => _selectedFileChoice;
        set
        {
            if (value is null || !Set(ref _selectedFileChoice, value)) return;
            Raise(nameof(ShowExtension));
            _ = RefreshAsync();
        }
    }

    /// <summary>Groups are offered for a single file; several files selected together are always one type.</summary>
    public bool ShowFileChoices => _selectedTarget is { Kind: ScopeKind.File, Several: false };

    public bool ShowExtension => IsFileTarget && (_selectedTarget.Several || _selectedFileChoice.IsOneType);

    public IReadOnlyList<Target> Targets { get; } =
    [
        new("A file", ScopeKind.File),
        new("Several files", ScopeKind.File, Several: true),
        new("A folder", ScopeKind.Folder),
        new("Several folders", ScopeKind.Folder, Several: true),
        new("A drive", ScopeKind.Drive),
        new("Empty space in a folder", ScopeKind.FolderBackground),
        new("Empty space on the desktop", ScopeKind.Desktop),
    ];

    public IReadOnlyList<string> ExtensionPresets { get; } = [".txt", ".png", ".jpg", ".pdf", ".zip", ".mp3", ".mp4", ".docx"];

    public IReadOnlyList<Choice<Placement>> PlacementOptions { get; } =
    [
        new("Where Windows puts it", Placement.Default),
        new("Top of the menu", Placement.Top),
        new("Bottom of the menu", Placement.Bottom),
        new("Where I moved it", Placement.Custom),
    ];

    public ObservableCollection<MenuItemViewModel> Items { get; } = [];

    public ObservableCollection<PreviewItem> Preview { get; } = [];

    public ObservableCollection<string> Groups { get; } = [];

    public ObservableCollection<NewItem> NewItems { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand AddItemCommand { get; }

    public ICommand EditItemCommand { get; }

    public ICommand RemoveItemCommand { get; }

    public Target SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is null || !Set(ref _selectedTarget, value)) return;
            Raise(nameof(IsFileTarget));
            Raise(nameof(ShowFileChoices));
            Raise(nameof(ShowExtension));
            _ = RefreshAsync();
        }
    }

    public bool IsFileTarget => _selectedTarget.Kind == ScopeKind.File;

    public string Extension
    {
        get => _extension;
        set
        {
            if (!Set(ref _extension, (value ?? "").Trim().ToLowerInvariant())) return;
            _ = RefreshAfterTypingAsync();
        }
    }

    public bool IncludeShiftItems
    {
        get => _includeShiftItems;
        set
        {
            if (Set(ref _includeShiftItems, value)) _ = RefreshAsync();
        }
    }

    public MenuItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!Set(ref _selectedItem, value is { IsSeparator: true } ? null : value)) return;
            Raise(nameof(HasSelection));
            Raise(nameof(HasNoSelection));
        }
    }

    public bool HasSelection => _selectedItem is not null;

    public bool HasNoSelection => _selectedItem is null;

    public NewItem? SelectedNewItem
    {
        get => _selectedNewItem;
        set => Set(ref _selectedNewItem, value);
    }

    public IReadOnlyList<Choice<Scope>> ScopeOptions
    {
        get => _scopeOptions;
        private set => Set(ref _scopeOptions, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set => Set(ref _engineStatus, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => Set(ref _isConnected, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string ApplyHint => "Saves your rules for Nilesoft Shell and restarts Explorer so they take effect. Open folder windows will close.";

    /// <summary>The engine config for the current rules, as it would be written by Apply.</summary>
    public string BuildConfig() => Engine.Find().BuildConfig(_profile);

    /// <summary>Switches the file choice to the first entry whose label starts with the given text, and loads it.</summary>
    public Task ShowFileChoiceAsync(string labelStart)
    {
        if (FileChoices.FirstOrDefault(c => c.Label.StartsWith(labelStart, StringComparison.OrdinalIgnoreCase)) is not { } choice)
        {
            return Task.CompletedTask;
        }

        _selectedFileChoice = choice;
        Raise(nameof(SelectedFileChoice));
        Raise(nameof(ShowExtension));
        return RefreshAsync();
    }

    /// <param name="fresh">True to read menus again even if they were already read this session.</param>
    public async Task RefreshAsync(bool fresh = false)
    {
        var version = ++_refreshVersion;
        var target = _selectedTarget;
        var extension = _extension;
        var choice = ShowFileChoices ? _selectedFileChoice : FileChoices[0];

        if (target.Kind == ScopeKind.File && choice.IsOneType && !SampleFiles.IsValidExtension(extension))
        {
            Status = "Type a file extension such as .txt to see that file type's menu.";
            return;
        }

        IsBusy = true;
        Status = "Reading the menu from Windows…";
        try
        {
            MenuSnapshot snapshot;
            IReadOnlyDictionary<MenuEntry, IReadOnlyList<string>>? seenOn = null;
            var typeCount = 1;
            if (choice.IsOneType)
            {
                var arguments = ProbeArguments(target, extension);
                if (_includeShiftItems) arguments.Insert(0, "--extended");
                snapshot = await ProbeRunner.RunAsync(arguments);
            }
            else
            {
                // A group is shown as one menu holding every item found on any of its types.
                // Reading every type on the PC is a lot of menus, so that one skips submenus.
                var types = choice.EveryType ? _allTypes : choice.Kind!.Extensions;
                var progress = new Progress<int>(done =>
                {
                    if (version == _refreshVersion) Status = $"Reading menus from Windows: {done} of {types.Count} file types…";
                });
                var menus = await _scanner.ScanAsync(types, _includeShiftItems, submenus: !choice.EveryType, fresh, progress);
                if (version != _refreshVersion) return;
                if (menus.Count == 0) throw new InvalidOperationException("None of these file types has a menu that could be read.");

                var combined = MenuMerge.Combine(menus);
                snapshot = choice.EveryType ? MostCommonFirst(combined) : combined.Menu;
                seenOn = combined.SeenOn;
                typeCount = combined.TypeCount;
            }

            if (version != _refreshVersion) return;

            _snapshot = snapshot;
            _seenOn = seenOn;
            _typeCount = typeCount;
            var count = target.Several ? SelectionCount.Several : SelectionCount.One;
            _context =
                choice.Kind is { } kind ? Scope.ForGroup(kind.Name, count)
                : choice.EveryType ? Scope.ForFile(null, count)
                : new Scope(target.Kind, target.Kind == ScopeKind.File ? extension : null, count);
            ScopeOptions = ScopeOptionsFor(target, extension, choice);
            RebuildItems(target.Kind is ScopeKind.File or ScopeKind.Folder ? SampleFiles.FileStem : "");
            RebuildPreview();
            Status = choice.IsOneType
                ? $"Windows shows {CountItems(snapshot.Items)} items here. Pick one to change it."
                : $"{CountItems(snapshot.Items)} different items across {typeCount} file types. Items that are not on every type say how many they are on.";
        }
        catch (Exception e)
        {
            if (version != _refreshVersion) return;
            _snapshot = null;
            Items.Clear();
            Preview.Clear();
            SelectedItem = null;
            Status = $"Could not read the menu. {e.Message}";
        }
        finally
        {
            if (version == _refreshVersion) IsBusy = false;
        }
    }

    private async Task RefreshAfterTypingAsync()
    {
        _extensionDelay?.Cancel();
        var delay = _extensionDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(ExtensionTypingDelayMs, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync();
    }

    private static List<string> ProbeArguments(Target target, string extension) => (target.Kind, target.Several) switch
    {
        (ScopeKind.File, false) => [SampleFiles.FileWithExtension(extension)],
        (ScopeKind.File, true) => [SampleFiles.FileWithExtension(extension), SampleFiles.FileWithExtension(extension, second: true)],
        (ScopeKind.Folder, false) => [SampleFiles.SubFolder()],
        (ScopeKind.Folder, true) => [SampleFiles.SubFolder(), SampleFiles.SubFolder(second: true)],
        (ScopeKind.Drive, _) => [SampleFiles.SystemDrive],
        (ScopeKind.FolderBackground, _) => ["--background", SampleFiles.Folder],
        _ => ["--desktop"],
    };

    /// <summary>The whole combined menu as a flat list with the items found on the most file types first.</summary>
    private static MenuSnapshot MostCommonFirst(CombinedMenu combined) => combined.Menu with
    {
        Items = combined.Menu.Items
            .Where(i => !i.IsSeparator)
            .OrderByDescending(i => combined.SeenOn[i].Count)
            .ThenBy(i => i.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList(),
    };

    private IReadOnlyList<Choice<Scope>> ScopeOptionsFor(Target target, string extension, FileChoice choice)
    {
        var everything = new Choice<Scope>("Everything I right-click", Scope.Everywhere);
        var allFiles = new Choice<Scope>("All files", Scope.ForFile(null));
        static Choice<Scope> ForGroup(FileKind kind) => new($"Every type in {kind.Name}", Scope.ForGroup(kind.Name));

        if (choice.EveryType) return [allFiles, everything];
        if (choice.Kind is { } shown) return [ForGroup(shown), allFiles, everything];

        return (target.Kind, target.Several) switch
        {
            (ScopeKind.File, false) =>
            [
                new($"Only {extension} files", Scope.ForFile(extension)),
                .. _profile.Kinds.Where(k => k.Contains(extension)).Select(ForGroup),
                allFiles,
                everything,
            ],
            (ScopeKind.File, true) =>
            [
                new($"Only several {extension} files", Scope.ForFile(extension, SelectionCount.Several)),
                new("Several files of any type", Scope.ForFile(null, SelectionCount.Several)),
                new("All files, one or several", Scope.ForFile(null)),
                everything,
            ],
            (ScopeKind.Folder, false) => [new("Only folders", new Scope(ScopeKind.Folder)), everything],
            (ScopeKind.Folder, true) =>
            [
                new("Only several folders", new Scope(ScopeKind.Folder, Count: SelectionCount.Several)),
                new("All folders, one or several", new Scope(ScopeKind.Folder)),
                everything,
            ],
            (ScopeKind.Drive, _) => [new("Only drives", new Scope(ScopeKind.Drive)), everything],
            (ScopeKind.FolderBackground, _) => [new("Only empty space in folders", new Scope(ScopeKind.FolderBackground)), everything],
            _ => [new("Only the desktop", new Scope(ScopeKind.Desktop)), everything],
        };
    }

    private void RebuildItems(string sampleName)
    {
        var offered = _scopeOptions.Select(o => o.Value).ToList();
        // A group is opened in order to edit that group, so changes there start scoped to it.
        var defaultScope = _seenOn is not null ? offered[0] : offered.MinBy(s => Math.Abs(s.Specificity - _preferredSpecificity))!;

        Items.Clear();
        SelectedItem = null;
        foreach (var item in Build(_snapshot!.Items, parentPath: "")) Items.Add(item);
        RefreshGroups();

        List<MenuItemViewModel> Build(IReadOnlyList<MenuEntry> entries, string parentPath)
        {
            var items = new List<MenuItemViewModel>(entries.Count);
            foreach (var entry in entries)
            {
                var seenOn = _seenOn is not null && _seenOn.TryGetValue(entry, out var types) ? types : null;
                var item = new MenuItemViewModel(entry, parentPath, sampleName, defaultScope, OnItemChanged, NeighbourOf, seenOn, _typeCount);
                item.Load(_profile, offered);
                if (entry.IsSubmenu)
                {
                    var name = EngineName.Path([item.FullName]);
                    item.Children.AddRange(Build(entry.Children, parentPath.Length == 0 ? name : $"{parentPath}/{name}"));
                }

                items.Add(item);
            }

            return items;
        }
    }

    private void OnItemChanged(MenuItemViewModel item, Scope previousScope)
    {
        var rule = item.ToRule();
        if (previousScope != rule.Scope)
        {
            // The change now belongs to a different scope, so drop it from the old one.
            _profile = _profile.With(new ItemRule { Name = rule.Name, Match = rule.Match, ParentPath = rule.ParentPath, Scope = previousScope });
            _preferredSpecificity = rule.Scope.Specificity;
        }

        _profile = _profile.With(rule);
        Save();
    }

    private void Save()
    {
        ProfileStore.Save(_profile);
        RebuildPreview();
        RefreshGroups();
        CommandManager.InvalidateRequerySuggested();
        Status = "Saved. Choose Apply to Explorer when you are ready to use it.";
    }

    /// <summary>Moves the selected item one row up or down within its menu.</summary>
    private void Move(int direction)
    {
        if (SelectedItem is not { CanBeMoved: true } item || _snapshot is null) return;

        var (row, menu) = MenuPreview.RowOf(MenuPreview.Build(_snapshot, _context, _profile), item.Entry);
        if (row < 0) return;

        var target = row + direction;
        if (target < 0 || target >= menu.Count)
        {
            Status = direction < 0 ? "It is already at the top." : "It is already at the bottom.";
            return;
        }

        // The engine places moved items one at a time, so the obvious neighbour does not always
        // give the row wanted. Try sitting above and below every other item and keep whichever
        // lands closest, preferring a neighbour near the target.
        var rule = item.ToRule();
        var best = (Anchor: "", After: false, Row: row, Score: int.MaxValue);
        for (var i = 0; i < menu.Count; i++)
        {
            if (!CanAnchorTo(menu[i], item)) continue;

            foreach (var after in new[] { true, false })
            {
                var candidate = rule with { Placement = Placement.Custom, Anchor = menu[i].EngineTitle, AfterAnchor = after };
                var landed = MenuPreview.RowOf(MenuPreview.Build(_snapshot, _context, _profile.With(candidate)), item.Entry).Row;
                if (landed < 0) continue;

                var score = Math.Abs(landed - target) * 1000 + Math.Abs(i - target);
                if (score < best.Score) best = (menu[i].EngineTitle, after, landed, score);
            }
        }

        if (best.Score == int.MaxValue || best.Row == row)
        {
            Status = "It cannot move any further that way.";
            return;
        }

        item.MoveNextTo(best.Anchor, best.After);
    }

    /// <summary>An item the given one currently sits next to, preferring the one above it.</summary>
    private (string Anchor, bool After)? NeighbourOf(MenuItemViewModel item)
    {
        if (_snapshot is null) return null;

        var (row, menu) = MenuPreview.RowOf(MenuPreview.Build(_snapshot, _context, _profile), item.Entry);
        if (row < 0) return null;

        for (var i = row - 1; i >= 0 && !menu[i].IsSeparator; i--)
        {
            if (CanAnchorTo(menu[i], item)) return (menu[i].EngineTitle, true);
        }

        for (var i = row + 1; i < menu.Count && !menu[i].IsSeparator; i++)
        {
            if (CanAnchorTo(menu[i], item)) return (menu[i].EngineTitle, false);
        }

        return null;
    }

    // A neighbour is found by its title, and the engine reads | in a title pattern as "or".
    private static bool CanAnchorTo(PreviewItem candidate, MenuItemViewModel item) =>
        !candidate.IsSeparator
        && !ReferenceEquals(candidate.Source, item.Entry)
        && candidate.EngineTitle.Length > 0
        && !candidate.EngineTitle.Contains('|');

    private void RebuildFileChoices()
    {
        FileChoices.Clear();
        FileChoices.Add(new FileChoice("One file type"));
        foreach (var kind in _profile.Kinds.OrderByDescending(k => k.IsBuiltIn).ThenBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            FileChoices.Add(new FileChoice($"{kind.Name} ({kind.Extensions.Count} types)", kind));
        }

        FileChoices.Add(new FileChoice($"Every file type ({_allTypes.Count})", EveryType: true));
    }

    private void ManageGroups()
    {
        var mine = _profile.Kinds.Where(k => !k.IsBuiltIn).ToList();
        var taken = _profile.Kinds.Where(k => k.IsBuiltIn).Select(k => k.Name).ToList();
        if (AskForGroups?.Invoke(mine, taken) is not { } updated) return;

        foreach (var removed in mine.Where(k => !updated.Any(u => string.Equals(u.Name, k.Name, StringComparison.OrdinalIgnoreCase))))
        {
            _profile = _profile.WithoutKind(removed.Name);
        }

        foreach (var kind in updated) _profile = _profile.WithKind(kind);

        RebuildFileChoicesKeepingSelection();
        Save();
        _ = RefreshAsync();
    }

    /// <summary>Rebuilds the list of file choices and puts the selection back on the same entry if it is still there.</summary>
    private void RebuildFileChoicesKeepingSelection()
    {
        var selected = _selectedFileChoice;
        RebuildFileChoices();
        _selectedFileChoice = FileChoices.FirstOrDefault(c => c.EveryType == selected.EveryType && c.Kind?.Name == selected.Kind?.Name) ?? FileChoices[0];
        Raise(nameof(SelectedFileChoice));
        Raise(nameof(ShowExtension));
    }

    /// <summary>
    /// Adds the file types Windows does not label to the groups, using two public lists. Called
    /// once after the window opens. Without the lists the groups hold only what Windows knows.
    /// </summary>
    /// <param name="offline">True to use only copies of the lists already on this PC.</param>
    public async Task LoadOnlineFileTypesAsync(bool offline)
    {
        if (await OnlineFileTypes.LoadAsync(offline) is not { } lists)
        {
            if (!offline) Status = "The public file-type lists could not be reached, so the groups hold only the types Windows labels.";
            return;
        }

        var showing = _selectedFileChoice.Kind;
        foreach (var kind in FileTypeCatalogue.Extend(WindowsFileTypes.BuiltInKinds(), _allTypes, lists.MediaTypes, lists.LanguageTypes))
        {
            _profile = _profile.WithKind(kind);
        }

        RebuildFileChoicesKeepingSelection();

        // The group on screen may have gained types.
        if (showing is not null && _selectedFileChoice.Kind?.Extensions.Count != showing.Extensions.Count) await RefreshAsync();
    }

    private void EditNewItem(NewItem? existing)
    {
        if (AskForNewItem?.Invoke(existing, Groups.ToList(), _profile.Kinds) is not { } item) return;

        _profile = _profile.WithItem(item, replacing: existing);
        RefreshNewItems(select: item);
        Save();
    }

    private void RemoveNewItem()
    {
        if (SelectedNewItem is not { } item) return;

        _profile = _profile.WithoutItem(item);
        RefreshNewItems(select: null);
        Save();
    }

    private void RefreshNewItems(NewItem? select)
    {
        NewItems.Clear();
        foreach (var item in _profile.Items) NewItems.Add(item);
        SelectedNewItem = select;
    }

    private void RebuildPreview()
    {
        Preview.Clear();
        if (_snapshot is null) return;
        foreach (var item in MenuPreview.Build(_snapshot, _context, _profile)) Preview.Add(item);
    }

    private void RefreshGroups()
    {
        var names = _profile.Groups.Order(StringComparer.OrdinalIgnoreCase).ToList();

        // Change the list in place: clearing it would also clear the text of the drop-down showing it.
        foreach (var gone in Groups.Except(names, StringComparer.OrdinalIgnoreCase).ToList()) Groups.Remove(gone);
        foreach (var added in names.Except(Groups, StringComparer.OrdinalIgnoreCase).ToList()) Groups.Add(added);
    }

    private const string ExplorerDidNotReturn = "Explorer did not come back by itself. Press Ctrl+Shift+Esc, choose Run new task and type explorer.";

    private async Task ApplyAsync()
    {
        try
        {
            var engine = Engine.Find();
            engine.WriteConfig(_profile);
            if (!engine.IsInstalled)
            {
                Status = "Saved your rules, but Nilesoft Shell is not installed, so Explorer's menus will not change yet.";
                return;
            }

            Engine.Connect();
            await ReloadEngineAsync(engine, "Applied. Explorer was restarted and is using your new menus.");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Status = $"Could not apply. {e.Message}";
        }

        UpdateEngineStatus();
    }

    private async Task DisconnectAsync()
    {
        Engine.Disconnect();
        await ReloadEngineAsync(Engine.Find(), "Nilesoft Shell is back on its own settings.");
        UpdateEngineStatus();
    }

    /// <summary>
    /// Makes the engine read its config again. It only does that when Explorer starts, so this
    /// restarts Explorer, after registering the engine first if Windows has lost track of it.
    /// </summary>
    private async Task ReloadEngineAsync(Engine engine, string doneMessage)
    {
        _isChangingExplorer = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (engine.IsInstalled && !Engine.IsRegistered)
            {
                Status = "Registering Nilesoft Shell with Windows. Windows will ask for permission.";
                if (!await engine.RegisterAsync())
                {
                    Status = "Saved, but Nilesoft Shell is not registered with Windows because permission was not given.";
                    return;
                }

                // Registering restarts Explorer.
                Status = doneMessage;
                return;
            }

            Status = "Restarting Explorer to load the change…";
            Status = await Task.Run(ExplorerRestart.Restart) ? doneMessage : ExplorerDidNotReturn;
        }
        finally
        {
            _isChangingExplorer = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Looks for a newer Nilesoft Shell and installs it. Called once after the window opens.</summary>
    public async Task UpdateEngineIfNeededAsync()
    {
        if (!Engine.Find().IsInstalled || !await EngineInstaller.IsUpdateAvailableAsync()) return;

        _isChangingExplorer = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            Status = "A newer Nilesoft Shell is available. Updating it now; follow the prompts in the window that opens.";
            await EngineInstaller.UpgradeAsync();

            // The installer registers the engine and restarts Explorer. Make sure both happened.
            var engine = Engine.Find();
            if (engine.IsInstalled && !Engine.IsRegistered) await engine.RegisterAsync();
            else if (engine.IsLoaded() == false) await Task.Run(ExplorerRestart.Restart);

            Status = $"Nilesoft Shell is now version {Engine.Find().Version}.";
        }
        finally
        {
            _isChangingExplorer = false;
            CommandManager.InvalidateRequerySuggested();
            UpdateEngineStatus();
        }
    }

    private void UpdateEngineStatus()
    {
        var engine = Engine.Find();
        IsConnected = engine.IsInstalled && Engine.IsConnected;
        EngineStatus = !engine.IsInstalled
            ? "Nilesoft Shell is not installed"
            : $"Nilesoft Shell {engine.Version} · {(IsConnected ? "using your rules" : "not using your rules yet")}";
        Raise(nameof(ApplyHint));
    }

    private static int CountItems(IReadOnlyList<MenuEntry> items) =>
        items.Count(i => !i.IsSeparator) + items.Sum(i => CountItems(i.Children));
}
