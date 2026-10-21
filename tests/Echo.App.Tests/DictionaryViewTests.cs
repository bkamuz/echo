using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using echo.App.Localization;
using echo.App.ViewModels;
using echo.App.Views;
using echo.Core;

namespace echo.App.Tests;

public sealed class DictionaryViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "echo-dict-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaFact]
    public void AddFromHistory_SelectPhrase_SaveAndApply()
    {
        var store = new DictionaryStore(Path.Combine(_dir, "dictionary.json"));
        var loc = new LocalizationService();
        var vm = new DictionaryViewModel(store, loc);
        var history = new HistoryViewModel(new HistoryStore(), loc);
        history.AddToDictionaryRequested += (_, text) => vm.BeginAddFromHistory(text);

        var window = new Window { Width = 900, Height = 600, Content = new DictionaryView { DataContext = vm } };
        window.Show();
        try
        {
            history.AddToDictionaryCommand.Execute("Открой гит хаб пожалуйста");
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.HasHistorySource);

            var source = Find<TextBox>(window, "HistorySourceBox");
            source.SelectionStart = 7;
            source.SelectionEnd = 14;
            var useSelection = Find<Button>(window, "UseSelectionButton");
            useSelection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("гит хаб", vm.FromText);

            Find<TextBox>(window, "ToBox").Text = "GitHub";
            var save = Find<Button>(window, "SaveButton");
            Assert.True(save.Command!.CanExecute(null));
            save.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(vm.Entries);
            Assert.False(vm.HasHistorySource);
            Assert.Equal("Открой GitHub пожалуйста", store.Apply("Открой гит хаб пожалуйста").Text);
            Assert.Equal("history", store.Snapshot().Entries[0].Source);

            vm.Entries[0].Enabled = false;
            Assert.Equal("гит хаб", store.Apply("гит хаб").Text);

            vm.Entries[0].Enabled = true;
            Find<CheckBox>(window, "DictEnabled").IsChecked = false;
            Assert.False(store.Snapshot().Enabled);
            Assert.Equal("гит хаб", store.Apply("гит хаб").Text);

            vm.DeleteCommand.Execute(vm.Entries[0]);
            Assert.Empty(store.Snapshot().Entries);
        }
        finally
        {
            window.Close();
        }
    }

    private static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}

public sealed class DictionarySuggestionsViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "echo-sugg-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaFact]
    public void Suggestions_AcceptAndReject()
    {
        var store = new DictionaryStore(Path.Combine(_dir, "dictionary.json"));
        var vm = new DictionaryViewModel(store, new LocalizationService());
        var window = new Window { Width = 900, Height = 600, Content = new DictionaryView { DataContext = vm } };
        window.Show();
        try
        {
            var tile = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SuggestionsTile");
            Assert.False(tile.IsVisible);

            store.AddSuggestions([new CorrectionCandidate("паркет", "Parakeet"), new CorrectionCandidate("эхо", "Echo")]);
            store.AddSuggestions([new CorrectionCandidate("паркет", "Parakeet")]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tile.IsVisible);
            Assert.Equal(2, vm.Suggestions.Count);
            Assert.Equal("Parakeet", vm.Suggestions[0].To); // most seen first
            Assert.Empty(vm.Entries);

            window.UpdateLayout();
            var accept = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "AcceptSuggestionButton");
            accept.Command!.Execute(accept.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(vm.Entries);
            Assert.Equal("Parakeet", store.Apply("паркет").Text);

            window.UpdateLayout();
            var reject = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RejectSuggestionButton");
            reject.Command!.Execute(reject.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(vm.Suggestions);
            Assert.False(tile.IsVisible);
            Assert.Single(store.Snapshot().Rejected);
        }
        finally
        {
            window.Close();
        }
    }
}
