using System.Globalization;
using System.Runtime.InteropServices;

namespace echo.Platform.Linux;

internal static class LinuxEvdevNative
{
    internal const ushort EvKey = 1;
    internal const int InputEventSize = 24;

    private const uint EvIocGbitEventTypes = (2U << 30) | (0x45U << 8) | 0x20U | (64U << 16);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, IntPtr arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, byte[] buffer, int count);

    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x800;

    public static IReadOnlyList<int> OpenKeyboardDevices()
    {
        var devices = new List<int>();
        if (!Directory.Exists("/dev/input"))
        {
            return devices;
        }

        foreach (var path in Directory.GetFiles("/dev/input", "event*").Order(StringComparer.Ordinal))
        {
            if (!HasKeyEvents(path))
            {
                continue;
            }

            var fd = open(path, O_RDONLY | O_NONBLOCK);
            if (fd >= 0)
            {
                devices.Add(fd);
            }
        }

        return devices;
    }

    public static void CloseDevices(IEnumerable<int> fds)
    {
        foreach (var fd in fds)
        {
            _ = close(fd);
        }
    }

    public static bool TryReadEvent(int fd, out InputEvent ev)
    {
        ev = default;
        var buffer = new byte[InputEventSize];
        var readBytes = read(fd, buffer, buffer.Length);
        if (readBytes != InputEventSize)
        {
            return false;
        }

        ev.Type = BitConverter.ToUInt16(buffer, 16);
        ev.Code = BitConverter.ToUInt16(buffer, 18);
        ev.Value = BitConverter.ToInt32(buffer, 20);
        return true;
    }

    public static bool CanAccessKeyboardDevices()
    {
        if (!Directory.Exists("/dev/input"))
        {
            return false;
        }

        foreach (var path in Directory.GetFiles("/dev/input", "event*"))
        {
            if (!HasKeyEvents(path))
            {
                continue;
            }

            var fd = open(path, O_RDONLY | O_NONBLOCK);
            if (fd >= 0)
            {
                _ = close(fd);
                return true;
            }
        }

        return false;
    }

    private static bool HasKeyEvents(string path)
    {
        var fd = open(path, O_RDONLY);
        if (fd < 0)
        {
            return false;
        }

        try
        {
            var bitBuffer = Marshal.AllocHGlobal(64);
            try
            {
                if (ioctl(fd, EvIocGbitEventTypes, bitBuffer) < 0)
                {
                    return false;
                }

                var bits = new byte[64];
                Marshal.Copy(bitBuffer, bits, 0, bits.Length);
                return (bits[0] & 0x02) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(bitBuffer);
            }
        }
        finally
        {
            _ = close(fd);
        }
    }

    public static int NormalizeKeyCode(int code) => code switch
    {
        29 or 97 => 29, // ctrl
        42 or 54 => 42, // shift
        56 or 100 => 56, // alt
        125 or 126 => 125, // meta / super
        >= 30 and <= 38 => code, // a-i
        >= 44 and <= 50 => code, // z-m
        >= 2 and <= 11 => code, // 1-0
        >= 59 and <= 70 => code, // f1-f12
        _ => code,
    };

    public static int MapTokenToKeyCode(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 29,
        "cmd" or "win" or "windows" => 125,
        "alt" => 56,
        "shift" => 42,
        _ when token.Length == 1 && token[0] is >= 'a' and <= 'z' => token[0] - 'a' + 30,
        _ when token.Length == 1 && token[0] is >= '0' and <= '9' => token[0] == '0' ? 11 : token[0] - '1' + 2,
        _ when token.StartsWith('f') && int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var fn) && fn is >= 1 and <= 12
            => 58 + fn,
        _ => 0,
    };

    internal struct InputEvent
    {
        public ushort Type;
        public ushort Code;
        public int Value;
    }
}
