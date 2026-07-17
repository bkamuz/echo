using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.App.Localization;

namespace echo.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly HomeViewModel _home;
    private readonly SettingsViewModel _settings;
    private readonly HistoryViewModel _history;
    private readonly LocalizationService _loc;

    public AppStatusViewModel StatusBar { get; }

    public UpdateViewModel Update { get; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Home;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageSubtitle = string.Empty;

    public ShellViewModel(
        HomeViewModel home,
        SettingsViewModel settings,
        HistoryViewModel history,
        AppStatusViewModel statusBar,
        UpdateViewModel update,
        LocalizationService loc)
    {
        _home = home;
        _settings = settings;
        _history = history;
        StatusBar = statusBar;
        Update = update;
        _loc = loc;
        _currentPage = home;
        _history.PropertyChanged += OnHistoryPropertyChanged;
        _loc.LanguageChanged += (_, _) => UpdatePageHeader(SelectedPage);
        UpdatePageHeader(AppPage.Home);
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
            AppPage.Home => (_loc.Get("Loc.Shell.Home.Title"), _loc.Get("Loc.Shell.Home.Subtitle")),
            AppPage.Settings => (_loc.Get("Loc.Shell.Settings.Title"), _loc.Get("Loc.Shell.Settings.Subtitle")),
            AppPage.History => (_loc.Get("Loc.Shell.History.Title"), _history.EntryCountLabel),
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
