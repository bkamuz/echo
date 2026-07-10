using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace echo.Platform.Linux;

public static class LinuxHotkeyBridgeLauncher
{
    public sealed record BridgeStartResult(
        Process Process,
        string Backend,
        int Devices,
        StreamReader EventReader,
        string SocketPath);

    public static bool CanLaunch() =>
        LinuxHotkeySetup.IsListedInInputGroup()
        && LinuxCommandHelper.CommandExists("sg");

    public static BridgeStartResult Start()
    {
        if (!CanLaunch())
        {
            throw new InvalidOperationException("Hotkey bridge requires the sg command (util-linux-extra).");
        }

        var socketPath = LinuxHotkeyBridgeSocket.CreateListenerPath();
        using var listener = LinuxHotkeyBridgeSocket.CreateListener(socketPath);
        Process? process = null;
        StreamReader? reader = null;
        try
        {
            var command = LinuxHotkeyBridgeCommand.Resolve(socketPath);
            var startInfo = BuildSgStartInfo(command.FileName, command.Arguments);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");

            if (!TryAcceptBridgeConnection(listener, process, out var clientSocket))
            {
                if (!process.HasExited)
                {
                    process.WaitForExit(500);
                }

                var connectStderr = process.StandardError.ReadToEnd().Trim();
                var detail = string.IsNullOrWhiteSpace(connectStderr)
                    ? $"exit={process.ExitCode}"
                    : $"exit={process.ExitCode}, {connectStderr}";
                throw new InvalidOperationException($"sg bridge did not connect ({detail})");
            }

            reader = new StreamReader(new NetworkStream(clientSocket, ownsSocket: true));
            if (!TryReadBridgeMeta(reader, out var devices) || devices <= 0)
            {
                var stderrText = process.StandardError.ReadToEnd().Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderrText)
                    ? $"sg bridge returned {devices} devices"
                    : stderrText);
            }

            return new BridgeStartResult(process, "Sg", devices, reader, socketPath);
        }
        catch
        {
            reader?.Dispose();
            if (process is not null)
            {
                KillQuietly(process);
            }

            LinuxHotkeyBridgeSocket.Delete(socketPath);
            throw;
        }
    }

    private static bool TryAcceptBridgeConnection(Socket listener, Process process, out Socket clientSocket)
    {
        clientSocket = null!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (listener.Poll(50_000, SelectMode.SelectRead))
            {
                clientSocket = listener.Accept();
                return true;
            }

            if (process.HasExited)
            {
                break;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static bool TryReadBridgeMeta(TextReader reader, out int devices)
    {
        devices = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (reader.Peek() >= 0)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var meta = JsonSerializer.Deserialize<LinuxHotkeyBridge.BridgeMeta>(line);
                    if (meta is not null)
                    {
                        devices = meta.Devices;
                        return true;
                    }
                }
                catch
                {
                }

                return false;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static ProcessStartInfo BuildSgStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sg",
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("input");
        startInfo.ArgumentList.Add("-c");
        var command = new StringBuilder();
        command.Append(Quote(fileName));
        foreach (var argument in arguments)
        {
            command.Append(' ');
            command.Append(Quote(argument));
        }

        startInfo.ArgumentList.Add(command.ToString());
        return startInfo;
    }

    private static string Quote(string value) =>
        value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
