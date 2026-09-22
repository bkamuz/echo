namespace echo.App.ViewModels;

public sealed record ComputeDeviceOption(
    string Id,
    string DisplayName,
    string Tooltip,
    bool IsEnabled = true)
{
    public override string ToString() => DisplayName;
}
