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

            // Confirm Delete
            ConfirmDeleteToggle.IsOn = SettingsService.ConfirmDelete;

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
            // Re-apply dark theme with OLED setting
            (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
        }

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ConfirmDelete = ConfirmDeleteToggle.IsOn;
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
