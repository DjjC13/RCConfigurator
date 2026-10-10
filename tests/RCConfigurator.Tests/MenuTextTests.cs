using RCConfigurator.Core;

namespace RCConfigurator.Tests;

public class MenuTextTests
{
    [Theory]
    [InlineData("Open", "Open")]
    [InlineData("&Open", "Open")]
    [InlineData("Open wit&h", "Open with")]
    [InlineData("&Copy\tCtrl+C", "Copy")]
    [InlineData("Cut && Paste", "Cut & Paste")]
    [InlineData("打开(&O)", "打开")]
    [InlineData("Scan (x64)", "Scan (x64)")]
    [InlineData("", "")]
    public void Clean_returns_the_title_the_user_sees(string raw, string expected)
    {
        Assert.Equal(expected, MenuText.Clean(raw));
    }

    [Fact]
    public void Clean_passes_null_through()
    {
        Assert.Null(MenuText.Clean(null));
    }
}
