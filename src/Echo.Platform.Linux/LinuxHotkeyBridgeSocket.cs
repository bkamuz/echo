using System.Net.Sockets;

namespace echo.Platform.Linux;

internal static class LinuxHotkeyBridgeSocket
{
    public static string CreateListenerPath()
    {
        var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
        return Path.Combine(dir, $"echo-hotkey-{Environment.ProcessId}-{Guid.NewGuid():N}.sock");
    }

    public static Socket CreateListener(string path)
    {
        Delete(path);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        socket.Listen(1);
        return socket;
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
