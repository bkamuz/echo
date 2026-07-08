namespace echo.App.ViewModels;

public sealed record InputMethodOption(string Id, string DisplayName, string Tooltip)
{
    public override string ToString() => DisplayName;
}
