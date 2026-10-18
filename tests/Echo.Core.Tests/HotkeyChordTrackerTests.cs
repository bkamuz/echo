using echo.Platform.Windows;
using T = echo.Platform.Windows.HotkeyChordTracker.Transition;

namespace echo.Core.Tests;

public sealed class HotkeyChordTrackerTests
{
    private const int LWin = 0x5B;
    private const int RWin = 0x5C;
    private const int LAlt = 0xA4;
    private const int LCtrl = 0xA2;

    [Fact]
    public void CmdAlt_HoldActivates_ReleaseDeactivates()
    {
        var t = new HotkeyChordTracker();
        t.Configure("cmd+alt");
        Assert.Equal(T.None, t.OnKey(LWin, true, false));
        Assert.Equal(T.Activated, t.OnKey(LAlt, true, false));
        // Autorepeat while held must not re-fire.
        Assert.Equal(T.None, t.OnKey(LAlt, true, false));
        Assert.Equal(T.None, t.OnKey(LWin, true, false));
        Assert.Equal(T.Deactivated, t.OnKey(LAlt, false, true));
        Assert.Equal(T.None, t.OnKey(LWin, false, true));
    }

    [Fact]
    public void RightWinAndAltOrderDoNotMatter()
    {
        var t = new HotkeyChordTracker();
        t.Configure("cmd+alt");
        Assert.Equal(T.None, t.OnKey(LAlt, true, false));
        Assert.Equal(T.Activated, t.OnKey(RWin, true, false));
        Assert.Equal(T.Deactivated, t.OnKey(RWin, false, true));
    }

    [Fact]
    public void Reset_DropsStalePressedKeys()
    {
        var t = new HotkeyChordTracker();
        t.Configure("ctrl+cmd");
        t.OnKey(LCtrl, true, false);
        t.Reset();
        Assert.Equal(T.None, t.OnKey(LWin, true, false));
    }

    [Fact]
    public void EmptyOrUnknownHotkey_NeverActivates()
    {
        var t = new HotkeyChordTracker();
        t.Configure("");
        Assert.Equal(T.None, t.OnKey(LWin, true, false));
        t.Configure("bogus");
        Assert.Empty(t.RequiredKeys);
    }

    [Theory]
    [InlineData("cmd", 0x5B)]
    [InlineData("win", 0x5B)]
    [InlineData("alt", 0x12)]
    [InlineData("ctrl", 0x11)]
    [InlineData("a", 'A')]
    public void MapToken_Maps(string token, int vk) => Assert.Equal(vk, HotkeyChordTracker.MapToken(token));
}

public sealed class HotkeyHookWatchdogTests
{
    [Fact]
    public void Healthy_RecentCallback_NoReinstall()
    {
        Assert.False(HotkeyHookWatchdog.ShouldReinstall(10_000, 0, 9_000, 9_500, chordActive: false));
    }

    [Fact]
    public void OsSawInputLongAfterLastCallback_Reinstalls()
    {
        Assert.True(HotkeyHookWatchdog.ShouldReinstall(20_000, 0, 5_000, 19_000, chordActive: false));
    }

    [Fact]
    public void PeriodicRefresh_AfterInterval()
    {
        var interval = (long)HotkeyHookWatchdog.RefreshInterval.TotalMilliseconds;
        Assert.True(HotkeyHookWatchdog.ShouldReinstall(interval + 1, 0, interval, interval, chordActive: false));
        Assert.True(HotkeyHookWatchdog.IsRefreshDue(interval, 0));
    }

    [Fact]
    public void NeverReinstallWhileChordHeld()
    {
        Assert.False(HotkeyHookWatchdog.ShouldReinstall(999_999, 0, 0, 999_000, chordActive: true));
    }

    [Fact]
    public void FreshInstall_IgnoresOlderInput()
    {
        Assert.False(HotkeyHookWatchdog.ShouldReinstall(10_000, 9_000, 0, 9_500, chordActive: false));
    }
}
