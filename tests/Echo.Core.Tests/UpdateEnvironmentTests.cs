using System.Reflection;
using echo.Core.Update;

namespace echo.Core.Tests;

public class UpdateEnvironmentTests
{
    [Fact]
    public void CurrentVersion_ReadsFromEntryAssembly()
    {
        var expected = Assembly.GetEntryAssembly()?.GetName().Version;
        Assert.NotNull(expected);
        Assert.Equal(expected, UpdateEnvironment.CurrentVersion);
    }

    [Fact]
    public void DisplayVersion_IncludesVersionPrefix()
    {
        Assert.StartsWith("v", UpdateEnvironment.DisplayVersion, StringComparison.Ordinal);
        Assert.Contains(UpdateEnvironment.CurrentVersion.ToString(), UpdateEnvironment.DisplayVersion);
    }

    [Theory]
    [InlineData("1.3.1", "1.3.2", true)]
    [InlineData("1.3.2", "1.3.2", false)]
    [InlineData("1.3.3", "1.3.2", false)]
    public void IsNewerVersion_PortableReleaseMatrix(string current, string latest, bool expectsUpdate)
    {
        var hasUpdate = GitHubReleaseParser.IsNewerVersion(
            Version.Parse(latest),
            Version.Parse(current));
        Assert.Equal(expectsUpdate, hasUpdate);
    }
}
