using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using echo.Core.Update;
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

    [ObservableProperty]
    private bool _isChecking;

    public string AppVersion => UpdateEnvironment.DisplayVersion;

    public bool IsCheckSupported =>
        _updateApplier.IsSupported && UpdateEnvironment.IsPublishedBuild;

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
        await CheckAsync(forceRefresh: false, notifyWhenUpToDate: false).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        await CheckAsync(forceRefresh: true, notifyWhenUpToDate: true).ConfigureAwait(false);
    }

    private bool CanCheckForUpdates() => IsCheckSupported && !IsChecking && !IsApplying;

    private async Task CheckAsync(bool forceRefresh, bool notifyWhenUpToDate)
    {
        if (!IsCheckSupported)
        {
            return;
        }

        IsChecking = true;
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(forceRefresh).ConfigureAwait(false);
            if (result.WasSkipped)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Update is not null)
                {
                    _pendingUpdate = result.Update;
                    IsAvailable = true;
                    OnPropertyChanged(nameof(Tooltip));
                    ApplyCommand.NotifyCanExecuteChanged();
                    return;
                }

                _pendingUpdate = null;
                IsAvailable = false;
                OnPropertyChanged(nameof(Tooltip));
                ApplyCommand.NotifyCanExecuteChanged();

                if (result.CheckFailed)
                {
                    _statusBar.SetStatusTemporary("Не удалось проверить обновления", alert: true);
                }
                else if (notifyWhenUpToDate)
                {
                    _statusBar.SetStatusTemporary("Установлена последняя версия", alert: false);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatusTemporary("Не удалось проверить обновления", alert: true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsChecking = false;
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
                ApplyCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private bool CanApply() => IsAvailable && !IsApplying && !IsChecking && _pendingUpdate is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        IsApplying = true;
        ApplyCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();

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
                _statusBar.SetStatusTemporary($"Не удалось обновить: {ex.Message}", alert: true);
                _statusBar.IsBusy = false;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsApplying = false;
                ApplyCommand.NotifyCanExecuteChanged();
                CheckForUpdatesCommand.NotifyCanExecuteChanged();
            });
        }
    }
}
