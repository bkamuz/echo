namespace echo.Abstractions.Platform;

public interface IHotkeyService
{
    event Action? Activated;
    event Action? Deactivated;
    void Configure(string hotkey);
    void Start();
    void Stop();
}
