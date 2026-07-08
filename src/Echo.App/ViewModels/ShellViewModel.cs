using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace echo.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly HomeViewModel _home;
    private readonly SettingsViewModel _settings;
    private readonly HistoryViewModel _history;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Home;

    public ShellViewModel(
        HomeViewModel home,
        SettingsViewModel settings,
        HistoryViewModel history)
    {
        _home = home;
        _settings = settings;
        _history = history;
        _currentPage = home;
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = page switch
        {
            AppPage.Home => ActivateHome(),
            AppPage.Settings => _settings,
            AppPage.History => ActivateHistory(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };
    }

    private HomeViewModel ActivateHome()
    {
        _home.NotifyConfigChanged();
        return _home;
    }

    private HistoryViewModel ActivateHistory()
    {
        _history.Refresh();
        return _history;
    }
}
