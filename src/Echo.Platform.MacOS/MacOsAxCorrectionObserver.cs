using System.Diagnostics;
using System.Runtime.InteropServices;
using echo.Core;
using Microsoft.Extensions.Logging;

namespace echo.Platform.MacOS;

/// <summary>
/// Correction learning on macOS: reads the focused element's AXValue through the Accessibility API
/// (AXUIElement). Requires Echo to be allowed in System Settings → Privacy &amp; Security → Accessibility.
/// </summary>
public sealed class MacOsAxCorrectionObserver : PollingCorrectionObserver
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const int MaxTextLength = 20000;

    private bool _hintLogged;

    public MacOsAxCorrectionObserver(DictionaryStore store, ILogger<MacOsAxCorrectionObserver> logger)
        : base(store, logger)
    {
    }

    protected override bool IsAvailable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            if (AXIsProcessTrusted())
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        if (!_hintLogged)
        {
            _hintLogged = true;
            Logger.LogInformation(
                "Correction learning unavailable: allow Echo in System Settings → Privacy & Security → Accessibility, then restart Echo");
        }

        return false;
    }

    protected override FocusedFieldSnapshot? ReadFocused()
    {
        var system = IntPtr.Zero;
        var focused = IntPtr.Zero;
        try
        {
            system = AXUIElementCreateSystemWide();
            if (system == IntPtr.Zero || CopyAttribute(system, "AXFocusedUIElement", out focused) != 0 || focused == IntPtr.Zero)
            {
                return null;
            }

            if (AXUIElementGetPid(focused, out var pid) != 0)
            {
                return null;
            }

            string app;
            try
            {
                using var proc = Process.GetProcessById(pid);
                app = proc.ProcessName;
            }
            catch
            {
                app = pid.ToString();
            }

            var role = CopyString(focused, "AXRole") ?? "";
            var subrole = CopyString(focused, "AXSubrole") ?? "";
            var password = subrole == "AXSecureTextField" || role == "AXSecureTextField";
            var text = password ? null : CopyString(focused, "AXValue");
            if (text is { Length: > MaxTextLength })
            {
                text = null;
            }

            var id = $"{pid}:{CFHash(focused)}:{role}";
            return new FocusedFieldSnapshot(id, app, password, text);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Correction learning: AX read failed");
            return null;
        }
        finally
        {
            if (focused != IntPtr.Zero) CFRelease(focused);
            if (system != IntPtr.Zero) CFRelease(system);
        }
    }

    private static int CopyAttribute(IntPtr element, string name, out IntPtr value)
    {
        var attr = CFStringCreateWithCString(IntPtr.Zero, name, kCFStringEncodingUTF8);
        try
        {
            return AXUIElementCopyAttributeValue(element, attr, out value);
        }
        finally
        {
            CFRelease(attr);
        }
    }

    private static string? CopyString(IntPtr element, string name)
    {
        if (CopyAttribute(element, name, out var value) != 0 || value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (CFGetTypeID(value) != CFStringGetTypeID())
            {
                return null;
            }

            var len = CFStringGetLength(value);
            var buffer = new char[len];
            CFStringGetCharacters(value, new CFRange(0, len), buffer);
            return new string(buffer);
        }
        finally
        {
            CFRelease(value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    [DllImport(AppServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(AppServices)]
    private static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(AppServices)]
    private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(AppServices)]
    private static extern int AXUIElementGetPid(IntPtr element, out int pid);

    [DllImport(CoreFoundation, CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string str, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr str);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern void CFStringGetCharacters(IntPtr str, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    private static extern nuint CFGetTypeID(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFHash(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
