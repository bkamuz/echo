using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace echo.App.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateChecker _updateChecker;
    private readonly IUpdateApplier _updateApplier;
    private readonly AppStatusViewModel _statusBar;
    private readonly ILogger<UpdateViewModel> _logger;
    private UpdateInfo? _pendingUpdate;

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _isApplying;

    public string Tooltip =>
        _pendingUpdate is null ? string.Empty : $"Обновить до v{_pendingUpdate.Version}";

    public UpdateViewModel(
        IUpdateChecker updateChecker,
        IUpdateApplier updateApplier,
        AppStatusViewModel statusBar,
        ILogger<UpdateViewModel> logger)
    {
        _updateChecker = updateChecker;
        _updateApplier = updateApplier;
        _statusBar = statusBar;
        _logger = logger;
        _ = CheckOnStartupAsync();
    }

    private async Task CheckOnStartupAsync()
    {
        try
        {
            var update = await _updateChecker.CheckForUpdateAsync().ConfigureAwait(false);
            if (update is null || !_updateApplier.IsSupported)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pendingUpdate = update;
                IsAvailable = true;
                OnPropertyChanged(nameof(Tooltip));
                ApplyCommand.NotifyCanExecuteChanged();
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Startup update check failed");
        }
    }

    private bool CanApply() => IsAvailable && !IsApplying && _pendingUpdate is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        IsApplying = true;
        ApplyCommand.NotifyCanExecuteChanged();

        try
        {
            var progress = new Progress<string>(text => _statusBar.SetStatus(text, busy: true));
            await _updateApplier.ApplyAndRestartAsync(_pendingUpdate, progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatus("Перезапуск…", busy: true);
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply update");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatusTemporary($"Не удалось обновить: {ex.Message}");
                _statusBar.IsBusy = false;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsApplying = false;
                ApplyCommand.NotifyCanExecuteChanged();
            });
        }
    }
}
