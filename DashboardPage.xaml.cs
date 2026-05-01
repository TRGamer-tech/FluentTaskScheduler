using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler
{
    public sealed partial class DashboardPage : Page
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage()
        {
            this.InitializeComponent();
            ViewModel = new DashboardViewModel();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            ApplyLocalizedUi();
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
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!ViewModel.IsLoading)
            {
                await ViewModel.LoadDashboardData();
            }
            ViewModel.StartAutoRefresh();
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.StopAutoRefresh();
        }

        private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            PageScrollViewer.IsScrollInertiaEnabled = FluentTaskScheduler.Services.SettingsService.SmoothScrolling;
        }
        private async void DashboardReload_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.LoadDashboardData();
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
    }
}
