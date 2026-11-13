using System;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentTaskScheduler
{
    public sealed partial class QuickActionsPage : Page
    {
        public QuickActionsViewModel ViewModel { get; } = new QuickActionsViewModel();

        private static string L(string key, string fallback) => Services.LocalizationService.GetString(key, fallback);

        public QuickActionsPage()
        {
            this.InitializeComponent();

            Services.LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            this.Unloaded += (s, e) => Services.LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ApplyLocalizedUi();
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(ApplyLocalizedUi);
        }

        /// <summary>
        /// Fills the header from LocalizationService. This used to rely on x:Uid, which resolves
        /// against the Windows display language rather than the app's language setting.
        /// </summary>
        private void ApplyLocalizedUi()
        {
            QuickActionsTitle.Text = L("QuickActions.Title.Text", "Quick Actions");
            QuickActionsDesc.Text = L("QuickActions.Description.Text",
                "Run common system maintenance tasks with a single click.");
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuickActionItemViewModel action)
            {
                await ViewModel.ExecuteAction(action);
            }
        }
    }
}
