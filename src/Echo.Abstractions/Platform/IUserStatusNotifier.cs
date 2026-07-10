namespace echo.Abstractions.Platform;

public interface IUserStatusNotifier
{
    void ShowTemporary(string message, int clearAfterMs = 4000);
}
