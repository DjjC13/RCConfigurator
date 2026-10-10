namespace RCConfigurator.Core;

/// <summary>One row of the menu as it should look once the rules are applied.</summary>
public sealed record PreviewItem
{
    public string? Title { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsSubmenu { get; init; }

    public IReadOnlyList<PreviewItem> Children { get; init; } = [];
}

/// <summary>
/// Works out what a menu will look like after the engine applies a <see cref="Profile"/>.
/// It follows the engine's ordering: items pinned to the top, then the rest in their original
/// order with new submenus dropped in at the separator nearest the middle, then items pinned
/// to the bottom.
/// </summary>
public static class MenuPreview
{
    public static IReadOnlyList<PreviewItem> Build(MenuSnapshot snapshot, Scope context, Profile profile)
    {
        var groups = new List<(string Name, List<PreviewItem> Items)>();
        var (top, middle, bottom) = Arrange(snapshot.Items, parentPath: "", context, profile, groups);

        var menu = top.Concat(middle).ToList();
        var groupMenus = groups.Select(g => new PreviewItem { Title = g.Name, IsSubmenu = true, Children = g.Items }).ToList();
        menu.InsertRange(FindMiddleSeparator(menu), groupMenus);
        menu.AddRange(bottom);

        return TidySeparators(menu);
    }

    private static (List<PreviewItem> Top, List<PreviewItem> Middle, List<PreviewItem> Bottom) Arrange(
        IReadOnlyList<MenuEntry> entries,
        string parentPath,
        Scope context,
        Profile profile,
        List<(string Name, List<PreviewItem> Items)> groups)
    {
        var top = new List<PreviewItem>();
        var middle = new List<PreviewItem>();
        var bottom = new List<PreviewItem>();

        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                middle.Add(new PreviewItem { IsSeparator = true });
                continue;
            }

            var name = EngineName.From(entry.RawText);
            var rule = profile.EffectiveRule(name, parentPath, context);
            if (rule is { Hidden: true }) continue;

            var item = new PreviewItem
            {
                Title = rule?.NewTitle ?? entry.Text,
                IsSubmenu = entry.IsSubmenu,
                Children = entry.IsSubmenu ? ArrangeSubmenu(entry, name, parentPath, context, profile, groups) : [],
            };

            if (rule?.Group is { } groupName)
            {
                var group = groups.Find(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group.Items is null)
                {
                    group = (groupName, []);
                    groups.Add(group);
                }

                group.Items.Add(item);
            }
            else if (rule?.Placement == Placement.Top)
            {
                top.Add(item);
            }
            else if (rule?.Placement == Placement.Bottom)
            {
                bottom.Add(item);
            }
            else
            {
                middle.Add(item);
            }
        }

        return (top, middle, bottom);
    }

    private static IReadOnlyList<PreviewItem> ArrangeSubmenu(
        MenuEntry submenu,
        string name,
        string parentPath,
        Scope context,
        Profile profile,
        List<(string Name, List<PreviewItem> Items)> groups)
    {
        var path = parentPath.Length == 0 ? EngineName.Path([name]) : $"{parentPath}/{EngineName.Path([name])}";
        var (top, middle, bottom) = Arrange(submenu.Children, path, context, profile, groups);
        return TidySeparators([.. top, .. middle, .. bottom]);
    }

    /// <summary>Where the engine drops new submenus: the separator at or just above the middle, else the first one below it.</summary>
    private static int FindMiddleSeparator(List<PreviewItem> menu)
    {
        if (menu.Count == 0) return 0;

        var middle = Math.Min(menu.Count / 2, menu.Count - 1);
        for (var i = middle; i >= 0; i--)
        {
            if (menu[i].IsSeparator) return i;
        }

        for (var i = middle; i < menu.Count; i++)
        {
            if (menu[i].IsSeparator) return i;
        }

        return middle;
    }

    /// <summary>Drops separators that would sit at either end or directly under another separator.</summary>
    private static List<PreviewItem> TidySeparators(List<PreviewItem> menu)
    {
        var tidy = new List<PreviewItem>(menu.Count);
        foreach (var item in menu)
        {
            if (item.IsSeparator && (tidy.Count == 0 || tidy[^1].IsSeparator)) continue;
            tidy.Add(item);
        }

        if (tidy.Count > 0 && tidy[^1].IsSeparator) tidy.RemoveAt(tidy.Count - 1);
        return tidy;
    }
}
