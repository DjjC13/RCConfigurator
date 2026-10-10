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

/// <summary>Which right-clicks a rule applies to.</summary>
/// <param name="Extension">For files only: a lower-case extension such as ".txt", or null for every file.</param>
public sealed record Scope(ScopeKind Kind, string? Extension = null)
{
    public static Scope Everywhere { get; } = new(ScopeKind.Everywhere);

    public static Scope ForFile(string? extension) =>
        new(ScopeKind.File, string.IsNullOrWhiteSpace(extension) ? null : extension.Trim().ToLowerInvariant());

    /// <summary>True when a rule with this scope applies to a right-click described by <paramref name="context"/>.</summary>
    public bool Covers(Scope context) =>
        Kind == ScopeKind.Everywhere
        || (Kind == context.Kind && (Extension is null || Extension == context.Extension));
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

/// <summary>Everything the user has configured.</summary>
public sealed record Profile
{
    public IReadOnlyList<ItemRule> Rules { get; init; } = [];

    /// <summary>Adds the rule, replacing any rule for the same item and scope. A rule that changes nothing is dropped.</summary>
    public Profile With(ItemRule rule)
    {
        var rules = Rules.Where(r => !r.HasSameTargetAs(rule)).ToList();
        if (!rule.HasNoEffect) rules.Add(rule);
        return this with { Rules = rules };
    }

    /// <summary>
    /// The combined effect of every rule that applies to an item in a given right-click.
    /// Wider rules are applied first, so a rule for one file type overrides a rule for everywhere.
    /// </summary>
    public ItemRule? EffectiveRule(string engineName, string parentPath, Scope context)
    {
        ItemRule? combined = null;
        foreach (var rule in Rules.Where(r => r.Scope.Covers(context) && r.Targets(engineName, parentPath)).OrderBy(Specificity))
        {
            combined = combined is null
                ? rule
                : rule with
                {
                    Hidden = combined.Hidden || rule.Hidden,
                    NewTitle = rule.NewTitle ?? combined.NewTitle,
                    Group = rule.Group ?? combined.Group,
                    Placement = rule.Placement == Placement.Default ? combined.Placement : rule.Placement,
                };
        }

        return combined;
    }

    /// <summary>Orders rules from widest to narrowest scope.</summary>
    public static int Specificity(ItemRule rule) => rule.Scope switch
    {
        { Kind: ScopeKind.Everywhere } => 0,
        { Extension: null } => 1,
        _ => 2,
    };
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
