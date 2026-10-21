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
    private readonly DictionaryViewModel _dictionary;
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
        LocalizationService loc,
        DictionaryViewModel dictionary)
    {
        _home = home;
        _settings = settings;
        _history = history;
        _dictionary = dictionary;
        _history.AddToDictionaryRequested += (_, text) =>
        {
            Navigate(AppPage.Dictionary);
            _dictionary.BeginAddFromHistory(text);
        };
        _dictionary.PropertyChanged += (_, e) =>
        {
            if (SelectedPage == AppPage.Dictionary && e.PropertyName == nameof(DictionaryViewModel.Subtitle))
            {
                PageSubtitle = _dictionary.Subtitle;
            }
        };
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
            AppPage.Dictionary => ActivateDictionary(),
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
            AppPage.Dictionary => (_loc.Get("Loc.Shell.Dictionary.Title"), _dictionary.Subtitle),
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

    private DictionaryViewModel ActivateDictionary()
    {
        _dictionary.Reload();
        return _dictionary;
    }

    private HistoryViewModel ActivateHistory()
    {
        _ = _history.LoadInitialAsync();
        return _history;
    }
}
