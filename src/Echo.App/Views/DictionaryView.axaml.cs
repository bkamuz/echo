using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using echo.App.ViewModels;

namespace echo.App.Views;

public partial class DictionaryView : UserControl
{
    private DictionaryViewModel? _vm;

    public DictionaryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.FocusFromRequested -= OnFocusFromRequested;
            }

            _vm = DataContext as DictionaryViewModel;
            if (_vm is not null)
            {
                _vm.FocusFromRequested += OnFocusFromRequested;
            }
        };
    }

    private void OnFocusFromRequested(object? sender, EventArgs e)
    {
        FromBox.Focus();
        FromBox.SelectAll();
    }

    private void OnUseSelectionClick(object? sender, RoutedEventArgs e) =>
        _vm?.UseHistorySelection(HistorySourceBox.SelectedText);

    private static readonly FilePickerFileType JsonType = new("JSON") { Patterns = ["*.json"] };

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (_vm is null || storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [JsonType],
        });
        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        _vm.ImportJson(await reader.ReadToEndAsync());
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (_vm is null || storage is null)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "echo-dictionary.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonType],
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_vm.ExportJson());
        _vm.ReportExported(file.Name);
    }
}
