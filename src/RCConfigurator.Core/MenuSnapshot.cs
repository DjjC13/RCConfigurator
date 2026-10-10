namespace RCConfigurator.Core;

/// <summary>What was right-clicked to produce a menu.</summary>
public enum TargetKind
{
    /// <summary>One or more files, folders or drives.</summary>
    Items,

    /// <summary>The empty area inside a folder.</summary>
    FolderBackground,

    /// <summary>The empty area of the desktop.</summary>
    Desktop,
}

/// <summary>The menu Windows produced for one right-click, as read by the probe.</summary>
public sealed record MenuSnapshot
{
    public TargetKind Kind { get; init; }

    /// <summary>The selected paths; a single folder for <see cref="TargetKind.FolderBackground"/>, empty for <see cref="TargetKind.Desktop"/>.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>True when Shift-only items were requested.</summary>
    public bool ExtendedVerbs { get; init; }

    public IReadOnlyList<MenuEntry> Items { get; init; } = [];
}

/// <summary>One row of a menu: a command, a submenu or a separator.</summary>
public sealed record MenuEntry
{
    /// <summary>The title as the user sees it, without the accelerator marker or shortcut text.</summary>
    public string? Text { get; init; }

    /// <summary>The title exactly as Windows reported it.</summary>
    public string? RawText { get; init; }

    /// <summary>The language-independent command name, when the item's owner reports one.</summary>
    public string? Verb { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsDisabled { get; init; }

    public bool IsChecked { get; init; }

    /// <summary>True for the item that runs on double-click.</summary>
    public bool IsDefault { get; init; }

    public bool IsSubmenu { get; init; }

    public IReadOnlyList<MenuEntry> Children { get; init; } = [];
}
