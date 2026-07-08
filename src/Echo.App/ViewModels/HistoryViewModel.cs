using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.App.Services;
using echo.Core;

namespace echo.App.ViewModels;

public sealed record HistoryItemView(string Timestamp, string Engine, string Text);

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryStore _history;
    private readonly IAppClipboard _clipboard;

    [ObservableProperty]
    private IReadOnlyList<HistoryItemView> _entries = [];

    public HistoryViewModel(HistoryStore history, IAppClipboard clipboard)
    {
        _history = history;
        _clipboard = clipboard;
        Refresh();
    }

    public string EntryCountLabel
    {
        get
        {
            var count = Entries.Count;
            if (count == 0)
            {
                return "Пока нет записей";
            }

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
    }

    public bool IsEmpty => Entries.Count == 0;

    public bool HasEntries => Entries.Count > 0;

    [RelayCommand]
    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await _clipboard.SetTextAsync(text);
    }

    public void Refresh()
    {
        Entries = _history.ReadAll()
            .Select(e => new HistoryItemView(
                e.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                e.Engine,
                e.Text))
            .ToList();

        OnPropertyChanged(nameof(EntryCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEntries));
    }
}
