using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using echo.App.ViewModels;

namespace echo.App.Views;

public partial class SettingsView : UserControl
{
    private readonly HotkeyCaptureSession _captureSession = new();
    private SettingsViewModel? _viewModel;
    private TopLevel? _captureTopLevel;
    private EventHandler<KeyEventArgs>? _tunnelKeyDown;
    private EventHandler<KeyEventArgs>? _tunnelKeyUp;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.IsCapturingHotkey))
        {
            return;
        }

        if (_viewModel?.IsCapturingHotkey == true)
        {
            _captureSession.Reset();
            StartCaptureHandlers();
            Focus();
        }
        else
        {
            _captureSession.Reset();
            StopCaptureHandlers();
        }
    }

    private void StartCaptureHandlers()
    {
        StopCaptureHandlers();

        _captureTopLevel = TopLevel.GetTopLevel(this);
        if (_captureTopLevel is null)
        {
            return;
        }

        _tunnelKeyDown = OnTunnelKeyDown;
        _tunnelKeyUp = OnTunnelKeyUp;
        _captureTopLevel.AddHandler(KeyDownEvent, _tunnelKeyDown, RoutingStrategies.Tunnel);
        _captureTopLevel.AddHandler(KeyUpEvent, _tunnelKeyUp, RoutingStrategies.Tunnel);
    }

    private void StopCaptureHandlers()
    {
        if (_captureTopLevel is null || _tunnelKeyDown is null || _tunnelKeyUp is null)
        {
            return;
        }

        _captureTopLevel.RemoveHandler(KeyDownEvent, _tunnelKeyDown);
        _captureTopLevel.RemoveHandler(KeyUpEvent, _tunnelKeyUp);
        _captureTopLevel = null;
        _tunnelKeyDown = null;
        _tunnelKeyUp = null;
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsCapturingHotkey)
        {
            return;
        }

        _captureSession.RegisterKeyDown(e);
        _viewModel.UpdateHotkeyPreview(_captureSession.Preview);
        e.Handled = true;
    }

    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsCapturingHotkey)
        {
            return;
        }

        if (_captureSession.TryCompleteOnKeyUp(e, out var hotkey))
        {
            _viewModel.ApplyCapturedHotkey(hotkey);
        }
        else
        {
            _viewModel.UpdateHotkeyPreview(_captureSession.Preview);
        }

        e.Handled = true;
    }
}
