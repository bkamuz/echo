namespace echo.App.Services;

public interface IAppClipboard
{
    Task SetTextAsync(string text);
}
