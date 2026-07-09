namespace echo.Abstractions.Platform;

public interface ITaskbarIconSync
{
    void Attach(nint windowHandle);

    void ApplyIcon(ReadOnlyMemory<byte> pngBytes, DictationOverlayState state, string description);
}
