using System.Runtime.InteropServices;
using System.Text;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Platform.Windows;

public sealed class WindowsTextInjector : ITextInjector
{
    private const int InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint CfUnicode = 13;
    private const uint GmemMoveable = 0x0002;
    private const int ClipboardRestoreDelayMs = 400;

    private readonly ILogger _logger;

    public WindowsTextInjector()
        : this(NullLogger<WindowsTextInjector>.Instance)
    {
    }

    public WindowsTextInjector(ILogger<WindowsTextInjector> logger)
    {
        _logger = logger;
    }

    public Task<TextInjectionResult> InjectAsync(
        string text,
        string method,
        int typeDelayMs = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(TextInjectionResult.AutoPasted);
        }

        var foreground = GetForegroundWindow();
        var target = WindowsIntegrity.DescribeWindow(foreground);
        _logger.LogInformation(
            "Inject start: method={Method} chars={Chars} echoIL={Own} foreground={Fg}",
            method,
            text.Length,
            WindowsIntegrity.Describe(WindowsIntegrity.Own),
            WindowsIntegrity.FormatWindow(foreground));

        if (target.Pid != (uint)Environment.ProcessId
            && WindowsIntegrity.IsBlockedByUipi(WindowsIntegrity.Own, target.Level))
        {
            // UIPI silently drops SendInput into a higher-integrity window.
            _logger.LogWarning(
                "Inject blocked by UIPI: target {Name} (pid {Pid}) runs elevated, Echo IL={Own}. Run Echo as administrator to paste into elevated apps.",
                target.Name,
                target.Pid,
                WindowsIntegrity.Describe(WindowsIntegrity.Own));
            return Task.FromResult(TextInjectionResult.Failed("Loc.Inject.Elevated"));
        }

        if (method is "clipboard" or "auto")
        {
            var outcome = TryInjectViaClipboard(text);
            _logger.LogInformation(
                "Clipboard inject outcome={Outcome} foregroundAfter={Fg}",
                outcome,
                WindowsIntegrity.FormatWindow(GetForegroundWindow()));
            var resolved = WindowsClipboardInjectPolicy.Resolve(method, outcome);
            if (resolved is not null)
            {
                return Task.FromResult(resolved);
            }
        }

        var sent = TypeText(text, typeDelayMs);
        _logger.LogInformation("Typed via SendInput: {Sent}/{Total} code units", sent, text.Length);
        return Task.FromResult(TextInjectionResult.AutoPasted);
    }

    private static int TypeText(string text, int typeDelayMs)
    {
        var sent = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                SendEnter();
            }
            else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sent += SendUnicodeCodeUnit((ushort)ch) ? 1 : 0;
                sent += SendUnicodeCodeUnit((ushort)text[i + 1]) ? 1 : 0;
                i++;
            }
            else
            {
                sent += SendUnicodeCodeUnit((ushort)ch) ? 1 : 0;
            }

            if (typeDelayMs > 0 && HasMoreChars(text, i))
            {
                Thread.Sleep(typeDelayMs);
            }
        }

        return sent;
    }

    private static bool HasMoreChars(string text, int index)
    {
        for (var j = index + 1; j < text.Length; j++)
        {
            if (text[j] != '\r')
            {
                return true;
            }
        }

        return false;
    }

    private static bool SendUnicodeCodeUnit(ushort code)
    {
        Input[] inputs =
        [
            new()
            {
                Type = InputKeyboard,
                U = new InputUnion { Ki = new KeyboardInput { WScan = code, DwFlags = KeyeventfUnicode } },
            },
            new()
            {
                Type = InputKeyboard,
                U = new InputUnion
                {
                    Ki = new KeyboardInput { WScan = code, DwFlags = KeyeventfUnicode | KeyeventfKeyup },
                },
            },
        ];
        return SendInput(2, inputs, InputSize) == 2;
    }

    private static void SendEnter()
    {
        const ushort vkReturn = 0x0D;
        Input[] inputs =
        [
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkReturn } } },
            new()
            {
                Type = InputKeyboard,
                U = new InputUnion { Ki = new KeyboardInput { WVk = vkReturn, DwFlags = KeyeventfKeyup } },
            },
        ];
        _ = SendInput(2, inputs, InputSize);
    }

    /// <summary>
    /// Saves text clipboard (CF_UNICODETEXT only), pastes dictation text, then restores the saved content.
    /// Images and other formats are not preserved.
    /// Success is paste itself; restore/clear is best-effort and must not trigger type fallback.
    /// </summary>
    private ClipboardPasteOutcome TryInjectViaClipboard(string text)
    {
        string? savedText = null;
        var hadText = false;

        if (!OpenClipboardWithRetry())
        {
            _logger.LogWarning(
                "OpenClipboard failed, Win32 error {Error}, held by {Owner}",
                Marshal.GetLastWin32Error(),
                WindowsIntegrity.FormatWindow(GetOpenClipboardWindow()));
            return ClipboardPasteOutcome.FailedBeforePaste;
        }

        try
        {
            if (IsClipboardFormatAvailable(CfUnicode))
            {
                savedText = ReadClipboardUnicodeText();
                hadText = savedText is not null;
            }
        }
        finally
        {
            CloseClipboard();
        }

        if (!TrySetClipboardText(text) && !TrySetClipboardText(text))
        {
            _logger.LogWarning("SetClipboardData failed, Win32 error {Error}", Marshal.GetLastWin32Error());
            return ClipboardPasteOutcome.FailedBeforePaste;
        }

        // Give clipboard viewers / OLE a beat before synthesizing Ctrl+V.
        Thread.Sleep(50);
        var ctrlV = SendCtrlV();
        if (ctrlV != 4)
        {
            _logger.LogWarning(
                "SendInput(Ctrl+V) injected {Count}/4, Win32 error {Error} (UIPI or secure desktop?)",
                ctrlV,
                Marshal.GetLastWin32Error());
            return ClipboardPasteOutcome.FailedBeforePaste;
        }

        // Let the target process begin handling WM_PASTE before we return
        // (toast/UI work). Clipboard restore stays deferred — Chromium reads async.
        Thread.Sleep(50);
        ScheduleClipboardRestore(hadText ? savedText : null);

        // Soft heuristic: if Echo owns foreground after Ctrl+V, paste likely missed the target.
        if (IsOwnProcessForeground())
        {
            _logger.LogWarning("After Ctrl+V the foreground window belongs to Echo — paste target was Echo itself or focus was stolen");
            return ClipboardPasteOutcome.FocusStolen;
        }

        return ClipboardPasteOutcome.Pasted;
    }

    private static bool OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }

    private static bool IsOwnProcessForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Chromium/Electron paste handlers read the clipboard asynchronously; restoring too
    /// early makes Ctrl+V insert the previous clip instead of the dictation text.
    /// </summary>
    private static void ScheduleClipboardRestore(string? savedText)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ClipboardRestoreDelayMs).ConfigureAwait(false);
                if (savedText is not null)
                {
                    TryRestoreClipboardWithRetry(savedText);
                }
                else
                {
                    TryClearClipboardWithRetry();
                }
            }
            catch
            {
                // Best-effort restore — dictation text is already in history/toast.
            }
        });
    }

    private static void TryRestoreClipboardWithRetry(string savedText)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (TrySetClipboardText(savedText))
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static void TryClearClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (TryClearClipboard())
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static string? ReadClipboardUnicodeText()
    {
        var handle = GetClipboardData(CfUnicode);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var source = GlobalLock(handle);
        if (source == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(source);
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero)
            {
                return false;
            }

            var target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CfUnicode, hGlobal) == IntPtr.Zero)
            {
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryClearClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            return EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static uint SendCtrlV()
    {
        const ushort vkControl = 0x11;
        const ushort vkV = 0x56;
        Input[] inputs =
        [
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkControl } } },
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkV } } },
            new()
            {
                Type = InputKeyboard,
                U = new InputUnion { Ki = new KeyboardInput { WVk = vkV, DwFlags = KeyeventfKeyup } },
            },
            new()
            {
                Type = InputKeyboard,
                U = new InputUnion { Ki = new KeyboardInput { WVk = vkControl, DwFlags = KeyeventfKeyup } },
            },
        ];
        return SendInput(4, inputs, InputSize);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private static readonly int InputSize = Marshal.SizeOf<Input>();

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
