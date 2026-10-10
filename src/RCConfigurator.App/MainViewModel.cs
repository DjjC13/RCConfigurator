using System.Collections.ObjectModel;
using System.Windows.Input;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>Something the user can right-click.</summary>
public sealed record Target(string Label, ScopeKind Kind, bool Several = false);

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

    public MainViewModel()
    {
        _selectedTarget = Targets[0];
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
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
    public Func<NewItem?, IReadOnlyList<string>, NewItem?>? AskForNewItem { get; set; }

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

    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var target = _selectedTarget;
        var extension = _extension;

        if (target.Kind == ScopeKind.File && !SampleFiles.IsValidExtension(extension))
        {
            Status = "Type a file extension such as .txt to see that file type's menu.";
            return;
        }

        IsBusy = true;
        Status = "Reading the menu from Windows…";
        try
        {
            var arguments = ProbeArguments(target, extension);
            if (_includeShiftItems) arguments.Insert(0, "--extended");
            var snapshot = await ProbeRunner.RunAsync(arguments);
            if (version != _refreshVersion) return;

            _snapshot = snapshot;
            _context = new Scope(
                target.Kind,
                target.Kind == ScopeKind.File ? extension : null,
                target.Several ? SelectionCount.Several : SelectionCount.One);
            ScopeOptions = ScopeOptionsFor(target, extension);
            RebuildItems(target.Kind is ScopeKind.File or ScopeKind.Folder ? SampleFiles.FileStem : "");
            RebuildPreview();
            Status = $"Windows shows {CountItems(snapshot.Items)} items here. Pick one to change it.";
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

    private static IReadOnlyList<Choice<Scope>> ScopeOptionsFor(Target target, string extension)
    {
        var everything = new Choice<Scope>("Everything I right-click", Scope.Everywhere);
        return (target.Kind, target.Several) switch
        {
            (ScopeKind.File, false) =>
            [
                new($"Only {extension} files", Scope.ForFile(extension)),
                new("All files", Scope.ForFile(null)),
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
        var defaultScope = offered.MinBy(s => Math.Abs(s.Specificity - _preferredSpecificity))!;

        Items.Clear();
        SelectedItem = null;
        foreach (var item in Build(_snapshot!.Items, parentPath: "")) Items.Add(item);
        RefreshGroups();

        List<MenuItemViewModel> Build(IReadOnlyList<MenuEntry> entries, string parentPath)
        {
            var items = new List<MenuItemViewModel>(entries.Count);
            foreach (var entry in entries)
            {
                var item = new MenuItemViewModel(entry, parentPath, sampleName, defaultScope, OnItemChanged, NeighbourOf);
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

    private void EditNewItem(NewItem? existing)
    {
        if (AskForNewItem?.Invoke(existing, Groups.ToList()) is not { } item) return;

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
