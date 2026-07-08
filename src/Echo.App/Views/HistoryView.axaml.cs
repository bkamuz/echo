using Avalonia.Controls;
using Avalonia.VisualTree;
using echo.App.ViewModels;

namespace echo.App.Views;

public partial class HistoryView : UserControl
{
    private const double LoadMoreThreshold = 80;

    public HistoryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var scroll = HistoryList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
        {
            return;
        }

        scroll.ScrollChanged -= OnScrollChanged;
        scroll.ScrollChanged += OnScrollChanged;
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not HistoryViewModel vm || sender is not ScrollViewer scroll)
        {
            return;
        }

        if (!vm.HasMore || vm.IsLoadingMore || vm.IsLoadingInitial)
        {
            return;
        }

        var remaining = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y;
        if (remaining > LoadMoreThreshold)
        {
            return;
        }

        await vm.LoadMoreAsync();
    }
}
