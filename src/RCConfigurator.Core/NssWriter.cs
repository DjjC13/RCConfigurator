using System.Text;

namespace RCConfigurator.Core;

/// <summary>Files from the engine's own folder to pull into the generated config.</summary>
/// <param name="ThemeFile">The engine's theme file, or null to use its built-in look.</param>
/// <param name="ImagesFile">The engine's icon definitions, or null to leave items without engine icons.</param>
public sealed record NssOptions(string? ThemeFile = null, string? ImagesFile = null);

/// <summary>Writes a <see cref="Profile"/> as a Nilesoft Shell configuration file.</summary>
public static class NssWriter
{
    public static string Write(Profile profile, NssOptions? options = null)
    {
        options ??= new NssOptions();
        var nss = new StringBuilder();

        nss.AppendLine("// Written by RCConfigurator. Edit your menus there: changes made here are overwritten.");
        nss.AppendLine();
        nss.AppendLine("settings");
        nss.AppendLine("{");
        nss.AppendLine("\tpriority=1");
        // Leave the menus of other programs' Open and Save dialogs alone.
        nss.AppendLine("\texclude.where = !process.is_explorer");
        nss.AppendLine("\tshowdelay = 200");
        nss.AppendLine("\ttip.enabled=true");
        nss.AppendLine("}");
        nss.AppendLine();

        if (options.ThemeFile is not null) nss.AppendLine($"import {Quote(options.ThemeFile)}");
        if (options.ImagesFile is not null) nss.AppendLine($"import {Quote(options.ImagesFile)}");

        // A moved item only appears if a submenu with that name exists, so declare each one,
        // with the new items that belong in it. The engine drops a submenu that ends up empty.
        var groups = profile.Groups;
        if (groups.Count > 0)
        {
            nss.AppendLine();
            foreach (var group in groups)
            {
                nss.AppendLine($"menu(mode=\"multiple\" title={Quote(group)})");
                nss.AppendLine("{");
                foreach (var item in profile.Items.Where(i => IsIn(i, group))) nss.AppendLine("\t" + WriteItem(item));
                nss.AppendLine("}");
            }
        }

        var ungrouped = profile.Items.Where(i => i.Group is null).ToList();
        if (ungrouped.Count > 0)
        {
            nss.AppendLine();
            foreach (var item in ungrouped) nss.AppendLine(WriteItem(item));
        }

        var active = profile.Rules.Where(r => !r.HasNoEffect).ToList();
        if (active.Count > 0)
        {
            nss.AppendLine();
            // Later rules win, so write the widest scopes first.
            foreach (var rule in active.OrderBy(r => r.Scope.Specificity)) nss.AppendLine(WriteRule(rule));
        }

        return nss.ToString();
    }

    private static bool IsIn(NewItem item, string group) =>
        string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase);

    private static string WriteRule(ItemRule rule)
    {
        var parts = new List<string> { $"type='{TypeOf(rule.Scope)}'" };
        if (rule.Scope.Count == SelectionCount.One) parts.Add("mode=\"single\"");

        var conditions = new List<string>();
        if (rule.Scope.Count == SelectionCount.Several) conditions.Add("sel.count>1");
        if (rule.Scope is { Kind: ScopeKind.File, Extension: not null })
        {
            conditions.Add($"sel.file.ext=={Quote(rule.Scope.Extension)}");
        }

        // A find pattern treats | as "or", so a title containing one is compared whole instead.
        var useFind = !rule.Name.Contains('|');
        if (!useFind) conditions.Add($"this.name=={Quote(rule.Name)}");

        if (conditions.Count > 0) parts.Add($"where={string.Join(" and ", conditions)}");
        if (rule.ParentPath.Length > 0) parts.Add($"in={Quote(rule.ParentPath)}");
        if (useFind)
        {
            var name = rule.Name.ToLowerInvariant();
            parts.Add($"find={Quote(rule.Match == MatchKind.Exact ? $"\"{name}\"" : $"{name}*")}");
        }

        if (rule.Hidden)
        {
            parts.Add("vis=vis.remove");
        }
        else
        {
            if (rule.NewTitle is not null) parts.Add($"title={Quote(rule.NewTitle)}");
            if (rule.Group is not null) parts.Add($"menu={Quote(rule.Group)}");
            if (PositionOf(rule.Placement, rule.Anchor, rule.AfterAnchor) is { } position) parts.Add(position);
        }

        return $"modify({string.Join(' ', parts)})";
    }

    private static string WriteItem(NewItem item)
    {
        var parts = new List<string> { $"type='{TypeOf(item.Scope)}'" };

        // New items show for a single selection unless told otherwise. With several things
        // selected, run the program once for each of them.
        var severalAllowed = item.Scope.Count != SelectionCount.One;
        parts.Add(severalAllowed ? "mode=\"multiple\"" : "mode=\"single\"");
        if (item.Scope.Count == SelectionCount.Several) parts.Add("where=sel.count>1");
        if (item.Scope is { Kind: ScopeKind.File, Extension: not null }) parts.Add($"find={Quote(item.Scope.Extension)}");

        parts.Add($"title={Quote(item.Title)}");
        if (item.Program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) parts.Add($"image={Quote(item.Program)}");
        parts.Add($"cmd={Quote(item.Program)}");
        if (item.Arguments.Trim().Length > 0) parts.Add($"args={ArgumentsOf(item.Arguments.Trim())}");
        if (severalAllowed) parts.Add("invoke=\"multiple\"");
        if (PositionOf(item.Placement, anchor: null, after: false) is { } position) parts.Add(position);

        return $"item({string.Join(' ', parts)})";
    }

    /// <summary>Joins the literal parts of the arguments with the selected path, so nothing the user typed is evaluated.</summary>
    private static string ArgumentsOf(string arguments) => string.Join(
        " + sel.path + ",
        arguments.Split(NewItem.PathPlaceholder).Select(Quote));

    private static string? PositionOf(Placement placement, string? anchor, bool after) => placement switch
    {
        Placement.Top => "pos=\"top\"",
        Placement.Bottom => "pos=\"bottom\"",
        // indexof finds the named item and inserts this one that many rows after it.
        Placement.Custom when anchor is not null => $"pos=indexof({Quote($"\"{anchor.ToLowerInvariant()}\"")}, {(after ? 1 : 0)})",
        _ => null,
    };

    private static string TypeOf(Scope scope) => scope.Kind switch
    {
        ScopeKind.File => "file",
        ScopeKind.Folder => "dir",
        ScopeKind.Drive => "drive",
        ScopeKind.FolderBackground => "dir.back|drive.back",
        ScopeKind.Desktop => "desktop",
        _ => "*",
    };

    /// <summary>A double-quoted engine string. Unlike single quotes, these never evaluate their content.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
