using System.Diagnostics;
using System.Runtime.InteropServices;

namespace echo.Platform.Windows;

/// <summary>Process integrity level helpers (UIPI diagnostics).</summary>
public static class WindowsIntegrity
{
    public const int Unknown = -1;
    public const int Low = 0x1000;
    public const int Medium = 0x2000;
    public const int High = 0x3000;
    public const int System = 0x4000;

    /// <summary>Returned when the token of a foreign process cannot be opened — almost always elevated.</summary>
    public const int AccessDenied = -2;

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    private static readonly Lazy<int> OwnLevel = new(() => GetProcessIntegrity((uint)Environment.ProcessId));

    public static int Own => OwnLevel.Value;

    /// <summary>
    /// UIPI: a lower-integrity process cannot SendInput / paste into (and on some builds cannot
    /// observe physical keys for) a higher-integrity foreground window.
    /// </summary>
    public static bool IsBlockedByUipi(int ownLevel, int targetLevel)
    {
        if (targetLevel == AccessDenied)
        {
            return ownLevel is not Unknown and < High;
        }

        if (ownLevel == Unknown || targetLevel == Unknown)
        {
            return false;
        }

        return targetLevel > ownLevel;
    }

    public static string Describe(int level) => level switch
    {
        AccessDenied => "denied(likely High)",
        Unknown => "unknown",
        < Medium => $"Low(0x{level:X})",
        < High => $"Medium(0x{level:X})",
        < System => $"High(0x{level:X})",
        _ => $"System(0x{level:X})",
    };

    public static int GetProcessIntegrity(uint pid)
    {
        if (!OperatingSystem.IsWindows() || pid == 0)
        {
            return Unknown;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() == 5 ? AccessDenied : Unknown;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return Marshal.GetLastWin32Error() == 5 ? AccessDenied : Unknown;
            }

            try
            {
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var len);
                if (len <= 0)
                {
                    return Unknown;
                }

                var buffer = Marshal.AllocHGlobal(len);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, len, out _))
                    {
                        return Unknown;
                    }

                    var sid = Marshal.ReadIntPtr(buffer);
                    var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static (nint Hwnd, uint Pid, string Name, int Level) DescribeWindow(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return (hwnd, 0, string.Empty, Unknown);
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try
        {
            name = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            name = "?";
        }

        return (hwnd, pid, name, GetProcessIntegrity(pid));
    }

    public static string FormatWindow(nint hwnd)
    {
        var (h, pid, name, level) = DescribeWindow(hwnd);
        return $"hwnd=0x{h:X} pid={pid} {name} IL={Describe(level)}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int returned);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
