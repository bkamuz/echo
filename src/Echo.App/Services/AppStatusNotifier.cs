using echo.Abstractions.Platform;
using echo.App.ViewModels;

namespace echo.App.Services;

public sealed class AppStatusNotifier(AppStatusViewModel status) : IUserStatusNotifier
{
    public void ShowTemporary(string message, int clearAfterMs = 4000, bool alert = false, bool warning = false) =>
        status.SetStatusTemporary(message, clearAfterMs, alert: alert, warning: warning);
}
