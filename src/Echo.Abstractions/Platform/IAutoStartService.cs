namespace echo.Abstractions.Platform;

public interface IAutoStartService
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
