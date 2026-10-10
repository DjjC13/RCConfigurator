using RCConfigurator.Core;

namespace RCConfigurator.Tests;

public class SnapshotJsonTests
{
    [Fact]
    public void A_snapshot_survives_a_round_trip()
    {
        var original = new MenuSnapshot
        {
            Kind = TargetKind.FolderBackground,
            Paths = [@"C:\Temp"],
            ExtendedVerbs = true,
            Items =
            [
                new MenuEntry { Text = "Open", RawText = "&Open", Verb = "open", IsDefault = true },
                new MenuEntry { IsSeparator = true },
                new MenuEntry
                {
                    Text = "Send to",
                    RawText = "Se&nd to",
                    IsSubmenu = true,
                    Children = [new MenuEntry { Text = "Desktop", RawText = "Desktop", IsDisabled = true }],
                },
            ],
        };

        var copy = SnapshotJson.Deserialize(SnapshotJson.Serialize(original));

        Assert.Equal(original.Kind, copy.Kind);
        Assert.Equal(original.Paths, copy.Paths);
        Assert.True(copy.ExtendedVerbs);
        Assert.Equal(3, copy.Items.Count);
        Assert.Equal("open", copy.Items[0].Verb);
        Assert.True(copy.Items[0].IsDefault);
        Assert.True(copy.Items[1].IsSeparator);
        Assert.Equal("Desktop", Assert.Single(copy.Items[2].Children).Text);
        Assert.True(copy.Items[2].Children[0].IsDisabled);
    }

    [Fact]
    public void The_target_kind_is_written_as_text()
    {
        var json = SnapshotJson.Serialize(new MenuSnapshot { Kind = TargetKind.FolderBackground });

        Assert.Contains("\"kind\":\"folderBackground\"", json);
    }
}
