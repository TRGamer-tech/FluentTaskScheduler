using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.ViewModels;

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
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!ViewModel.IsLoading)
            {
                await ViewModel.LoadDashboardData();
            }
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
    }
}
