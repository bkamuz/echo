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
        var normalized = UpdateEnvironment.NormalizeVersion(UpdateEnvironment.CurrentVersion);
        Assert.Equal(
            $"v{normalized.Major}.{normalized.Minor}.{normalized.Build}",
            UpdateEnvironment.DisplayVersion);
    }

    [Fact]
    public void DisplayVersion_UsesThreePartSemver()
    {
        Assert.Matches(@"^v\d+\.\d+\.\d+$", UpdateEnvironment.DisplayVersion);
    }

    [Fact]
    public void NormalizeVersion_IgnoresRevision()
    {
        var withRevision = new Version(1, 3, 3, 0);
        var withoutRevision = new Version(1, 3, 3);

        Assert.Equal(
            UpdateEnvironment.NormalizeVersion(withRevision),
            UpdateEnvironment.NormalizeVersion(withoutRevision));
    }

    [Theory]
    [InlineData("1.3.1", "1.3.2", true)]
    [InlineData("1.3.2", "1.3.2", false)]
    [InlineData("1.3.3", "1.3.2", false)]
    [InlineData("1.3.3.0", "1.3.3", false)]
    public void IsNewerVersion_PortableReleaseMatrix(string current, string latest, bool expectsUpdate)
    {
        var hasUpdate = UpdateEnvironment.IsNewerVersion(
            Version.Parse(latest),
            Version.Parse(current));
        Assert.Equal(expectsUpdate, hasUpdate);
    }
}
