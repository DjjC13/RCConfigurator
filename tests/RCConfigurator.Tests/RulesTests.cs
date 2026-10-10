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
