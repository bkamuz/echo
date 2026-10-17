using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(echo.App.Tests.TestAppBuilder))]

namespace echo.App.Tests;

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        foreach (var key in new[] { "IconCheck", "IconDownload", "IconTrash" })
        {
            Resources[key] = StreamGeometry.Parse("M0,0 L1,1");
        }
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
