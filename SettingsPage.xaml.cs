using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoaded = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;

            // Load saved settings
            
            // OLED Mode - always enabled in dark mode
            OledModeToggle.IsOn = SettingsService.IsOledMode;

            // Mica Mode
            MicaModeToggle.IsOn = SettingsService.IsMicaEnabled;
            // Only enable Mica toggle if not in OLED mode (conceptually)
            MicaModeToggle.IsEnabled = !SettingsService.IsOledMode;

            // Confirm Delete
            ConfirmDeleteToggle.IsOn = SettingsService.ConfirmDelete;

            // Notifications
            NotificationsToggle.IsOn = SettingsService.ShowNotifications;
            
            // System Tray
            TrayIconToggle.IsOn = SettingsService.EnableTrayIcon;
            MinimizeToTrayCheck.IsChecked = SettingsService.MinimizeToTray;
            MinimizeToTrayCheck.IsEnabled = SettingsService.EnableTrayIcon;

            // Events for checkboxes (since they don't have Toggled in XAML yet, or we need to add handlers)
            // Actually, in XAML I didn't add Toggled/Click handlers for these new controls yet.
            // I should have. But I can assign them here or in XAML.
            // Since I am already editing code behind, I can subscribe here.
            NotificationsToggle.Toggled += NotificationsToggle_Toggled;
            TrayIconToggle.Toggled += TrayIconToggle_Toggled;
            MinimizeToTrayCheck.Click += MinimizeToTrayCheck_Click;

            _isLoaded = true;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }



        private void OledModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            SettingsService.IsOledMode = OledModeToggle.IsOn;
            
            // Disable Mica toggle if OLED is on (mutually exclusive)
            MicaModeToggle.IsEnabled = !OledModeToggle.IsOn;
            
            // Re-apply dark theme
            (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
        }

        private void MicaModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsMicaEnabled = MicaModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
        }

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ConfirmDelete = ConfirmDeleteToggle.IsOn;
        }

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ShowNotifications = NotificationsToggle.IsOn;
        }

        private void TrayIconToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableTrayIcon = TrayIconToggle.IsOn;
            MinimizeToTrayCheck.IsEnabled = TrayIconToggle.IsOn;
            TrayIconService.UpdateVisibility();
        }

        private void MinimizeToTrayCheck_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.MinimizeToTray = MinimizeToTrayCheck.IsChecked ?? false;
        }

        private void UpdateOledToggleState()
        {
            // Enable OLED toggle only if Dark mode is effectively active
            var currentTheme = SettingsService.Theme;
            bool isDark = currentTheme == ElementTheme.Dark;
            
            if (currentTheme == ElementTheme.Default)
            {
                // Simple heuristic: default might be dark, but we only explicitly support OLED override when forced Dark to avoid complications
                // Or we can check actual system theme, but that's harder in WinUI 3 without more wiring. 
                // For now, enable OLED only if explicit Dark.
                isDark = false; 
            }

            OledModeToggle.IsEnabled = isDark;
            if (!isDark && OledModeToggle.IsOn) 
            {
                 // Don't turn it off automatically in settings, just disable interaction
            }
        }
    }
}
