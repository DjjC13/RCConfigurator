namespace RCConfigurator.Core;

/// <summary>One menu standing for several file types, with a record of which types each item came from.</summary>
/// <param name="Menu">Every item that appears on at least one of the types.</param>
/// <param name="SeenOn">For each item in <paramref name="Menu"/>, the types whose menu has it.</param>
/// <param name="TypeCount">How many types were combined.</param>
public sealed record CombinedMenu(
    MenuSnapshot Menu,
    IReadOnlyDictionary<MenuEntry, IReadOnlyList<string>> SeenOn,
    int TypeCount);

/// <summary>
/// Combines the menus of several file types into one, so a group of similar types can be edited
/// together even though their menus are rarely identical.
/// </summary>
public static class MenuMerge
{
    public static CombinedMenu Combine(IReadOnlyList<(string Type, MenuSnapshot Menu)> menus)
    {
        var root = new List<Node>();

        // Start from the menu most types share, so the usual layout shapes the result and the
        // odd ones are fitted into it. Between equally common menus the fullest goes first.
        var commonness = menus
            .GroupBy(m => Signature(m.Menu))
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var (type, menu) in menus
            .OrderByDescending(m => commonness[Signature(m.Menu)])
            .ThenByDescending(m => m.Menu.Items.Count))
        {
            Merge(root, menu.Items, type);
        }

        var seenOn = new Dictionary<MenuEntry, IReadOnlyList<string>>(ReferenceEqualityComparer.Instance);
        var combined = new MenuSnapshot
        {
            Kind = TargetKind.Items,
            ExtendedVerbs = menus.Any(m => m.Menu.ExtendedVerbs),
            Items = Build(root, seenOn),
        };

        return new CombinedMenu(combined, seenOn, menus.Count);
    }

    private static string Signature(MenuSnapshot menu) =>
        string.Join('\n', menu.Items.Select(i => i.IsSeparator ? "-" : EngineName.From(i.RawText).ToLowerInvariant()));

    private static int IndexOf(List<Node> level, MenuEntry entry, string name) => level.FindIndex(n =>
        !n.Entry.IsSeparator
        && n.Entry.IsSubmenu == entry.IsSubmenu
        && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void Merge(List<Node> level, IReadOnlyList<MenuEntry> entries, string type)
    {
        var isFirst = level.Count == 0;

        // An item new to the level goes just after the last item this menu shares with it,
        // which keeps it beside its usual neighbours.
        var last = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsSeparator)
            {
                if (isFirst) level.Add(new Node(entry, name: ""));
                last = isFirst ? level.Count - 1 : last;
                continue;
            }

            var name = EngineName.From(entry.RawText);
            var at = IndexOf(level, entry, name);
            if (at < 0)
            {
                // With no shared item before it, sit just below the next shared item instead of
                // jumping above everything the types have in common.
                at = last + 1;
                if (last < 0 && !isFirst)
                {
                    var next = entries.Skip(i + 1)
                        .Where(e => !e.IsSeparator)
                        .Select(e => IndexOf(level, e, EngineName.From(e.RawText)))
                        .FirstOrDefault(index => index >= 0, -1);
                    at = next >= 0 ? next + 1 : level.Count;
                }

                level.Insert(at, new Node(entry, name));
            }

            var node = level[at];
            if (!node.Types.Contains(type)) node.Types.Add(type);
            Merge(node.Children, entry.Children, type);

            // Never step back: later new items follow the ones already placed.
            last = Math.Max(last, at);
        }
    }

    private static List<MenuEntry> Build(List<Node> level, Dictionary<MenuEntry, IReadOnlyList<string>> seenOn)
    {
        var entries = new List<MenuEntry>(level.Count);
        foreach (var node in level)
        {
            var entry = node.Entry with { Children = Build(node.Children, seenOn) };
            if (!entry.IsSeparator) seenOn[entry] = node.Types;
            entries.Add(entry);
        }

        return entries;
    }

    private sealed class Node(MenuEntry entry, string name)
    {
        public MenuEntry Entry { get; } = entry;

        public string Name { get; } = name;

        public List<string> Types { get; } = [];

        public List<Node> Children { get; } = [];
    }
}
