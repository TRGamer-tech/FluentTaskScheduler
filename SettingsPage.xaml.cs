using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using FluentTaskScheduler.Services;
using Windows.Storage.Pickers;

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

            // Appearance
            ThemeComboBox.SelectedIndex = SettingsService.Theme switch
            {
                ElementTheme.Light => 0,
                ElementTheme.Dark => 1,
                _ => 2
            };

            OledModeToggle.IsOn = SettingsService.IsOledMode;
            MicaModeToggle.IsOn = SettingsService.IsMicaEnabled;
            MicaModeToggle.IsEnabled = !SettingsService.IsOledMode;
            UpdateOledToggleState();

            // General
            ConfirmDeleteToggle.IsOn = SettingsService.ConfirmDelete;

            // Notifications
            NotificationsToggle.IsOn = SettingsService.ShowNotifications;
            UpcomingRemindersToggle.IsOn = SettingsService.EnableUpcomingReminders;
            UpcomingRemindersToggle.IsEnabled = SettingsService.ShowNotifications;
            
            // Minimize to Tray (single toggle now controls both)
            TrayIconToggle.IsOn = SettingsService.EnableTrayIcon;

            // Run on Startup
            RunOnStartupToggle.IsOn = SettingsService.RunOnStartup;

            // Smooth Scrolling
            SmoothScrollingToggle.IsOn = SettingsService.SmoothScrolling;

            // Logging
            LoggingToggle.IsOn = SettingsService.EnableLogging;

            // Subscribe events
            NotificationsToggle.Toggled += NotificationsToggle_Toggled;
            UpcomingRemindersToggle.Toggled += UpcomingRemindersToggle_Toggled;
            TrayIconToggle.Toggled += TrayIconToggle_Toggled;
            RunOnStartupToggle.Toggled += RunOnStartupToggle_Toggled;
            LoggingToggle.Toggled += LoggingToggle_Toggled;
            SmoothScrollingToggle.Toggled += SmoothScrollingToggle_Toggled;

            _isLoaded = true;

            // Apply current smooth scrolling setting to this page's ScrollViewer
            PageScrollViewer.IsScrollInertiaEnabled = SettingsService.SmoothScrolling;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.Theme = ThemeComboBox.SelectedIndex switch
            {
                0 => ElementTheme.Light,
                1 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
            UpdateOledToggleState();
            LogService.Info($"App Theme: {SettingsService.Theme}");
        }

        private void OledModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsOledMode = OledModeToggle.IsOn;
            MicaModeToggle.IsEnabled = !OledModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
            LogService.Info($"OLED Mode: {(OledModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void MicaModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsMicaEnabled = MicaModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
            LogService.Info($"Mica Effect: {(MicaModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ConfirmDelete = ConfirmDeleteToggle.IsOn;
            LogService.Info($"Confirm Task Deletion: {(ConfirmDeleteToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ShowNotifications = NotificationsToggle.IsOn;
            UpcomingRemindersToggle.IsEnabled = NotificationsToggle.IsOn;
            LogService.Info($"Task Notifications: {(NotificationsToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void UpcomingRemindersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableUpcomingReminders = UpcomingRemindersToggle.IsOn;
            LogService.Info($"Upcoming Task Reminders: {(UpcomingRemindersToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void TrayIconToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableTrayIcon = TrayIconToggle.IsOn;
            SettingsService.MinimizeToTray = TrayIconToggle.IsOn;
            TrayIconService.UpdateVisibility();
            LogService.Info($"Minimize to Tray: {(TrayIconToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.RunOnStartup = RunOnStartupToggle.IsOn;
            StartupService.UpdateFromSettings();
        }

        private void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableLogging = LoggingToggle.IsOn;
            if (LoggingToggle.IsOn)
                LogService.Info("Application Logging: enabled");
        }

        private void SmoothScrollingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool enable = SmoothScrollingToggle.IsOn;
            SettingsService.SmoothScrolling = enable;
            LogService.Info($"Smooth Scrolling: {(enable ? "enabled" : "disabled")}");
            // Apply to this page's own ScrollViewer immediately
            PageScrollViewer.IsScrollInertiaEnabled = enable;
            // Apply to the rest of the live visual tree (MainPage dialogs etc.)
            (Application.Current as App)?.ApplySmoothScrolling(enable);
            MainPage.Current?.ApplySmoothScrollingSelf(enable);
        }

        private async void VersionButton_Click(object sender, RoutedEventArgs e)
        {
            // Fetch on demand — reuse the same service as the startup check
            var release = await Services.GitHubReleaseService.GetLatestReleaseAsync();
            if (release == null)
            {
                await ShowDialog("What's New", "Could not fetch release notes. Check your internet connection and try again.");
                return;
            }
            var dialog = new Dialogs.WhatsNewDialog(release) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }

        private async void ReplayOnboardingButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset flag so the walkthrough can be shown again on next launch too
            Services.SettingsService.HasCompletedOnboarding = false;

            var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = SettingsService.Theme };
            await dialog.ShowAsync();
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogService.OpenLogFile();
        }

        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.Desktop;
                picker.FileTypeChoices.Add("JSON", new[] { ".json" });
                picker.SuggestedFileName = "FluentTaskScheduler_Settings";

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    SettingsService.ExportSettings(file.Path);
                    await ShowDialog("Export Successful", $"Settings exported to:\n{file.Path}");
                }
            }
            catch (Exception ex)
            {
                await ShowDialog("Export Failed", ex.Message);
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.Desktop;
                picker.FileTypeFilter.Add(".json");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    SettingsService.ImportSettings(file.Path);

                    // Reload UI
                    _isLoaded = false;
                    SettingsPage_Loaded(this, new RoutedEventArgs());

                    // Re-apply theme and tray
                    (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
                    TrayIconService.UpdateVisibility();
                    StartupService.UpdateFromSettings();

                    await ShowDialog("Import Successful", "Settings have been restored. Some changes may require an app restart.");
                }
            }
            catch (Exception ex)
            {
                await ShowDialog("Import Failed", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ShowDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
                RequestedTheme = SettingsService.Theme
            };
            await dialog.ShowAsync();
        }

        private void UpdateOledToggleState()
        {
            var currentTheme = SettingsService.Theme;
            bool isDark = currentTheme == ElementTheme.Dark;
            OledModeToggle.IsEnabled = isDark;
            // When not in explicit dark mode, OLED cannot apply — Mica must always be freely toggleable
            MicaModeToggle.IsEnabled = !isDark || !SettingsService.IsOledMode;
        }
    }
}
