using echo.Engines.ParakeetNpu;

namespace echo.Core.Tests;

public class QnnNativeLoaderTests
{
    [Fact]
    public void PrependPathEntry_IsIdempotent()
    {
        var runtimeDir = @"C:\Users\test\AppData\Roaming\Echo\qnn";
        var first = QnnNativeLoader.PrependPathEntry(@"C:\Windows\System32", runtimeDir);
        var second = QnnNativeLoader.PrependPathEntry(first, runtimeDir);

        Assert.Equal(first, second);
        Assert.StartsWith(runtimeDir, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrependAdspLibraryPath_PrependsRuntimeDir()
    {
        var runtimeDir = @"C:\Users\test\AppData\Roaming\Echo\qnn";
        var updated = QnnNativeLoader.PrependAdspLibraryPath(@"C:\Existing\Path", runtimeDir);

        Assert.Equal($@"{runtimeDir};C:\Existing\Path", updated);
    }

    [Fact]
    public void PrependAdspLibraryPath_IsIdempotent()
    {
        var runtimeDir = @"C:\Users\test\AppData\Roaming\Echo\qnn";
        var first = QnnNativeLoader.PrependAdspLibraryPath(null, runtimeDir);
        var second = QnnNativeLoader.PrependAdspLibraryPath(first, runtimeDir);

        Assert.Equal(first, second);
        Assert.Equal(runtimeDir, first);
    }
}
