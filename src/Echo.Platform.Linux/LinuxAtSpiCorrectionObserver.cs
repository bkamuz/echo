using System.Diagnostics;
using System.Text.Json;
using echo.Core;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Linux;

/// <summary>
/// Correction learning on Linux: reads the focused text field through AT-SPI2 (org.a11y.atspi.Text)
/// with the bundled <c>Scripts/atspi-read.py</c> helper (python3-gi + gir1.2-atspi-2.0).
/// GNOME on Wayland does not expose the focused widget, so learning is unavailable there.
/// </summary>
public sealed class LinuxAtSpiCorrectionObserver : PollingCorrectionObserver
{
    private static readonly Lazy<string?> ScriptPath = new(() =>
        new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Scripts", "atspi-read.py"),
            Path.Combine(AppContext.BaseDirectory, "atspi-read.py"),
        }.FirstOrDefault(File.Exists));

    private bool _hintLogged;

    public LinuxAtSpiCorrectionObserver(DictionaryStore store, ILogger<LinuxAtSpiCorrectionObserver> logger)
        : base(store, logger)
    {
    }

    protected override bool IsAvailable()
    {
        string? reason = null;
        if (ScriptPath.Value is null)
        {
            reason = "atspi-read.py helper is missing";
        }
        else if (!LinuxCommandHelper.CommandExists("python3"))
        {
            reason = "python3 is not installed";
        }
        else if (LinuxSession.IsGnome && LinuxSession.IsWayland)
        {
            reason = "GNOME Wayland does not expose the focused field over AT-SPI";
        }

        if (reason is not null && !_hintLogged)
        {
            _hintLogged = true;
            Logger.LogInformation("Correction learning unavailable: {Reason}", reason);
        }

        return reason is null;
    }

    protected override FocusedFieldSnapshot? ReadFocused()
    {
        try
        {
            var psi = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(ScriptPath.Value!);
            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(true); } catch { }
                return null;
            }

            if (p.ExitCode == 3 && !_hintLogged)
            {
                _hintLogged = true;
                Logger.LogInformation(
                    "Correction learning unavailable: install python3-gi and gir1.2-atspi-2.0 (AT-SPI bindings)");
            }

            if (p.ExitCode != 0)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stdout.Result);
            var root = doc.RootElement;
            return new FocusedFieldSnapshot(
                root.GetProperty("id").GetString() ?? "",
                root.GetProperty("app").GetString() ?? "?",
                root.GetProperty("password").GetBoolean(),
                root.GetProperty("text").ValueKind == JsonValueKind.String ? root.GetProperty("text").GetString() : null);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Correction learning: AT-SPI read failed");
            return null;
        }
    }
}
