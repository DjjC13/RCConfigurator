using RCConfigurator.Core;

namespace RCConfigurator.Tests;

public class EngineNameTests
{
    [Theory]
    [InlineData("&Open", "Open")]
    [InlineData("Add to archive...", "Add to archive")]
    [InlineData("&Copy\tCtrl+C", "Copy")]
    [InlineData("Rena&me...\tF2", "Rename...")]
    [InlineData("Cut && Paste", "Cut & Paste")]
    [InlineData("打开(&O)", "打开(O)")]
    [InlineData(null, "")]
    public void From_matches_the_engine(string? raw, string expected)
    {
        Assert.Equal(expected, EngineName.From(raw));
    }

    [Fact]
    public void Path_is_lower_case_and_slash_separated()
    {
        Assert.Equal("7-zip/crc sha", EngineName.Path(["7-Zip", "CRC SHA"]));
    }
}

public class ProfileTests
{
    private static readonly Scope TextFiles = Scope.ForFile(".TXT");

    [Fact]
    public void A_rule_replaces_the_earlier_rule_for_the_same_item_and_scope()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Scope = TextFiles, Hidden = true })
            .With(new ItemRule { Name = "print", Scope = TextFiles, NewTitle = "Print it" });

        var rule = Assert.Single(profile.Rules);
        Assert.False(rule.Hidden);
        Assert.Equal("Print it", rule.NewTitle);
    }

    [Fact]
    public void A_rule_that_changes_nothing_is_dropped()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Hidden = true })
            .With(new ItemRule { Name = "Print" });

        Assert.Empty(profile.Rules);
    }

    [Fact]
    public void A_narrower_rule_overrides_a_wider_one()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Scope = TextFiles, NewTitle = "Print text" })
            .With(new ItemRule { Name = "Print", NewTitle = "Print anything", Placement = Placement.Bottom });

        var forText = profile.EffectiveRule("Print", "", Scope.ForFile(".txt"));
        var forImages = profile.EffectiveRule("Print", "", Scope.ForFile(".png"));

        Assert.Equal("Print text", forText!.NewTitle);
        Assert.Equal(Placement.Bottom, forText.Placement);
        Assert.Equal("Print anything", forImages!.NewTitle);
    }

    [Fact]
    public void A_rule_only_reaches_items_in_its_own_submenu()
    {
        var profile = new Profile().With(new ItemRule { Name = "MD5", ParentPath = "7-zip/crc sha", Hidden = true });

        Assert.NotNull(profile.EffectiveRule("MD5", "7-Zip/CRC SHA", Scope.ForFile(".txt")));
        Assert.Null(profile.EffectiveRule("MD5", "", Scope.ForFile(".txt")));
    }

    [Fact]
    public void A_starts_with_rule_matches_titles_that_include_a_file_name()
    {
        var profile = new Profile().With(new ItemRule { Name = "Add to \"", Match = MatchKind.StartsWith, Hidden = true });

        Assert.NotNull(profile.EffectiveRule("Add to \"holiday.7z\"", "", new Scope(ScopeKind.Folder)));
        Assert.Null(profile.EffectiveRule("Add to archive", "", new Scope(ScopeKind.Folder)));
    }

    [Fact]
    public void A_profile_survives_a_round_trip()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Scope = TextFiles, Hidden = true })
            .With(new ItemRule { Name = "Share", Group = "Extras", Placement = Placement.Top });

        var copy = ProfileJson.Deserialize(ProfileJson.Serialize(profile));

        Assert.Equal(profile.Rules, copy.Rules);
    }
}

public class NssWriterTests
{
    [Fact]
    public void An_empty_profile_still_writes_settings()
    {
        var nss = NssWriter.Write(new Profile());

        Assert.Contains("settings", nss);
        Assert.Contains("exclude.where = !process.is_explorer", nss);
        Assert.DoesNotContain("modify(", nss);
        Assert.DoesNotContain("import", nss);
    }

    [Fact]
    public void Engine_files_are_imported_with_escaped_paths()
    {
        var nss = NssWriter.Write(new Profile(), new NssOptions(ThemeFile: @"C:\Shell\imports\theme.nss"));

        Assert.Contains("import \"C:\\\\Shell\\\\imports\\\\theme.nss\"", nss);
    }

    [Fact]
    public void A_hidden_item_for_one_file_type()
    {
        var nss = Write(new ItemRule { Name = "Ask Copilot", Scope = Scope.ForFile(".txt"), Hidden = true });

        Assert.Contains("modify(type='file' where=sel.file.ext==\".txt\" find=\"\\\"ask copilot\\\"\" vis=vis.remove)", nss);
    }

    [Fact]
    public void A_renamed_item_pinned_to_the_top_everywhere()
    {
        var nss = Write(new ItemRule { Name = "Open with Code", NewTitle = "VS Code", Placement = Placement.Top });

        Assert.Contains("modify(type='*' find=\"\\\"open with code\\\"\" title=\"VS Code\" pos=\"top\")", nss);
    }

    [Fact]
    public void An_item_inside_a_submenu_names_that_submenu()
    {
        var nss = Write(new ItemRule { Name = "MD5", ParentPath = "7-zip/crc sha", Scope = new Scope(ScopeKind.Folder), Hidden = true });

        Assert.Contains("modify(type='dir' in=\"7-zip/crc sha\" find=\"\\\"md5\\\"\" vis=vis.remove)", nss);
    }

    [Fact]
    public void A_starts_with_rule_uses_a_wildcard()
    {
        var nss = Write(new ItemRule { Name = "Add to \"", Match = MatchKind.StartsWith, Hidden = true });

        Assert.Contains("find=\"add to \\\"*\"", nss);
    }

    [Fact]
    public void A_title_with_a_pipe_is_compared_whole()
    {
        var nss = Write(new ItemRule { Name = "Scan | Clean", Hidden = true });

        Assert.Contains("modify(type='*' where=this.name==\"Scan | Clean\" vis=vis.remove)", nss);
    }

    [Fact]
    public void Moving_items_declares_each_submenu_once()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Share", Group = "Extras" })
            .With(new ItemRule { Name = "Print", Group = "extras", Scope = new Scope(ScopeKind.FolderBackground) });

        var nss = NssWriter.Write(profile);

        Assert.Equal(1, CountOf(nss, "menu(mode=\"multiple\" title="));
        Assert.Contains("modify(type='*' find=\"\\\"share\\\"\" menu=\"Extras\")", nss);
        Assert.Contains("modify(type='dir.back|drive.back' find=\"\\\"print\\\"\" menu=\"extras\")", nss);
    }

    [Fact]
    public void Wider_rules_are_written_before_narrower_ones()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Scope = Scope.ForFile(".txt"), NewTitle = "A" })
            .With(new ItemRule { Name = "Print", NewTitle = "B" });

        var nss = NssWriter.Write(profile);

        Assert.True(nss.IndexOf("title=\"B\"", StringComparison.Ordinal) < nss.IndexOf("title=\"A\"", StringComparison.Ordinal));
    }

    private static string Write(ItemRule rule) => NssWriter.Write(new Profile().With(rule));

    private static int CountOf(string text, string value) => text.Split(value).Length - 1;
}

public class MenuPreviewTests
{
    private static readonly Scope Context = Scope.ForFile(".txt");

    private static readonly MenuSnapshot Menu = new()
    {
        Items =
        [
            Item("Open"),
            Item("Print"),
            new MenuEntry { IsSeparator = true },
            new MenuEntry
            {
                Text = "7-Zip",
                RawText = "7-Zip",
                IsSubmenu = true,
                Children = [Item("Add to archive..."), Item("Test")],
            },
            Item("Share"),
            new MenuEntry { IsSeparator = true },
            Item("Delete"),
            Item("Properties"),
        ],
    };

    [Fact]
    public void Without_rules_the_menu_is_unchanged()
    {
        Assert.Equal(
            ["Open", "Print", "-", "7-Zip", "Share", "-", "Delete", "Properties"],
            Titles(MenuPreview.Build(Menu, Context, new Profile())));
    }

    [Fact]
    public void Hidden_items_go_and_leave_no_stray_separators()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Open", Hidden = true })
            .With(new ItemRule { Name = "Print", Hidden = true })
            .With(new ItemRule { Name = "Properties", Hidden = true })
            .With(new ItemRule { Name = "Delete", Hidden = true });

        Assert.Equal(["7-Zip", "Share"], Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void Renamed_and_pinned_items_move_to_the_ends()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Share", NewTitle = "Send", Placement = Placement.Top })
            .With(new ItemRule { Name = "Open", Placement = Placement.Bottom });

        Assert.Equal(
            ["Send", "Print", "-", "7-Zip", "-", "Delete", "Properties", "Open"],
            Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void Grouped_items_move_into_a_new_submenu_at_the_middle_separator()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Print", Group = "Extras" })
            .With(new ItemRule { Name = "Share", Group = "Extras" });

        var preview = MenuPreview.Build(Menu, Context, profile);

        Assert.Equal(["Open", "-", "7-Zip", "Extras", "-", "Delete", "Properties"], Titles(preview));
        Assert.Equal(["Print", "Share"], Titles(preview[3].Children));
    }

    [Fact]
    public void Rules_reach_into_submenus()
    {
        var profile = new Profile().With(new ItemRule { Name = "Test", ParentPath = "7-zip", Hidden = true });

        var sevenZip = MenuPreview.Build(Menu, Context, profile).Single(i => i.Title == "7-Zip");

        Assert.Equal(["Add to archive..."], Titles(sevenZip.Children));
    }

    [Fact]
    public void A_rule_for_another_file_type_does_nothing()
    {
        var profile = new Profile().With(new ItemRule { Name = "Print", Scope = Scope.ForFile(".png"), Hidden = true });

        Assert.Contains("Print", Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    private static MenuEntry Item(string title) => new() { Text = title, RawText = title };

    private static string[] Titles(IEnumerable<PreviewItem> items) =>
        items.Select(i => i.IsSeparator ? "-" : i.Title!).ToArray();
}

public class ScopeTests
{
    [Fact]
    public void A_rule_for_several_things_skips_a_single_selection()
    {
        var several = Scope.ForFile(null, SelectionCount.Several);

        Assert.True(several.Covers(Scope.ForFile(".txt", SelectionCount.Several)));
        Assert.False(several.Covers(Scope.ForFile(".txt", SelectionCount.One)));
    }

    [Fact]
    public void A_rule_without_a_count_covers_one_and_several()
    {
        var allFiles = Scope.ForFile(null);

        Assert.True(allFiles.Covers(Scope.ForFile(".txt", SelectionCount.One)));
        Assert.True(allFiles.Covers(Scope.ForFile(".txt", SelectionCount.Several)));
        Assert.False(allFiles.Covers(new Scope(ScopeKind.Folder, Count: SelectionCount.One)));
    }

    [Fact]
    public void Narrower_scopes_are_more_specific()
    {
        Assert.True(Scope.Everywhere.Specificity < Scope.ForFile(null).Specificity);
        Assert.True(Scope.ForFile(null).Specificity < Scope.ForFile(".txt").Specificity);
        Assert.True(Scope.ForFile(".txt").Specificity < Scope.ForFile(".txt", SelectionCount.Several).Specificity);
    }
}

public class OrderingAndNewItemTests
{
    private static readonly Scope Context = Scope.ForFile(".txt", SelectionCount.One);

    private static readonly MenuSnapshot Menu = new()
    {
        Items = [Item("Open"), Item("Edit"), new MenuEntry { IsSeparator = true }, Item("Cut"), Item("Copy"), Item("Delete")],
    };

    [Fact]
    public void A_moved_item_is_written_relative_to_its_neighbour()
    {
        var after = NssWriter.Write(new Profile().With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Open", AfterAnchor = true }));
        var before = NssWriter.Write(new Profile().With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Open" }));

        Assert.Contains("modify(type='*' find=\"\\\"delete\\\"\" pos=indexof(\"\\\"open\\\"\", 1))", after);
        Assert.Contains("modify(type='*' find=\"\\\"delete\\\"\" pos=indexof(\"\\\"open\\\"\", 0))", before);
    }

    [Fact]
    public void Count_scopes_are_written_as_mode_and_selection_tests()
    {
        var one = NssWriter.Write(new Profile().With(new ItemRule { Name = "Edit", Scope = new Scope(ScopeKind.Folder, Count: SelectionCount.One), Hidden = true }));
        var several = NssWriter.Write(new Profile().With(new ItemRule { Name = "Edit", Scope = Scope.ForFile(".txt", SelectionCount.Several), Hidden = true }));

        Assert.Contains("modify(type='dir' mode=\"single\" find=\"\\\"edit\\\"\" vis=vis.remove)", one);
        Assert.Contains("modify(type='file' where=sel.count>1 and sel.file.ext==\".txt\" find=\"\\\"edit\\\"\" vis=vis.remove)", several);
    }

    [Fact]
    public void A_moved_item_lands_next_to_its_neighbour()
    {
        var below = new Profile().With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Open", AfterAnchor = true });
        var above = new Profile().With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Open" });

        Assert.Equal(["Open", "Delete", "Edit", "-", "Cut", "Copy"], Titles(MenuPreview.Build(Menu, Context, below)));
        Assert.Equal(["Delete", "Open", "Edit", "-", "Cut", "Copy"], Titles(MenuPreview.Build(Menu, Context, above)));
    }

    [Fact]
    public void A_moved_item_stays_with_its_neighbour_when_rows_above_change()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Cut", AfterAnchor = true })
            .With(new ItemRule { Name = "Open", Hidden = true })
            .WithItem(new NewItem { Title = "Mine", Program = "a.exe", Placement = Placement.Top });

        Assert.Equal(["Mine", "Edit", "-", "Cut", "Delete", "Copy"], Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void A_neighbour_is_found_by_its_new_name()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Edit", NewTitle = "Modify" })
            .With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Modify", AfterAnchor = true });

        Assert.Equal(["Open", "Modify", "Delete", "-", "Cut", "Copy"], Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void Moved_items_are_placed_in_their_original_order()
    {
        // Open comes first in the original menu, so it is placed before Copy has been put back.
        // Its neighbour is missing at that moment, which sends it to the end.
        var profile = new Profile()
            .With(new ItemRule { Name = "Open", Placement = Placement.Custom, Anchor = "Copy", AfterAnchor = true })
            .With(new ItemRule { Name = "Copy", Placement = Placement.Custom, Anchor = "Edit", AfterAnchor = true });

        Assert.Equal(["Edit", "Copy", "-", "Cut", "Delete", "Open"], Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void RowOf_finds_an_item_and_its_menu()
    {
        var profile = new Profile().With(new ItemRule { Name = "Delete", Placement = Placement.Custom, Anchor = "Open", AfterAnchor = true });

        var (row, menu) = MenuPreview.RowOf(MenuPreview.Build(Menu, Context, profile), Menu.Items[5]);
        var hidden = MenuPreview.RowOf(MenuPreview.Build(Menu, Context, new Profile().With(new ItemRule { Name = "Open", Hidden = true })), Menu.Items[0]);

        Assert.Equal(1, row);
        Assert.Equal(6, menu.Count);
        Assert.Equal(-1, hidden.Row);
    }
    [Fact]
    public void A_new_item_is_written_with_its_program_and_the_selected_path()
    {
        var item = new NewItem { Title = "Open in Editor", Program = @"C:\Tools\edit.exe", Arguments = "--file \"%1\" --new", Scope = Scope.ForFile(".txt") };

        var nss = NssWriter.Write(new Profile().WithItem(item));

        Assert.Contains(
            "item(type='file' mode=\"multiple\" find=\".txt\" title=\"Open in Editor\" image=\"C:\\\\Tools\\\\edit.exe\" cmd=\"C:\\\\Tools\\\\edit.exe\" "
            + "args=\"--file \\\"\" + sel.path + \"\\\" --new\" invoke=\"multiple\")",
            nss);
    }

    [Fact]
    public void A_new_item_for_one_selection_at_the_top()
    {
        var item = new NewItem { Title = "Terminal here", Program = "wt.exe", Scope = new Scope(ScopeKind.FolderBackground, Count: SelectionCount.One), Placement = Placement.Top };

        var nss = NssWriter.Write(new Profile().WithItem(item));

        Assert.Contains("item(type='dir.back|drive.back' mode=\"single\" title=\"Terminal here\" image=\"wt.exe\" cmd=\"wt.exe\" pos=\"top\")", nss);
    }

    [Fact]
    public void A_new_item_in_a_submenu_is_written_inside_it()
    {
        var profile = new Profile()
            .WithItem(new NewItem { Title = "Hash it", Program = "hash.cmd", Group = "Tools" })
            .With(new ItemRule { Name = "Copy", Group = "Tools" });

        var nss = NssWriter.Write(profile).ReplaceLineEndings("\n");

        Assert.Contains("menu(mode=\"multiple\" title=\"Tools\")\n{\n\titem(type='*' mode=\"multiple\" title=\"Hash it\" cmd=\"hash.cmd\" invoke=\"multiple\")\n}", nss);
        Assert.Equal(1, nss.Split("menu(mode=").Length - 1);
    }

    [Fact]
    public void New_items_appear_in_the_preview_where_the_engine_puts_them()
    {
        var profile = new Profile()
            .WithItem(new NewItem { Title = "First", Program = "a.exe", Placement = Placement.Top })
            .WithItem(new NewItem { Title = "Middle", Program = "b.exe" })
            .WithItem(new NewItem { Title = "Last", Program = "c.exe", Placement = Placement.Bottom })
            .WithItem(new NewItem { Title = "Folders only", Program = "d.exe", Scope = new Scope(ScopeKind.Folder) });

        Assert.Equal(
            ["First", "Open", "Edit", "Middle", "-", "Cut", "Copy", "Delete", "Last"],
            Titles(MenuPreview.Build(Menu, Context, profile)));
    }

    [Fact]
    public void A_submenu_lists_new_items_before_moved_ones()
    {
        var profile = new Profile()
            .With(new ItemRule { Name = "Copy", Group = "Tools" })
            .WithItem(new NewItem { Title = "Hash it", Program = "hash.cmd", Group = "Tools" });

        var tools = MenuPreview.Build(Menu, Context, profile).Single(i => i.Title == "Tools");

        Assert.Equal(["Hash it", "Copy"], Titles(tools.Children));
    }

    [Fact]
    public void Editing_and_removing_new_items()
    {
        var first = new NewItem { Title = "One", Program = "1.exe" };
        var second = new NewItem { Title = "Two", Program = "2.exe" };
        var profile = new Profile().WithItem(first).WithItem(second);

        var edited = profile.WithItem(first with { Title = "Uno" }, replacing: first);

        Assert.Equal(["Uno", "Two"], edited.Items.Select(i => i.Title));
        Assert.Equal(["Two"], profile.WithoutItem(first).Items.Select(i => i.Title));
    }

    private static MenuEntry Item(string title) => new() { Text = title, RawText = title };

    private static string[] Titles(IEnumerable<PreviewItem> items) =>
        items.Select(i => i.IsSeparator ? "-" : i.Title!).ToArray();
}

