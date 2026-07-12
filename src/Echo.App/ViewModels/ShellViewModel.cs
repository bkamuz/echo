using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace echo.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly HomeViewModel _home;
    private readonly SettingsViewModel _settings;
    private readonly HistoryViewModel _history;

    public AppStatusViewModel StatusBar { get; }

    public UpdateViewModel Update { get; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Home;

    [ObservableProperty]
    private string _pageTitle = "Главная";

    [ObservableProperty]
    private string _pageSubtitle = "Диктовка с локальным распознаванием речи";

    public ShellViewModel(
        HomeViewModel home,
        SettingsViewModel settings,
        HistoryViewModel history,
        AppStatusViewModel statusBar,
        UpdateViewModel update)
    {
        _home = home;
        _settings = settings;
        _history = history;
        StatusBar = statusBar;
        Update = update;
        _currentPage = home;
        _history.PropertyChanged += OnHistoryPropertyChanged;
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        UpdatePageHeader(page);
        CurrentPage = page switch
        {
            AppPage.Home => ActivateHome(),
            AppPage.Settings => ActivateSettings(),
            AppPage.History => ActivateHistory(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };
    }

    private void UpdatePageHeader(AppPage page)
    {
        (PageTitle, PageSubtitle) = page switch
        {
            AppPage.Home => ("Главная", "Диктовка с локальным распознаванием речи"),
            AppPage.Settings => ("Настройки", "Хоткей, движок, микрофон и способ ввода текста"),
            AppPage.History => ("История", _history.EntryCountLabel),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
    }

    private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SelectedPage == AppPage.History && e.PropertyName == nameof(HistoryViewModel.EntryCountLabel))
        {
            PageSubtitle = _history.EntryCountLabel;
        }
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
