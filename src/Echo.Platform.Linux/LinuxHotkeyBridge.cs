using System.Net.Sockets;
using System.Text.Json;

namespace echo.Platform.Linux;

public static class LinuxHotkeyBridge
{
    public const string Argument = "--linux-hotkey-bridge";

    public static int Run(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            Console.Error.WriteLine("linux-hotkey-bridge: socket path is required");
            return 1;
        }

        var devices = new List<int>();
        Socket? socket = null;
        StreamWriter? writer = null;
        try
        {
            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            writer = new StreamWriter(new NetworkStream(socket, ownsSocket: true))
            {
                AutoFlush = true,
            };
            socket = null;

            devices.AddRange(LinuxEvdevNative.OpenKeyboardDevices());
            writer.WriteLine(JsonSerializer.Serialize(new BridgeMeta(devices.Count)));
            if (devices.Count == 0)
            {
                Console.Error.WriteLine("linux-hotkey-bridge: no keyboard devices");
                return 1;
            }

            while (true)
            {
                var handled = false;
                foreach (var fd in devices)
                {
                    while (LinuxEvdevNative.TryReadEvent(fd, out var inputEvent))
                    {
                        handled = true;
                        if (inputEvent.Type != LinuxEvdevNative.EvKey)
                        {
                            continue;
                        }

                        writer.WriteLine(JsonSerializer.Serialize(new BridgeEvent(
                            inputEvent.Type,
                            inputEvent.Code,
                            inputEvent.Value)));
                    }
                }

                if (!handled)
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"linux-hotkey-bridge: {ex.Message}");
            return 1;
        }
        finally
        {
            writer?.Dispose();
            socket?.Dispose();
            LinuxEvdevNative.CloseDevices(devices);
        }
    }

    internal sealed record BridgeMeta(int Devices);

    internal sealed record BridgeEvent(ushort T, ushort C, int V);
}
