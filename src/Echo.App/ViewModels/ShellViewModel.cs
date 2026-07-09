using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace echo.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly HomeViewModel _home;
    private readonly SettingsViewModel _settings;
    private readonly HistoryViewModel _history;

    public AppStatusViewModel StatusBar { get; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Home;

    public ShellViewModel(
        HomeViewModel home,
        SettingsViewModel settings,
        HistoryViewModel history,
        AppStatusViewModel statusBar)
    {
        _home = home;
        _settings = settings;
        _history = history;
        StatusBar = statusBar;
        _currentPage = home;
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = page switch
        {
            AppPage.Home => ActivateHome(),
            AppPage.Settings => ActivateSettings(),
            AppPage.History => ActivateHistory(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };
    }

    private HomeViewModel ActivateHome()
    {
        _home.NotifyConfigChanged();
        return _home;
    }

    private SettingsViewModel ActivateSettings()
    {
        _settings.RefreshDeviceOptions();
        return _settings;
    }

    private HistoryViewModel ActivateHistory()
    {
        _ = _history.LoadInitialAsync();
        return _history;
    }
}
