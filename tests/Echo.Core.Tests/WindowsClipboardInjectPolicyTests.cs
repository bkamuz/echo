using echo.Abstractions.Platform;
using echo.Platform.Windows;

namespace echo.Core.Tests;

public sealed class WindowsClipboardInjectPolicyTests
{
    [Fact]
    public void Resolve_ReturnsAutoPasted_WhenPasteSucceeded()
    {
        var result = WindowsClipboardInjectPolicy.Resolve("clipboard", ClipboardPasteOutcome.Pasted);

        Assert.NotNull(result);
        Assert.Equal(TextInjectionOutcome.AutoPasted, result.Outcome);
    }

    [Fact]
    public void Resolve_ReturnsAutoPasted_ForAutoMethod_WhenPasteSucceeded()
    {
        var result = WindowsClipboardInjectPolicy.Resolve("auto", ClipboardPasteOutcome.Pasted);

        Assert.NotNull(result);
        Assert.Equal(TextInjectionOutcome.AutoPasted, result.Outcome);
    }

    [Fact]
    public void Resolve_ReturnsFailed_ForClipboard_WhenPasteFailed_WithoutTypingFallback()
    {
        var result = WindowsClipboardInjectPolicy.Resolve("clipboard", ClipboardPasteOutcome.FailedBeforePaste);

        Assert.NotNull(result);
        Assert.Equal(TextInjectionOutcome.Failed, result.Outcome);
        Assert.Equal("Loc.Inject.Failed", result.Message);
    }

    [Fact]
    public void Resolve_ReturnsFailed_ForFocusStolen_WithoutTypingFallback()
    {
        var clipboard = WindowsClipboardInjectPolicy.Resolve("clipboard", ClipboardPasteOutcome.FocusStolen);
        Assert.NotNull(clipboard);
        Assert.Equal(TextInjectionOutcome.Failed, clipboard.Outcome);

        var auto = WindowsClipboardInjectPolicy.Resolve("auto", ClipboardPasteOutcome.FocusStolen);
        Assert.NotNull(auto);
        Assert.Equal(TextInjectionOutcome.Failed, auto.Outcome);
    }
}
