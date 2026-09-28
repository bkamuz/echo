namespace echo.Core.Tests;

public class ParakeetNpuWorkerSessionGateTests
{
    [Fact]
    public void ShouldUseWorker_BecomesFalse_AfterMarkWorkerFailed()
    {
        var gate = new echo.Platform.Windows.ParakeetNpuWorkerSessionGate();

        Assert.True(gate.ShouldUseWorker(wantNpu: true, isolate: true));

        gate.MarkWorkerFailed();

        Assert.False(gate.ShouldUseWorker(wantNpu: true, isolate: true));
    }

    [Fact]
    public void OnConfigure_ResetsGate_WhenDeviceLeavesNpu()
    {
        var gate = new echo.Platform.Windows.ParakeetNpuWorkerSessionGate();
        gate.MarkWorkerFailed();

        gate.OnConfigure(wantNpu: false, optionsChanged: false);

        Assert.True(gate.ShouldUseWorker(wantNpu: true, isolate: true));
    }

    [Fact]
    public void OnConfigure_ResetsGate_WhenNpuSettingsChange()
    {
        var gate = new echo.Platform.Windows.ParakeetNpuWorkerSessionGate();
        gate.MarkWorkerFailed();

        gate.OnConfigure(wantNpu: true, optionsChanged: true);

        Assert.True(gate.ShouldUseWorker(wantNpu: true, isolate: true));
    }
}
