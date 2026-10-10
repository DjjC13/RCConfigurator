using System.Text.Json;
using System.Text.Json.Serialization;

namespace RCConfigurator.Core;

public enum ScopeKind
{
    Everywhere,
    File,
    Folder,
    Drive,
    FolderBackground,
    Desktop,
}

/// <summary>How many things are selected when the menu opens.</summary>
public enum SelectionCount
{
    Any,
    One,
    Several,
}

/// <summary>Which right-clicks a rule applies to. The same type also describes one particular right-click.</summary>
/// <param name="Extension">For files only: a lower-case extension such as ".txt", or null for every file.</param>
public sealed record Scope(ScopeKind Kind, string? Extension = null, SelectionCount Count = SelectionCount.Any)
{
    public static Scope Everywhere { get; } = new(ScopeKind.Everywhere);

    public static Scope ForFile(string? extension, SelectionCount count = SelectionCount.Any) =>
        new(ScopeKind.File, string.IsNullOrWhiteSpace(extension) ? null : extension.Trim().ToLowerInvariant(), count);

    /// <summary>True when a rule with this scope applies to a right-click described by <paramref name="context"/>.</summary>
    public bool Covers(Scope context) =>
        (Kind == ScopeKind.Everywhere || (Kind == context.Kind && (Extension is null || Extension == context.Extension)))
        && (Count == SelectionCount.Any || Count == context.Count);

    /// <summary>Higher for narrower scopes. Narrower rules are applied later, so they win.</summary>
    [JsonIgnore]
    public int Specificity =>
        (Kind == ScopeKind.Everywhere ? 0 : 1) + (Extension is null ? 0 : 1) + (Count == SelectionCount.Any ? 0 : 1);
}

public enum MatchKind
{
    /// <summary>The item's whole title.</summary>
    Exact,

    /// <summary>The start of the title, for items whose title includes the selected file's name.</summary>
    StartsWith,
}

public enum Placement
{
    Default,
    Top,
    Bottom,

    /// <summary>Next to another item, given by <see cref="ItemRule.Anchor"/>.</summary>
    Custom,
}

/// <summary>A change to one existing menu item.</summary>
public sealed record ItemRule
{
    /// <summary>The item's title as the engine sees it. See <see cref="EngineName"/>.</summary>
    public required string Name { get; init; }

    public MatchKind Match { get; init; }

    /// <summary>The submenu the item sits in, as an engine path such as "7-zip/crc sha". Empty for the top level.</summary>
    public string ParentPath { get; init; } = "";

    public Scope Scope { get; init; } = Scope.Everywhere;

    public bool Hidden { get; init; }

    public string? NewTitle { get; init; }

    /// <summary>The name of a submenu to move the item into.</summary>
    public string? Group { get; init; }

    public Placement Placement { get; init; }

    /// <summary>
    /// For <see cref="Placement.Custom"/>: the title, as the engine sees it, of the item to sit
    /// next to. Naming a neighbour instead of a row number keeps the item in place when other
    /// changes add or remove rows above it. If no item has this title the engine puts it last.
    /// </summary>
    public string? Anchor { get; init; }

    /// <summary>True to sit just below <see cref="Anchor"/>, false to sit just above it.</summary>
    public bool AfterAnchor { get; init; }

    [JsonIgnore]
    public bool HasNoEffect => !Hidden && NewTitle is null && Group is null && Placement == Placement.Default;

    public bool Targets(string engineName, string parentPath) =>
        string.Equals(ParentPath, parentPath, StringComparison.OrdinalIgnoreCase)
        && (Match == MatchKind.Exact
            ? string.Equals(engineName, Name, StringComparison.OrdinalIgnoreCase)
            : engineName.StartsWith(Name, StringComparison.OrdinalIgnoreCase));

    public bool HasSameTargetAs(ItemRule other) =>
        Match == other.Match
        && Scope == other.Scope
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ParentPath, other.ParentPath, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A menu item the user adds: it runs a program.</summary>
public sealed record NewItem
{
    /// <summary>What <see cref="Arguments"/> uses to stand for the path of whatever was right-clicked.</summary>
    public const string PathPlaceholder = "%1";

    public required string Title { get; init; }

    public required string Program { get; init; }

    public string Arguments { get; init; } = "";

    public Scope Scope { get; init; } = Scope.Everywhere;

    /// <summary>Default, Top or Bottom.</summary>
    public Placement Placement { get; init; }

    /// <summary>The name of a submenu to put the item in.</summary>
    public string? Group { get; init; }
}

/// <summary>Everything the user has configured.</summary>
public sealed record Profile
{
    public IReadOnlyList<ItemRule> Rules { get; init; } = [];

    public IReadOnlyList<NewItem> Items { get; init; } = [];

    /// <summary>Adds the rule, replacing any rule for the same item and scope. A rule that changes nothing is dropped.</summary>
    public Profile With(ItemRule rule)
    {
        var rules = Rules.Where(r => !r.HasSameTargetAs(rule)).ToList();
        if (!rule.HasNoEffect) rules.Add(rule);
        return this with { Rules = rules };
    }

    /// <summary>Adds an item, or replaces <paramref name="replacing"/> with it in the same place.</summary>
    public Profile WithItem(NewItem item, NewItem? replacing = null)
    {
        var items = Items.ToList();
        var at = replacing is null ? -1 : items.IndexOf(replacing);
        if (at >= 0) items[at] = item;
        else items.Add(item);
        return this with { Items = items };
    }

    public Profile WithoutItem(NewItem item) => this with { Items = Items.Where(i => i != item).ToList() };

    /// <summary>Every submenu name in use, by rules and by new items.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Groups => Rules
        .Where(r => !r.Hidden)
        .Select(r => r.Group)
        .Concat(Items.Select(i => i.Group))
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The combined effect of every rule that applies to an item in a given right-click.
    /// Wider rules are applied first, so a rule for one file type overrides a rule for everywhere.
    /// </summary>
    public ItemRule? EffectiveRule(string engineName, string parentPath, Scope context)
    {
        ItemRule? combined = null;
        foreach (var rule in Rules.Where(r => r.Scope.Covers(context) && r.Targets(engineName, parentPath)).OrderBy(r => r.Scope.Specificity))
        {
            combined = combined is null
                ? rule
                : rule with
                {
                    Hidden = combined.Hidden || rule.Hidden,
                    NewTitle = rule.NewTitle ?? combined.NewTitle,
                    Group = rule.Group ?? combined.Group,
                    Placement = rule.Placement == Placement.Default ? combined.Placement : rule.Placement,
                    Anchor = rule.Placement == Placement.Default ? combined.Anchor : rule.Anchor,
                    AfterAnchor = rule.Placement == Placement.Default ? combined.AfterAnchor : rule.AfterAnchor,
                };
        }

        return combined;
    }
}

public static class ProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(Profile profile) => JsonSerializer.Serialize(profile, Options);

    public static Profile Deserialize(string json) =>
        JsonSerializer.Deserialize<Profile>(json, Options) ?? new Profile();
}
