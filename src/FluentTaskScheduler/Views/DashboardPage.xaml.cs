using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Views;

/// <summary>How the tasks are doing: their counts, how their runs went and when they run, and a log of the runs.</summary>
public sealed partial class DashboardPage : Page
{
    // Four tiles in a row need about this much room; with less they go two by two.
    private const double WidthForOneRowOfTiles = 720;

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // The page lives as long as its window and is put on screen and taken off again.
        Loaded += async (_, _) => await ViewModel.ShowAsync();
        Unloaded += (_, _) => ViewModel.Hide();
    }

    public DashboardViewModel ViewModel { get; }

    private void Tiles_SizeChanged(object sender, SizeChangedEventArgs e) =>
        VisualStateManager.GoToState(this, e.NewSize.Width >= WidthForOneRowOfTiles ? "OneRow" : "TwoRows", useTransitions: false);

    private void LogFilter_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Filter = (LogFilter)sender.Items.IndexOf(sender.SelectedItem);

    private void TaskLine_ItemClick(object sender, ItemClickEventArgs e) => ViewModel.ShowTask(((TaskLine)e.ClickedItem).TaskPath);

    private void LogEntry_ItemClick(object sender, ItemClickEventArgs e) => ViewModel.ShowTask(((LogEntry)e.ClickedItem).TaskPath);
}
