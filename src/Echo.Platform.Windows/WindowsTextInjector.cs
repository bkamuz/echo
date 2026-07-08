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

    /// <summary>
    /// Small pause between keystrokes — prevents target apps from dropping spaces/letters
    /// when SendInput is flooded (original Python supported optional delay; 0 was default).
    /// </summary>
    private const int InterKeyDelayMs = 1;

    public Task InjectAsync(string text, string method, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        if (method == "clipboard" && TryInjectViaClipboard(text))
        {
            return Task.CompletedTask;
        }

        TypeText(text);
        return Task.CompletedTask;
    }

    private static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                SendVk(0x0D);
            }
            else if (ch != '\r')
            {
                foreach (var code in Utf16CodeUnits(ch))
                {
                    SendUnicodeCodeUnit(code);
                }
            }

            if (InterKeyDelayMs > 0)
            {
                Thread.Sleep(InterKeyDelayMs);
            }
        }
    }

    private static bool TryInjectViaClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            try
            {
                EmptyClipboard();

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

                hGlobal = IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }

            Thread.Sleep(30);
            SendCtrlV();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<ushort> Utf16CodeUnits(char ch)
    {
        var encoded = Encoding.Unicode.GetBytes([ch]);
        for (var i = 0; i < encoded.Length; i += 2)
        {
            yield return (ushort)(encoded[i] | (encoded[i + 1] << 8));
        }
    }

    private static void SendUnicodeCodeUnit(ushort code)
    {
        var down = new Input
        {
            Type = InputKeyboard,
            U = new InputUnion { Ki = new KeyboardInput { WScan = code, DwFlags = KeyeventfUnicode } },
        };
        var up = new Input
        {
            Type = InputKeyboard,
            U = new InputUnion { Ki = new KeyboardInput { WScan = code, DwFlags = KeyeventfUnicode | KeyeventfKeyup } },
        };
        _ = SendInput(2, [down, up], InputSize);
    }

    private static void SendVk(ushort vk)
    {
        var down = new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vk } } };
        var up = new Input { Type = InputKeyboard, U = new InputUnion { Ki = new KeyboardInput { WVk = vk, DwFlags = KeyeventfKeyup } } };
        _ = SendInput(2, [down, up], InputSize);
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
