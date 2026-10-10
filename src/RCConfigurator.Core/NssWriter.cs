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

        var active = profile.Rules.Where(r => !r.HasNoEffect).ToList();

        // A moved item only appears if a submenu with that name exists, so declare each one.
        // The engine drops a declared submenu that ends up empty.
        var groups = active
            .Where(r => !r.Hidden && r.Group is not null)
            .Select(r => r.Group!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count > 0)
        {
            nss.AppendLine();
            foreach (var group in groups)
            {
                nss.AppendLine($"menu(mode=\"multiple\" title={Quote(group)})");
                nss.AppendLine("{");
                nss.AppendLine("}");
            }
        }

        if (active.Count > 0)
        {
            nss.AppendLine();
            // Later rules win, so write the widest scopes first.
            foreach (var rule in active.OrderBy(Profile.Specificity))
            {
                nss.AppendLine(WriteRule(rule));
            }
        }

        return nss.ToString();
    }

    private static string WriteRule(ItemRule rule)
    {
        var parts = new List<string> { $"type='{TypeOf(rule.Scope)}'" };

        var conditions = new List<string>();
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
            if (rule.Placement == Placement.Top) parts.Add("pos=\"top\"");
            if (rule.Placement == Placement.Bottom) parts.Add("pos=\"bottom\"");
        }

        return $"modify({string.Join(' ', parts)})";
    }

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
