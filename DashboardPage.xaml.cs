using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Windows.Foundation;

namespace FluentTaskScheduler
{
    public sealed partial class DashboardPage : Page
    {
        public DashboardViewModel ViewModel { get; }

        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        public DashboardPage()
        {
            this.InitializeComponent();
            ViewModel = new DashboardViewModel();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            // Subscriptions are armed in Loaded and torn down in Unloaded, never in this
            // constructor. NavigationCacheMode.Required means this instance is reused for the life
            // of its Frame, so the constructor runs exactly once: setting them up here left the
            // page permanently deaf after the first navigate-away unsubscribed it, freezing the
            // labels in whatever language was active at first construction and stranding the view
            // model's filter labels in the old locale (which then matched nothing and zeroed out
            // every statistic). Tearing down on Unloaded still avoids the leak in 3.3 — the page,
            // its Frame and its window stay collectable once the window closes.
            this.Loaded += DashboardPage_Loaded;
            this.Unloaded += DashboardPage_Unloaded;
        }

        private void DashboardPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Loaded fires again every time a cached page is navigated back to, so re-arm
            // idempotently (-= before += is a no-op when not currently subscribed).
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            ViewModel.HealthScoreChanged -= DashboardViewModel_HealthScoreChanged;
            ViewModel.HealthScoreChanged += DashboardViewModel_HealthScoreChanged;
            ViewModel.Resume();

            // Picks up any language change that happened while this page was off-screen.
            ApplyLocalizedUi();
        }

        private void DashboardPage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ViewModel.HealthScoreChanged -= DashboardViewModel_HealthScoreChanged;
            ViewModel.Cleanup();
        }

        private void DashboardViewModel_HealthScoreChanged(object? sender, EventArgs e) => DrawHealthRing();

        // ── Health ring ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the health arc. WinUI has no built-in ring gauge, so the arc is a Path whose
        /// geometry is recomputed whenever the score changes.
        /// </summary>
        private void DrawHealthRing()
        {
            if (HealthRingArc == null) return;

            const double size = 76;
            const double stroke = 8;
            double radius = (size - stroke) / 2;
            var centre = new Point(size / 2, size / 2);

            double fraction = Math.Clamp(ViewModel.HealthScore / 100.0, 0, 1);

            // A full circle cannot be expressed as a single arc segment (start == end).
            if (fraction <= 0)
            {
                HealthRingArc.Data = null;
                return;
            }
            if (fraction >= 1) fraction = 0.9999;

            double angle = fraction * 2 * Math.PI;
            var start = new Point(centre.X, centre.Y - radius);            // 12 o'clock
            var end = new Point(
                centre.X + radius * Math.Sin(angle),
                centre.Y - radius * Math.Cos(angle));

            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = fraction > 0.5,
                SweepDirection = SweepDirection.Clockwise
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            HealthRingArc.Data = geometry;

            HealthRingArc.Stroke = ViewModel.HealthScore switch
            {
                >= 90 => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
                >= 70 => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                _ => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            };
        }

        private void LocalizationService_LanguageChanged(object? sender, System.EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(ApplyLocalizedUi);
        }

        private void ApplyLocalizedUi()
        {
            string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

            DashboardRefreshBtn.Text = L("DashboardRefreshBtn.Text", "Refresh Dashboard");
            DashboardFilterCategory.Text = L("DashboardFilterCategory.Text", "Filter by Category");
            DashboardFilterTag.Text = L("DashboardFilterTag.Text", "Filter by Tag");
            DashboardTotalTasks.Text = L("DashboardTotalTasks.Text", "Total Tasks");
            DashboardEnabled.Text = L("DashboardEnabled.Text", "Enabled");
            DashboardDisabled.Text = L("DashboardDisabled.Text", "Disabled");
            DashboardRunning.Text = L("DashboardRunning.Text", "Running");
            DashboardActiveProcesses.Text = L("DashboardActiveProcesses.Text", "Active Processes");
            DashboardHealthScore.Text = L("DashboardHealthScore.Text", "Health Score");
            DashboardRecentSuccess.Text = L("DashboardRecentSuccess.Text", "Recent Success");
            DashboardRecentChecked.Text = L("DashboardRecentChecked.Text", "Tasks Checked Recently");
            DashboardRecentFailure.Text = L("DashboardRecentFailure.Text", "Recent Failure");
            DashboardNeedsAttention.Text = L("DashboardNeedsAttention.Text", "Needs Attention");
            DashboardCurrentlyRunning.Text = L("DashboardCurrentlyRunning.Text", "Currently Running Tasks");
            DashboardNoRunningTasks.Text = L("DashboardNoRunningTasks.Text", "No tasks running currently");
            DashboardRecentlyFailed.Text = L("DashboardRecentlyFailed.Text", "Recently Failed Tasks");
            DashboardNoRecentFailures.Text = L("DashboardNoRecentFailures.Text", "No recent failures, tasks are in good health");
            DashboardUpcomingSchedule.Text = L("DashboardUpcomingSchedule.Text", "Upcoming Schedule");
            DashboardActivityStream.Text = L("DashboardActivityStream.Text", "Recent Activity Stream");
            DashboardRunHistory.Text = L("DashboardRunHistory.Text", "Run History - Last 7 Days");
            DashboardSuccess.Text = L("DashboardSuccess.Text", "Success");
            DashboardFailure.Text = L("DashboardFailure.Text", "Failure");

            // v1.9 analytics
            DashboardHealthCaption.Text = L("Dashboard.HealthCaption", "Share of runs that finished with exit code 0 in the last 7 days.");
            DashboardThroughput.Text = L("Dashboard.Throughput", "Executions");
            DashboardRuns24hLabel.Text = L("Dashboard.Runs24h", "in the last 24h");
            DashboardRuns7dLabel.Text = L("Dashboard.Runs7d", "in the last 7 days");
            DashboardAvgDuration.Text = L("Dashboard.AvgDuration", "Average Duration");
            DashboardAvgDurationHint.Text = L("Dashboard.AvgDurationHint", "Across all completed runs (7 days)");
            DashboardLongestTask.Text = L("Dashboard.LongestTask", "Longest Run");
            DashboardHeatmapTitle.Text = L("Dashboard.HeatmapTitle", "Execution Heatmap - by hour of day");
            DashboardPeakHourLabel.Text = L("Dashboard.PeakHour", "Busiest hour:");
            DashboardExecutionStream.Text = L("Dashboard.ExecutionStream", "Execution Log Stream");
            DashboardNoExecutionEntries.Text = L("Dashboard.NoExecutionEntries", "No runs recorded for this filter.");

            ExecFilterAll.Content = L("Dashboard.Filter.All", "All");
            ExecFilterSuccess.Content = L("Dashboard.Status.Success", "Success");
            ExecFilterFailed.Content = L("Dashboard.Status.Failed", "Failed");
            ExecFilterSnoozed.Content = L("Dashboard.Status.Snoozed", "Snoozed");
        }

        private void ExecutionFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;

            string filter = clicked.Tag?.ToString() ?? "All";
            ViewModel.ExecutionFilter = filter;

            // Behave like a radio group: exactly one filter stays checked.
            foreach (var button in new[] { ExecFilterAll, ExecFilterSuccess, ExecFilterFailed, ExecFilterSnoozed })
                button.IsChecked = ReferenceEquals(button, clicked);
        }

        private void ExecutionEntry_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ExecutionLogEntry entry && !string.IsNullOrEmpty(entry.TaskPath))
            {
                ViewModel.NavigateToTask(entry.TaskPath);
            }
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!ViewModel.IsLoading)
            {
                await ViewModel.LoadDashboardData();
            }
            ViewModel.StartAutoRefresh();
            RefreshFilterScrollArrows();
        }

        private void RefreshFilterScrollArrows()
        {
            // Defer one frame so the ItemsControls have measured their (possibly new) content width.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                UpdateFilterScrollArrows(CategoryScrollViewer, CategoryScrollLeftBtn, CategoryScrollRightBtn);
                UpdateFilterScrollArrows(TagScrollViewer, TagScrollLeftBtn, TagScrollRightBtn);
            });
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.StopAutoRefresh();
        }

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            PageScrollViewer.IsScrollInertiaEnabled = Settings.SmoothScrolling;
            DrawHealthRing();
        }
        private async void DashboardReload_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.LoadDashboardData();
            RefreshFilterScrollArrows();
        }

        private void ActivityList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FluentTaskScheduler.Models.TaskHistoryEntry entry && !string.IsNullOrEmpty(entry.TaskPath))
            {
                ViewModel.NavigateToTask(entry.TaskPath);
            }
        }

        private void RunningTask_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FluentTaskScheduler.ViewModels.RunningTaskInfo info && !string.IsNullOrEmpty(info.Path))
            {
                ViewModel.NavigateToTask(info.Path);
            }
        }

        private void FailedTask_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FluentTaskScheduler.ViewModels.FailedTaskInfo info && !string.IsNullOrEmpty(info.Path))
            {
                ViewModel.NavigateToTask(info.Path);
            }
        }

        private void CategoryToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FluentTaskScheduler.ViewModels.FilterItem filterItem)
            {
                ViewModel.SelectedCategory = filterItem.Name;
            }
        }

        private void TagToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FluentTaskScheduler.ViewModels.FilterItem filterItem)
            {
                ViewModel.SelectedTag = filterItem.Name;
            }
        }

        private const double FilterScrollStep = 150;

        private void CategoryScrollLeft_Click(object sender, RoutedEventArgs e) =>
            CategoryScrollViewer.ChangeView(System.Math.Max(0, CategoryScrollViewer.HorizontalOffset - FilterScrollStep), null, null);

        private void CategoryScrollRight_Click(object sender, RoutedEventArgs e) =>
            CategoryScrollViewer.ChangeView(System.Math.Min(CategoryScrollViewer.ScrollableWidth, CategoryScrollViewer.HorizontalOffset + FilterScrollStep), null, null);

        private void CategoryScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            UpdateFilterScrollArrows(CategoryScrollViewer, CategoryScrollLeftBtn, CategoryScrollRightBtn);

        private void CategoryScrollViewer_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
            UpdateFilterScrollArrows(CategoryScrollViewer, CategoryScrollLeftBtn, CategoryScrollRightBtn);

        private void TagScrollLeft_Click(object sender, RoutedEventArgs e) =>
            TagScrollViewer.ChangeView(System.Math.Max(0, TagScrollViewer.HorizontalOffset - FilterScrollStep), null, null);

        private void TagScrollRight_Click(object sender, RoutedEventArgs e) =>
            TagScrollViewer.ChangeView(System.Math.Min(TagScrollViewer.ScrollableWidth, TagScrollViewer.HorizontalOffset + FilterScrollStep), null, null);

        private void TagScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            UpdateFilterScrollArrows(TagScrollViewer, TagScrollLeftBtn, TagScrollRightBtn);

        private void TagScrollViewer_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
            UpdateFilterScrollArrows(TagScrollViewer, TagScrollLeftBtn, TagScrollRightBtn);

        private static void UpdateFilterScrollArrows(ScrollViewer sv, Button left, Button right)
        {
            if (sv == null || left == null || right == null) return;
            bool overflowing = sv.ScrollableWidth > 0.5;
            left.Visibility = overflowing && sv.HorizontalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            right.Visibility = overflowing && sv.HorizontalOffset < sv.ScrollableWidth - 0.5 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
