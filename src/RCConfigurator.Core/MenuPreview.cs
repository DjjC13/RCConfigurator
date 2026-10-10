namespace RCConfigurator.Core;

/// <summary>One row of the menu as it should look once the rules are applied.</summary>
public sealed record PreviewItem
{
    public string? Title { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsSubmenu { get; init; }

    /// <summary>The item Windows reported that this row comes from, or null for a row the profile adds.</summary>
    public MenuEntry? Source { get; init; }

    /// <summary>The title as the engine sees it, which is what a moved item's neighbour is found by.</summary>
    public string EngineTitle { get; init; } = "";

    public IReadOnlyList<PreviewItem> Children { get; init; } = [];
}

/// <summary>
/// Works out what a menu will look like after the engine applies a <see cref="Profile"/>.
/// It follows the engine's ordering: items pinned to the top, then the rest in their original
/// order with added items and submenus dropped in at the separator nearest the middle, then
/// items pinned to the bottom. Items placed next to a neighbour are inserted last, one at a time
/// and in their original order, wherever that neighbour is by then.
/// </summary>
public static class MenuPreview
{
    public static IReadOnlyList<PreviewItem> Build(MenuSnapshot snapshot, Scope context, Profile profile)
    {
        var groups = new List<Group>();
        var level = Arrange(snapshot.Items, parentPath: "", context, profile, groups);
        var added = profile.Items.Where(i => i.Scope.Covers(context)).ToList();

        // Added items come before moved ones inside a submenu, as the engine lists them.
        foreach (var group in profile.Groups)
        {
            var own = added.Where(i => string.Equals(i.Group, group, StringComparison.OrdinalIgnoreCase)).Select(ToPreview).ToList();
            var moved = groups.Find(g => string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase))?.Items ?? [];
            if (own.Count + moved.Count == 0) continue;
            level.AddedMiddle.Add(new PreviewItem { Title = group, EngineTitle = EngineName.From(group), IsSubmenu = true, Children = [.. own, .. moved] });
        }

        foreach (var item in added.Where(i => i.Group is null))
        {
            var list = item.Placement switch
            {
                Placement.Top => level.AddedTop,
                Placement.Bottom => level.AddedBottom,
                _ => level.AddedMiddle,
            };
            list.Add(ToPreview(item));
        }

        return level.Finish();
    }

    /// <summary>
    /// Finds the row an original item ends up on, together with the menu or submenu that row is
    /// in. The row is -1 when the item is no longer shown.
    /// </summary>
    public static (int Row, IReadOnlyList<PreviewItem> Menu) RowOf(IReadOnlyList<PreviewItem> menu, MenuEntry source)
    {
        for (var i = 0; i < menu.Count; i++)
        {
            if (ReferenceEquals(menu[i].Source, source)) return (i, menu);
        }

        foreach (var item in menu)
        {
            var found = RowOf(item.Children, source);
            if (found.Row >= 0) return found;
        }

        return (-1, []);
    }

    private static PreviewItem ToPreview(NewItem item) => new() { Title = item.Title, EngineTitle = EngineName.From(item.Title) };

    private static Level Arrange(IReadOnlyList<MenuEntry> entries, string parentPath, Scope context, Profile profile, List<Group> groups)
    {
        var level = new Level();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                level.Middle.Add(new PreviewItem { IsSeparator = true, Source = entry });
                continue;
            }

            var name = EngineName.From(entry.RawText);
            var rule = profile.EffectiveRule(name, parentPath, context);
            if (rule is { Hidden: true }) continue;

            var item = new PreviewItem
            {
                Title = rule?.NewTitle ?? entry.Text,
                EngineTitle = rule?.NewTitle is { } newTitle ? EngineName.From(newTitle) : name,
                IsSubmenu = entry.IsSubmenu,
                Source = entry,
                Children = entry.IsSubmenu ? ArrangeSubmenu(entry, name, parentPath, context, profile, groups) : [],
            };

            if (rule?.Group is { } groupName)
            {
                var group = groups.Find(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group is null) groups.Add(group = new Group(groupName));
                group.Items.Add(item);
                continue;
            }

            switch (rule?.Placement)
            {
                case Placement.Top: level.Top.Add(item); break;
                case Placement.Bottom: level.Bottom.Add(item); break;
                case Placement.Custom when rule.Anchor is not null: level.Custom.Add((item, rule.Anchor, rule.AfterAnchor)); break;
                default: level.Middle.Add(item); break;
            }
        }

        return level;
    }

    private static IReadOnlyList<PreviewItem> ArrangeSubmenu(
        MenuEntry submenu, string name, string parentPath, Scope context, Profile profile, List<Group> groups)
    {
        var path = parentPath.Length == 0 ? EngineName.Path([name]) : $"{parentPath}/{EngineName.Path([name])}";
        return Arrange(submenu.Children, path, context, profile, groups).Finish();
    }

    private sealed record Group(string Name)
    {
        public List<PreviewItem> Items { get; } = [];
    }

    /// <summary>The rows of one menu, sorted into the buckets the engine keeps.</summary>
    private sealed class Level
    {
        public List<PreviewItem> AddedTop { get; } = [];
        public List<PreviewItem> Top { get; } = [];
        public List<PreviewItem> Middle { get; } = [];
        public List<PreviewItem> AddedMiddle { get; } = [];
        public List<PreviewItem> Bottom { get; } = [];
        public List<PreviewItem> AddedBottom { get; } = [];
        public List<(PreviewItem Item, string Anchor, bool After)> Custom { get; } = [];

        public List<PreviewItem> Finish()
        {
            List<PreviewItem> menu = [.. AddedTop, .. Top, .. Middle];
            menu.InsertRange(FindMiddleSeparator(menu), AddedMiddle);
            menu.AddRange(Bottom);
            menu.AddRange(AddedBottom);

            foreach (var (item, anchor, after) in Custom)
            {
                var at = menu.FindIndex(i => !i.IsSeparator && string.Equals(i.EngineTitle, anchor, StringComparison.OrdinalIgnoreCase));
                if (at < 0) menu.Add(item);
                else menu.Insert(at + (after ? 1 : 0), item);
            }

            return TidySeparators(menu);
        }
    }

    /// <summary>Where the engine drops added items: the separator at or just above the middle, else the first one below it.</summary>
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
