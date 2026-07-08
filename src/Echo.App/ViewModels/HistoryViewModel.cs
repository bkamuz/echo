using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Core;

namespace echo.App.ViewModels;

public sealed record HistoryItemView(string Timestamp, string Engine, string Text);

public partial class HistoryViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly HistoryStore _history;
    private int _loadedCount;
    private bool _isLoading;

    public ObservableCollection<HistoryItemView> Entries { get; } = [];

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isLoadingInitial;

    [ObservableProperty]
    private bool _isLoadingMore;

    public HistoryViewModel(HistoryStore history)
    {
        _history = history;
    }

    public bool HasMore => _loadedCount < TotalCount;

    public string EntryCountLabel
    {
        get
        {
            if (TotalCount == 0)
            {
                return "Пока нет записей";
            }

            return FormatEntryCount(TotalCount);
        }
    }

    public bool IsEmpty => !IsLoadingInitial && TotalCount == 0;

    public bool HasEntries => TotalCount > 0;

    [RelayCommand]
    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var clipboard = desktop.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }

    public async Task LoadInitialAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        IsLoadingInitial = true;
        IsLoadingMore = false;
        Entries.Clear();
        _loadedCount = 0;

        try
        {
            var (total, page) = await Task.Run(() =>
            {
                var count = _history.CountEntries();
                var entries = _history.ReadPage(0, PageSize);
                return (count, entries);
            }).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TotalCount = total;
                foreach (var entry in page)
                {
                    Entries.Add(ToView(entry));
                }

                _loadedCount = page.Count;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoadingInitial = false;
                _isLoading = false;
                NotifySummaryChanged();
            });
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_isLoading || !HasMore)
        {
            return;
        }

        _isLoading = true;
        IsLoadingMore = true;

        try
        {
            var page = await Task.Run(() => _history.ReadPage(_loadedCount, PageSize)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var entry in page)
                {
                    Entries.Add(ToView(entry));
                }

                _loadedCount += page.Count;
                OnPropertyChanged(nameof(HasMore));
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoadingMore = false;
                _isLoading = false;
            });
        }
    }

    private static HistoryItemView ToView(HistoryEntry entry) =>
        new(entry.Timestamp.ToString("dd.MM.yyyy HH:mm"), entry.Engine, entry.Text);

    private static string FormatEntryCount(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return $"{count} запись";
        }

        if (mod10 is >= 2 and <= 4 && (mod100 < 10 || mod100 >= 20))
        {
            return $"{count} записи";
        }

        return $"{count} записей";
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(EntryCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasMore));
    }

    partial void OnTotalCountChanged(int value) => NotifySummaryChanged();
}
