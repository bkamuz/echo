using echo.Core;

namespace echo.Core.Tests;

public class HotkeyTokensTests
{
    [Theory]
    [InlineData("ctrl+cmd", "Ctrl + Win")]
    [InlineData("ctrl+alt+a", "Ctrl + Alt + a")]
    [InlineData("shift+win+f2", "Shift + Win + f2")]
    [InlineData("", "")]
    public void ToDisplay_FormatsCorrectly(string input, string expected)
    {
        var result = HotkeyTokens.ToDisplay(input);
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
