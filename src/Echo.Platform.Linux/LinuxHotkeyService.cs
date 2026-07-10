using System.Diagnostics;
using System.Text.Json;
using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxHotkeyService : IHotkeyService
{
    private readonly HashSet<int> _requiredKeys = [];
    private readonly HashSet<int> _pressedKeys = [];
    private readonly object _lock = new();
    private readonly List<int> _deviceHandles = [];

    private Thread? _eventThread;
    private CancellationTokenSource? _eventCts;
    private Process? _bridgeProcess;
    private StreamReader? _bridgeReader;
    private string? _bridgeSocketPath;
    private bool _usingBridge;
    private bool _active;
    private SynchronizationContext? _syncContext;

    public event Action? Activated;
    public event Action? Deactivated;

    public bool IsActive => _eventThread is not null;

    public void Configure(string hotkey)
    {
        lock (_lock)
        {
            _requiredKeys.Clear();
            foreach (var token in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var keyCode = LinuxEvdevNative.MapTokenToKeyCode(token);
                if (keyCode != 0)
                {
                    _requiredKeys.Add(keyCode);
                }
            }
        }
    }

    public void Start()
    {
        LinuxPlatformCapabilities.Refresh();

        if (_eventThread is not null)
        {
            return;
        }

        var canAccess = LinuxEvdevNative.CanAccessKeyboardDevices();
        var canBridge = !canAccess && LinuxHotkeyBridgeLauncher.CanLaunch();
        if (!canAccess && !canBridge)
        {
            return;
        }

        _syncContext = SynchronizationContext.Current;
        _eventCts = new CancellationTokenSource();
        var token = _eventCts.Token;

        if (canAccess)
        {
            _usingBridge = false;
            _deviceHandles.AddRange(LinuxEvdevNative.OpenKeyboardDevices());
            if (_deviceHandles.Count == 0)
            {
                return;
            }

            _eventThread = new Thread(() => EventLoop(token))
            {
                IsBackground = true,
                Name = "LinuxHotkeyService",
            };
            _eventThread.Start();
            return;
        }

        try
        {
            var bridge = LinuxHotkeyBridgeLauncher.Start();
            _usingBridge = true;
            _bridgeProcess = bridge.Process;
            _bridgeReader = bridge.EventReader;
            _bridgeSocketPath = bridge.SocketPath;
            _eventThread = new Thread(() => BridgeEventLoop(_bridgeReader, _bridgeProcess, token))
            {
                IsBackground = true,
                Name = "LinuxHotkeyBridgeReader",
            };
            _eventThread.Start();
        }
        catch
        {
            StopBridgeProcess();
            _usingBridge = false;
        }
    }

    public void Stop()
    {
        _eventCts?.Cancel();
        if (_eventThread is not null)
        {
            _eventThread.Join(TimeSpan.FromSeconds(2));
            _eventThread = null;
        }

        _eventCts?.Dispose();
        _eventCts = null;

        if (_usingBridge)
        {
            StopBridgeProcess();
            _usingBridge = false;
        }
        else if (_deviceHandles.Count > 0)
        {
            LinuxEvdevNative.CloseDevices(_deviceHandles);
            _deviceHandles.Clear();
        }

        lock (_lock)
        {
            _pressedKeys.Clear();
            _active = false;
        }
    }

    private void StopBridgeProcess()
    {
        try
        {
            _bridgeReader?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _bridgeReader = null;
        }

        if (_bridgeProcess is not null)
        {
            try
            {
                if (!_bridgeProcess.HasExited)
                {
                    _bridgeProcess.Kill(entireProcessTree: true);
                    _bridgeProcess.WaitForExit(2000);
                }
            }
            catch
            {
            }
            finally
            {
                _bridgeProcess.Dispose();
                _bridgeProcess = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(_bridgeSocketPath))
        {
            LinuxHotkeyBridgeSocket.Delete(_bridgeSocketPath);
            _bridgeSocketPath = null;
        }
    }

    private void EventLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _deviceHandles.Count > 0)
        {
            var handled = false;
            foreach (var fd in _deviceHandles)
            {
                while (LinuxEvdevNative.TryReadEvent(fd, out var inputEvent))
                {
                    handled = true;
                    HandleInputEvent(inputEvent);
                }
            }

            if (!handled)
            {
                Thread.Sleep(1);
            }
        }
    }

    private void BridgeEventLoop(StreamReader reader, Process process, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !process.HasExited)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                LinuxHotkeyBridge.BridgeEvent? bridgeEvent;
                try
                {
                    bridgeEvent = JsonSerializer.Deserialize<LinuxHotkeyBridge.BridgeEvent>(line);
                }
                catch
                {
                    continue;
                }

                if (bridgeEvent is null)
                {
                    continue;
                }

                HandleInputEvent(new LinuxEvdevNative.InputEvent
                {
                    Type = bridgeEvent.T,
                    Code = bridgeEvent.C,
                    Value = bridgeEvent.V,
                });
            }
        }
        catch
        {
        }
    }

    private void HandleInputEvent(LinuxEvdevNative.InputEvent inputEvent)
    {
        if (inputEvent.Type != LinuxEvdevNative.EvKey)
        {
            return;
        }

        var normalized = LinuxEvdevNative.NormalizeKeyCode(inputEvent.Code);
        var isDown = inputEvent.Value == 1;
        var isUp = inputEvent.Value == 0;

        if (!isDown && !isUp)
        {
            return;
        }

        var shouldActivate = false;
        var shouldDeactivate = false;

        lock (_lock)
        {
            if (isDown)
            {
                _pressedKeys.Add(normalized);
            }
            else if (isUp)
            {
                _pressedKeys.Remove(normalized);
            }

            var satisfied = _requiredKeys.Count > 0 && _requiredKeys.All(_pressedKeys.Contains);

            if (satisfied && !_active)
            {
                _active = true;
                shouldActivate = true;
            }
            else if (_active && isUp)
            {
                _active = false;
                _pressedKeys.Clear();
                shouldDeactivate = true;
            }
        }

        if (shouldActivate)
        {
            Post(Activated);
        }
        else if (shouldDeactivate)
        {
            Post(Deactivated);
        }
    }

    private void Post(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (_syncContext is not null)
        {
            _syncContext.Post(_ => handler(), null);
        }
        else
        {
            handler();
        }
    }
}
