namespace echo.App.ViewModels;

public sealed record ComputeDeviceOption(string Id, string DisplayName, string Tooltip)
{
    public override string ToString() => DisplayName;
}
