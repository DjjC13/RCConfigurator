using System.Collections.ObjectModel;
using System.Windows.Input;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>Something the user can right-click.</summary>
public sealed record Target(string Label, ScopeKind Kind);

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
    private IReadOnlyList<Choice<Scope>> _scopeOptions = [];
    private string _status = "";
    private string _engineStatus = "";
    private bool _isConnected;
    private bool _isBusy;

    public MainViewModel()
    {
        _selectedTarget = Targets[0];
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ApplyCommand = new RelayCommand(Apply);
        DisconnectCommand = new RelayCommand(Disconnect);
        ResetCommand = new RelayCommand(() => SelectedItem?.Reset(), () => SelectedItem?.HasChanges == true);
        UpdateEngineStatus();
    }

    public IReadOnlyList<Target> Targets { get; } =
    [
        new("A file", ScopeKind.File),
        new("A folder", ScopeKind.Folder),
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
    ];

    public ObservableCollection<MenuItemViewModel> Items { get; } = [];

    public ObservableCollection<PreviewItem> Preview { get; } = [];

    public ObservableCollection<string> Groups { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand ResetCommand { get; }

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
            var arguments = ProbeArguments(target.Kind, extension);
            if (_includeShiftItems) arguments.Insert(0, "--extended");
            var snapshot = await ProbeRunner.RunAsync(arguments);
            if (version != _refreshVersion) return;

            _snapshot = snapshot;
            _context = target.Kind == ScopeKind.File ? Scope.ForFile(extension) : new Scope(target.Kind);
            ScopeOptions = ScopeOptionsFor(target, extension);
            RebuildItems(SampleNameFor(target.Kind));
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

    private static List<string> ProbeArguments(ScopeKind kind, string extension) => kind switch
    {
        ScopeKind.File => [SampleFiles.FileWithExtension(extension)],
        ScopeKind.Folder => [SampleFiles.SubFolder()],
        ScopeKind.Drive => [SampleFiles.SystemDrive],
        ScopeKind.FolderBackground => ["--background", SampleFiles.Folder],
        _ => ["--desktop"],
    };

    private static string SampleNameFor(ScopeKind kind) => kind switch
    {
        ScopeKind.File => SampleFiles.FileStem,
        ScopeKind.Folder => SampleFiles.FolderName,
        _ => "",
    };

    private static IReadOnlyList<Choice<Scope>> ScopeOptionsFor(Target target, string extension)
    {
        var everything = new Choice<Scope>("Everything I right-click", Scope.Everywhere);
        return target.Kind switch
        {
            ScopeKind.File =>
            [
                new($"Only {extension} files", Scope.ForFile(extension)),
                new("All files", Scope.ForFile(null)),
                everything,
            ],
            ScopeKind.Folder => [new("Only folders", new Scope(ScopeKind.Folder)), everything],
            ScopeKind.Drive => [new("Only drives", new Scope(ScopeKind.Drive)), everything],
            ScopeKind.FolderBackground => [new("Only empty space in folders", new Scope(ScopeKind.FolderBackground)), everything],
            _ => [new("Only the desktop", new Scope(ScopeKind.Desktop)), everything],
        };
    }

    private void RebuildItems(string sampleName)
    {
        var offered = _scopeOptions.Select(o => o.Value).ToList();
        var defaultScope = offered.MinBy(s => Math.Abs(Profile.Specificity(new ItemRule { Name = "", Scope = s }) - _preferredSpecificity))!;

        Items.Clear();
        SelectedItem = null;
        foreach (var item in Build(_snapshot!.Items, parentPath: "")) Items.Add(item);
        RefreshGroups();

        List<MenuItemViewModel> Build(IReadOnlyList<MenuEntry> entries, string parentPath)
        {
            var items = new List<MenuItemViewModel>(entries.Count);
            foreach (var entry in entries)
            {
                var item = new MenuItemViewModel(entry, parentPath, sampleName, defaultScope, OnItemChanged);
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
            _profile = _profile.With(rule with { Scope = previousScope, Hidden = false, NewTitle = null, Group = null, Placement = Placement.Default });
            _preferredSpecificity = Profile.Specificity(rule);
        }

        _profile = _profile.With(rule);
        ProfileStore.Save(_profile);
        RebuildPreview();
        RefreshGroups();
        CommandManager.InvalidateRequerySuggested();
        Status = "Saved. Choose Apply to Explorer when you are ready to use it.";
    }

    private void RebuildPreview()
    {
        Preview.Clear();
        if (_snapshot is null) return;
        foreach (var item in MenuPreview.Build(_snapshot, _context, _profile)) Preview.Add(item);
    }

    private void RefreshGroups()
    {
        var names = _profile.Rules
            .Select(r => r.Group)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Change the list in place: clearing it would also clear the text of the drop-down showing it.
        foreach (var gone in Groups.Except(names, StringComparer.OrdinalIgnoreCase).ToList()) Groups.Remove(gone);
        foreach (var added in names.Except(Groups, StringComparer.OrdinalIgnoreCase).ToList()) Groups.Add(added);
    }

    /// <summary>The engine config for the current rules, as it would be written by Apply.</summary>
    public string BuildConfig() => Engine.Find().BuildConfig(_profile);

    private void Apply()
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
            Status = "Applied. Hold Ctrl and right-click once in Explorer to load the new menus.";
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Status = $"Could not apply. {e.Message}";
        }

        UpdateEngineStatus();
    }

    private void Disconnect()
    {
        Engine.Disconnect();
        UpdateEngineStatus();
        Status = "Nilesoft Shell is back on its own settings. Hold Ctrl and right-click once in Explorer to reload.";
    }

    private void UpdateEngineStatus()
    {
        var engine = Engine.Find();
        IsConnected = engine.IsInstalled && Engine.IsConnected;
        EngineStatus = !engine.IsInstalled
            ? "Nilesoft Shell is not installed"
            : $"Nilesoft Shell {engine.Version} · {(IsConnected ? "using your rules" : "not using your rules yet")}";
    }

    private static int CountItems(IReadOnlyList<MenuEntry> items) =>
        items.Count(i => !i.IsSeparator) + items.Sum(i => CountItems(i.Children));
}
