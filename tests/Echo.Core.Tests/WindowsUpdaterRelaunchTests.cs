#pragma warning disable CA1416 // pure string/dictionary logic, platform-independent
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using echo.Platform.Windows;

namespace echo.Core.Tests;

public sealed class WindowsUpdaterRelaunchTests
{
    private static string Script() =>
        WindowsUpdateApplier.BuildUpdaterScript(4242, @"D:\APP\Echo", @"C:\Temp\echo-update\1.9.39\staging", @"C:\Users\u\AppData\Roaming\Echo\echo-update.log");

    [Fact]
    public void Relaunch_IsVisible_NotMinimized()
    {
        var script = Script();
        Assert.DoesNotContain(ApplicationLauncher.MinimizedArgument, script);
        Assert.Contains("-WindowStyle Normal", script);
        Assert.Contains("Start-Process -FilePath $exePath", script);
    }

    [Fact]
    public void Script_StopsLeftoverEchoProcesses_RetriesCopy_AndLogs()
    {
        var script = Script();
        Assert.Contains("Stop-Process -Id $targetPid -Force", script);
        Assert.Contains("$_.Path -ieq $exePath", script);
        Assert.Contains("Copy-WithRetry (Join-Path $stagingDir $exeName) $exePath", script);
        Assert.Contains("$logPath = 'C:\\Users\\u\\AppData\\Roaming\\Echo\\echo-update.log'", script);
        Assert.Contains("Log \"relaunched", script);
        Assert.Contains("Log \"install failed", script);
        // Relaunch happens even if the install step failed (old exe still starts).
        Assert.True(script.IndexOf("install failed", StringComparison.Ordinal) < script.IndexOf("Start-Process -FilePath", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_ClearsInheritedNativeRuntimeEnvironment()
    {
        var script = Script();
        Assert.Contains("Remove-Item Env:ORT_DYLIB_PATH", script);
        Assert.Contains("Remove-Item Env:ADSP_LIBRARY_PATH", script);
        Assert.Contains("$echoDataDir", script);
    }

    [Fact]
    public void Script_EscapesSingleQuotesInPaths()
    {
        var script = WindowsUpdateApplier.BuildUpdaterScript(1, @"D:\Bob's Apps\Echo", @"C:\s", @"C:\l");
        Assert.Contains("'D:\\Bob''s Apps\\Echo'", script);
    }

    [Fact]
    public void SanitizeChildEnvironment_DropsEchoRuntimeDirsAndOrtVars()
    {
        var qnn = AppPaths.NpuDir;
        var directMl = AppPaths.DirectMlDir;
        var env = new Dictionary<string, string?>
        {
            ["PATH"] = $"{qnn};C:\\Windows\\System32;{directMl};{AppPaths.NpuWhisperRuntimeDir}\\;C:\\Tools",
            ["ORT_DYLIB_PATH"] = Path.Combine(qnn, "onnxruntime.dll"),
            ["ADSP_LIBRARY_PATH"] = qnn,
            ["OTHER"] = "keep",
        };

        WindowsUpdateApplier.SanitizeChildEnvironment(env);

        Assert.Equal("C:\\Windows\\System32;C:\\Tools", env["PATH"]);
        Assert.False(env.ContainsKey("ORT_DYLIB_PATH"));
        Assert.False(env.ContainsKey("ADSP_LIBRARY_PATH"));
        Assert.Equal("keep", env["OTHER"]);
    }

    [Fact]
    public void RemoveEchoRuntimeDirectories_KeepsUnrelatedEntries()
    {
        var path = $"C:\\A;\"{AppPaths.NpuDir}\";;C:\\EchoTools;{AppPaths.BaseDir}Other";
        Assert.Equal($"C:\\A;C:\\EchoTools;{AppPaths.BaseDir}Other", SherpaNativeEnvironmentScrubber.RemoveEchoRuntimeDirectories(path));
    }
}
