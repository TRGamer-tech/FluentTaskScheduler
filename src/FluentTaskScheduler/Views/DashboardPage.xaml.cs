using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Views;

/// <summary>How the tasks are doing: their counts, how their runs went and when they run, and a log of the runs.</summary>
public sealed partial class DashboardPage : Page
{
    // Four tiles in a row need about this much room; with less they go two by two.
    private const double WidthForOneRowOfTiles = 720;

    private XamlRoot? _root;
    private bool _hostVisible;

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // The page lives as long as its window and is put on screen and taken off again. It is
        // off screen too while its window is hidden in the notification area or minimized.
        Loaded += async (_, _) =>
        {
            _root = XamlRoot;
            _hostVisible = _root.IsHostVisible;
            _root.Changed += Root_Changed;
            await ViewModel.ShowAsync();
        };
        Unloaded += (_, _) =>
        {
            _root!.Changed -= Root_Changed;
            ViewModel.Hide();
        };
    }

    public DashboardViewModel ViewModel { get; }

    // Also raised for a new size or scale of the window; only a change of visibility matters here.
    private async void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.IsHostVisible == _hostVisible) return;
        _hostVisible = sender.IsHostVisible;
        if (_hostVisible) await ViewModel.ShowAsync();
        else ViewModel.Hide();
    }

    private void Tiles_SizeChanged(object sender, SizeChangedEventArgs e) =>
        VisualStateManager.GoToState(this, e.NewSize.Width >= WidthForOneRowOfTiles ? "OneRow" : "TwoRows", useTransitions: false);

    private void LogFilter_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Filter = (LogFilter)sender.Items.IndexOf(sender.SelectedItem);

    private void TaskLine_ItemClick(object sender, ItemClickEventArgs e) => ViewModel.ShowTask(((TaskLine)e.ClickedItem).TaskPath);

    private void LogEntry_ItemClick(object sender, ItemClickEventArgs e) => ViewModel.ShowTask(((LogEntry)e.ClickedItem).TaskPath);
}
