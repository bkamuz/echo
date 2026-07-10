using echo.Core;

namespace echo.Core.Tests;

public class HotkeyTokensTests
{
    [Theory]
    [InlineData(HotkeyDisplayPlatform.Windows, "ctrl+cmd", "Ctrl + Win")]
    [InlineData(HotkeyDisplayPlatform.Linux, "ctrl+cmd", "Ctrl + Super")]
    [InlineData(HotkeyDisplayPlatform.MacOS, "ctrl+cmd", "Ctrl + ⌘")]
    [InlineData(HotkeyDisplayPlatform.Windows, "ctrl+alt+a", "Ctrl + Alt + a")]
    [InlineData(HotkeyDisplayPlatform.Linux, "ctrl+alt+a", "Ctrl + Alt + a")]
    [InlineData(HotkeyDisplayPlatform.Windows, "shift+win+f2", "Shift + Win + f2")]
    [InlineData(HotkeyDisplayPlatform.Linux, "shift+win+f2", "Shift + Super + f2")]
    [InlineData(HotkeyDisplayPlatform.MacOS, "shift+win+f2", "Shift + ⌘ + f2")]
    [InlineData(HotkeyDisplayPlatform.Windows, "", "")]
    public void ToDisplay_FormatsCorrectly(HotkeyDisplayPlatform platform, string input, string expected)
    {
        var result = HotkeyTokens.ToDisplay(input, platform);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ctrl+cmd", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsValid_ValidatesCorrectly(string? input, bool expected)
    {
        var result = HotkeyTokens.IsValid(input!);
        Assert.Equal(expected, result);
    }
}
