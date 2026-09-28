namespace echo.Platform.Windows;

/// <summary>
/// Remembers when the isolated QNN worker failed so we fall back to in-process CPU until settings change.
/// </summary>
internal sealed class ParakeetNpuWorkerSessionGate
{
    private bool _workerDisabledForSession;

    public bool ShouldUseWorker(bool wantNpu, bool isolate) =>
        wantNpu && isolate && !_workerDisabledForSession;

    public void MarkWorkerFailed() => _workerDisabledForSession = true;

    public void OnConfigure(bool wantNpu, bool optionsChanged)
    {
        if (!wantNpu || optionsChanged)
        {
            _workerDisabledForSession = false;
        }
    }
}
