using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.App.Localization;
using echo.Core;

namespace echo.App.ViewModels;

public partial class DictionaryItemView : ObservableObject
{
    private readonly DictionaryViewModel _owner;

    public DictionaryItemView(DictionaryViewModel owner, DictionaryEntry entry, string usageLabel)
    {
        _owner = owner;
        Id = entry.Id;
        From = entry.From;
        To = entry.To;
        _enabled = entry.Enabled;
        UsageLabel = usageLabel;
    }

    public string Id { get; }
    public string From { get; }
    public string To { get; }
    public string UsageLabel { get; }

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _owner.SetEntryEnabled(Id, value);
}

public partial class DictionaryViewModel : ObservableObject
{
    private readonly DictionaryStore _store;
    private readonly LocalizationService _loc;
    private bool _loading;
    private string _pendingSource = "manual";

    public DictionaryViewModel(DictionaryStore store, LocalizationService loc)
    {
        _store = store;
        _loc = loc;
        _loc.LanguageChanged += (_, _) => Reload();
        Reload();
    }

    public ObservableCollection<DictionaryItemView> Entries { get; } = [];

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEntryCommand))]
    private string _fromText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEntryCommand))]
    private string _toText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(SaveLabel))]
    private string? _editingId;

    /// <summary>Full history text the user is picking a phrase from (null when not adding from history).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistorySource))]
    private string? _historySource;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool IsEditing => EditingId is not null;
    public bool HasHistorySource => !string.IsNullOrEmpty(HistorySource);
    public bool IsEmpty => Entries.Count == 0;
    public string SaveLabel => _loc.Get(IsEditing ? "Loc.Dict.Save" : "Loc.Dict.Add");
    public string Subtitle => _loc.Format("Loc.Dict.Subtitle", Entries.Count);

    public event EventHandler? FocusFromRequested;

    public void Reload()
    {
        _loading = true;
        var doc = _store.Snapshot();
        IsEnabled = doc.Enabled;
        Entries.Clear();
        foreach (var e in doc.Entries.OrderBy(e => e.From, StringComparer.CurrentCultureIgnoreCase))
        {
            Entries.Add(new DictionaryItemView(this, e, FormatUsage(e)));
        }

        _loading = false;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SaveLabel));
    }

    /// <summary>Called from History: prefill "from" with the dictated text so the user trims it to the wrong word.</summary>
    public void BeginAddFromHistory(string text)
    {
        CancelEdit();
        HistorySource = text.Trim();
        FromText = text.Trim();
        _pendingSource = "history";
        StatusMessage = string.Empty;
        FocusFromRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UseHistorySelection(string selection)
    {
        if (!string.IsNullOrWhiteSpace(selection))
        {
            FromText = selection.Trim();
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading)
        {
            _store.Update(d => d.Enabled = value);
        }
    }

    internal void SetEntryEnabled(string id, bool enabled)
    {
        if (_loading)
        {
            return;
        }

        _store.Update(d =>
        {
            var e = d.Entries.FirstOrDefault(x => x.Id == id);
            if (e is not null)
            {
                e.Enabled = enabled;
            }
        });
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(FromText) && !string.IsNullOrWhiteSpace(ToText);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveEntry()
    {
        var from = FromText.Trim();
        var to = ToText.Trim();
        var key = DictionaryMatcher.NormalizeKey(from);
        var editingId = EditingId;
        var source = _pendingSource;
        _store.Update(d =>
        {
            var target = editingId is not null
                ? d.Entries.FirstOrDefault(x => x.Id == editingId)
                : d.Entries.FirstOrDefault(x => DictionaryMatcher.NormalizeKey(x.From) == key);
            if (target is null)
            {
                d.Entries.Add(new DictionaryEntry { From = from, To = to, Source = source });
            }
            else
            {
                target.From = from;
                target.To = to;
            }
        });
        StatusMessage = _loc.Format("Loc.Dict.Saved", from, to);
        CancelEdit();
        Reload();
    }

    [RelayCommand]
    private void Edit(DictionaryItemView? item)
    {
        if (item is null)
        {
            return;
        }

        HistorySource = null;
        EditingId = item.Id;
        FromText = item.From;
        ToText = item.To;
        OnPropertyChanged(nameof(SaveLabel));
        FocusFromRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingId = null;
        HistorySource = null;
        FromText = string.Empty;
        ToText = string.Empty;
        _pendingSource = "manual";
        OnPropertyChanged(nameof(SaveLabel));
    }

    [RelayCommand]
    private void Delete(DictionaryItemView? item)
    {
        if (item is null)
        {
            return;
        }

        _store.Update(d => d.Entries.RemoveAll(x => x.Id == item.Id));
        if (EditingId == item.Id)
        {
            CancelEdit();
        }

        Reload();
    }

    public string ExportJson() => _store.ExportJson();

    public void ImportJson(string json)
    {
        try
        {
            var n = _store.ImportJson(json);
            StatusMessage = _loc.Format("Loc.Dict.Imported", n);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            StatusMessage = _loc.Get("Loc.Dict.ImportFailed");
        }

        Reload();
    }

    public void ReportExported(string path) => StatusMessage = _loc.Format("Loc.Dict.Exported", path);

    private string FormatUsage(DictionaryEntry e)
    {
        if (e.Uses == 0 || e.LastUsed is null)
        {
            return _loc.Get("Loc.Dict.NeverUsed");
        }

        return _loc.Format(
            "Loc.Dict.Usage",
            e.Uses,
            e.LastUsed.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentUICulture));
    }
}
