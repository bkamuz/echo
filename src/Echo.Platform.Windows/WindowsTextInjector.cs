using System.Runtime.InteropServices;
using System.Text;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsTextInjector : ITextInjector
{
    private const int InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const uint CfUnicode = 13;
    private const uint GmemMoveable = 0x0002;
    private const int ClipboardRestoreDelayMs = 400;

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

        if (method is "clipboard" or "auto")
        {
            var outcome = TryInjectViaClipboard(text);
            var resolved = WindowsClipboardInjectPolicy.Resolve(method, outcome);
            if (resolved is not null)
            {
                return Task.FromResult(resolved);
            }
        }

        TypeText(text, typeDelayMs);
        return Task.FromResult(TextInjectionResult.AutoPasted);
    }

    private static void TypeText(string text, int typeDelayMs)
    {
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
                SendUnicodeCodeUnit((ushort)ch);
                SendUnicodeCodeUnit((ushort)text[i + 1]);
                i++;
            }
            else
            {
                SendUnicodeCodeUnit((ushort)ch);
            }

            if (typeDelayMs > 0 && HasMoreChars(text, i))
            {
                Thread.Sleep(typeDelayMs);
            }
        }
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

    private static void SendUnicodeCodeUnit(ushort code)
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
        _ = SendInput(2, inputs, InputSize);
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
    private static ClipboardPasteOutcome TryInjectViaClipboard(string text)
    {
        string? savedText = null;
        var hadText = false;

        if (!OpenClipboard(IntPtr.Zero))
        {
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

        if (!TrySetClipboardText(text))
        {
            return ClipboardPasteOutcome.FailedBeforePaste;
        }

        Thread.Sleep(30);
        SendCtrlV();

        ScheduleClipboardRestore(hadText ? savedText : null);

        return ClipboardPasteOutcome.Pasted;
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

    private static void SendCtrlV()
    {
        const ushort vkControl = 0x11;
        const ushort vkV = 0x56;
        Input[] inputs =
        [
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkControl } } },
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkV } } },
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkV, DwFlags = KeyeventfKeyup } } },
            new() { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vkControl, DwFlags = KeyeventfKeyup } } },
        ];
        _ = SendInput(4, inputs, InputSize);
    }

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
