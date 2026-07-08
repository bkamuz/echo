using Avalonia.Controls;
using Avalonia.Input;
using echo.App.ViewModels;

namespace echo.App.Views;

public partial class SettingsView : UserControl
{
    private readonly HotkeyCaptureSession _captureSession = new();
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
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
        if (e.PropertyName == nameof(SettingsViewModel.IsCapturingHotkey))
        {
            if (_viewModel?.IsCapturingHotkey == true)
            {
                _captureSession.Reset();
                Focus();
            }
            else
            {
                _captureSession.Reset();
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsCapturingHotkey)
        {
            return;
        }

        _captureSession.RegisterKeyDown(e);
        _viewModel.UpdateHotkeyPreview(_captureSession.Preview);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
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
