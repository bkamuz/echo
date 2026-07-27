using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.Core.Update;
using Microsoft.Extensions.Logging;

namespace echo.App.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateChecker _updateChecker;
    private readonly IUpdateApplier _updateApplier;
    private readonly AppStatusViewModel _statusBar;
    private readonly LocalizationService _loc;
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
        _pendingUpdate is null
            ? string.Empty
            : _loc.Format("Loc.Update.Tooltip", _pendingUpdate.Version);

    public UpdateViewModel(
        IUpdateChecker updateChecker,
        IUpdateApplier updateApplier,
        AppStatusViewModel statusBar,
        LocalizationService loc,
        ILogger<UpdateViewModel> logger)
    {
        _updateChecker = updateChecker;
        _updateApplier = updateApplier;
        _statusBar = statusBar;
        _loc = loc;
        _logger = logger;
        _loc.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Tooltip));
        _ = CheckOnStartupAsync();
    }

    private async Task CheckOnStartupAsync()
    {
        await CheckAsync(forceRefresh: true, notifyWhenUpToDate: false).ConfigureAwait(false);
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

        if (forceRefresh)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _statusBar.SetStatus("Loc.Status.CheckingUpdates", busy: true));
        }

        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(forceRefresh).ConfigureAwait(false);
            if (result.WasSkipped)
            {
                if (forceRefresh)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _statusBar.IsBusy = false;
                        _statusBar.RefreshReadiness();
                    });
                }

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
                    _statusBar.IsBusy = false;
                    _statusBar.RefreshReadiness();
                    return;
                }

                _pendingUpdate = null;
                IsAvailable = false;
                OnPropertyChanged(nameof(Tooltip));
                ApplyCommand.NotifyCanExecuteChanged();

                if (result.CheckFailed)
                {
                    _statusBar.SetStatusTemporary("Loc.Status.UpdateCheckFailed", alert: true);
                }
                else if (notifyWhenUpToDate)
                {
                    _statusBar.SetStatusTemporary("Loc.Status.UpToDate", alert: false);
                }
                else if (forceRefresh)
                {
                    _statusBar.IsBusy = false;
                    _statusBar.RefreshReadiness();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatusTemporary("Loc.Status.UpdateCheckFailed", alert: true);
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
            var progress = new Progress<string>(text =>
                _statusBar.SetStatus(text, busy: true));
            await _updateApplier.ApplyAndRestartAsync(_pendingUpdate, progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatus("Loc.Status.Restarting", busy: true);
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    if (desktop.MainWindow is echo.App.MainWindow mainWindow)
                    {
                        mainWindow.ForceClose();
                    }

                    desktop.Shutdown();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply update");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusBar.SetStatusTemporary(_loc.Format("Loc.Status.UpdateFailed", ex.Message), alert: true);
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
