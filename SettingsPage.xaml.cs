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
        private StackPanel[]? _panels;

        private static readonly int[] _leadMinuteOptions = { 1, 5, 10, 15, 30 };

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
            UpdateOledToggleState();

            // Notifications
            NotificationsToggle.IsOn = SettingsService.ShowNotifications;
            UpcomingRemindersToggle.IsOn = SettingsService.EnableUpcomingReminders;
            UpcomingRemindersToggle.IsEnabled = SettingsService.ShowNotifications;

            int leadIdx = Array.IndexOf(_leadMinuteOptions, SettingsService.ReminderLeadMinutes);
            ReminderLeadTimeComboBox.SelectedIndex = leadIdx >= 0 ? leadIdx : 1;
            ReminderLeadTimeComboBox.IsEnabled = SettingsService.ShowNotifications && SettingsService.EnableUpcomingReminders;

            // System
            RunOnStartupToggle.IsOn = SettingsService.RunOnStartup;
            TrayIconToggle.IsOn = SettingsService.EnableTrayIcon;
            SmoothScrollingToggle.IsOn = SettingsService.SmoothScrolling;

            // Advanced
            ConfirmDeleteToggle.IsOn = SettingsService.ConfirmDelete;
            LoggingToggle.IsOn = SettingsService.EnableLogging;

            // Init sidebar panels — sync visibility with current selection
            _panels = new[] { PanelAppearance, PanelNotifications, PanelSystem, PanelAdvanced, PanelData, PanelAbout };
            SyncPanelVisibility();

            _isLoaded = true;
            PageScrollViewer.IsScrollInertiaEnabled = SettingsService.SmoothScrolling;

            // Set custom title bar drag region
            App.m_window?.SetTitleBar(AppTitleBarDragArea);
        }

        // ── Sidebar ────────────────────────────────────────────────────────────
        
        private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_panels == null || args.SelectedItem == null) return;
            SyncPanelVisibility();
            PageScrollViewer.ScrollToVerticalOffset(0);
        }

        private void SyncPanelVisibility()
        {
            if (_panels == null) return;
            
            var selectedItem = SettingsNav.SelectedItem as NavigationViewItem;
            if (selectedItem == null) return;

            string tag = selectedItem.Tag?.ToString() ?? "";
            
            PanelAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
            PanelNotifications.Visibility = tag == "Notifications" ? Visibility.Visible : Visibility.Collapsed;
            PanelSystem.Visibility = tag == "System" ? Visibility.Visible : Visibility.Collapsed;
            PanelAdvanced.Visibility = tag == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
            PanelData.Visibility = tag == "Data" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Navigation ─────────────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        // ── Appearance ─────────────────────────────────────────────────────────

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

        private void MicaModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsMicaEnabled = MicaModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
            LogService.Info($"Mica Effect: {(MicaModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void OledModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsOledMode = OledModeToggle.IsOn;
            MicaModeToggle.IsEnabled = !OledModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(SettingsService.Theme);
            LogService.Info($"OLED Mode: {(OledModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void UpdateOledToggleState()
        {
            bool isDark = SettingsService.Theme == ElementTheme.Dark;
            OledModeToggle.IsEnabled = isDark;
            MicaModeToggle.IsEnabled = !isDark || !SettingsService.IsOledMode;
        }

        // ── Notifications ──────────────────────────────────────────────────────

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ShowNotifications = NotificationsToggle.IsOn;
            UpcomingRemindersToggle.IsEnabled = NotificationsToggle.IsOn;
            ReminderLeadTimeComboBox.IsEnabled = NotificationsToggle.IsOn && UpcomingRemindersToggle.IsOn;
            LogService.Info($"Task Notifications: {(NotificationsToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void UpcomingRemindersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableUpcomingReminders = UpcomingRemindersToggle.IsOn;
            ReminderLeadTimeComboBox.IsEnabled = UpcomingRemindersToggle.IsOn;
            LogService.Info($"Upcoming Task Reminders: {(UpcomingRemindersToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void ReminderLeadTimeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            int idx = ReminderLeadTimeComboBox.SelectedIndex;
            if (idx >= 0 && idx < _leadMinuteOptions.Length)
            {
                SettingsService.ReminderLeadMinutes = _leadMinuteOptions[idx];
                LogService.Info($"Reminder lead time: {_leadMinuteOptions[idx]} min");
            }
        }

        // ── System ─────────────────────────────────────────────────────────────

        private void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.RunOnStartup = RunOnStartupToggle.IsOn;
            StartupService.UpdateFromSettings();
        }

        private void TrayIconToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableTrayIcon = TrayIconToggle.IsOn;
            SettingsService.MinimizeToTray = TrayIconToggle.IsOn;
            TrayIconService.UpdateVisibility();
            LogService.Info($"Minimize to Tray: {(TrayIconToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void SmoothScrollingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool enable = SmoothScrollingToggle.IsOn;
            SettingsService.SmoothScrolling = enable;
            LogService.Info($"Smooth Scrolling: {(enable ? "enabled" : "disabled")}");
            PageScrollViewer.IsScrollInertiaEnabled = enable;
            (Application.Current as App)?.ApplySmoothScrolling(enable);
            MainPage.Current?.ApplySmoothScrollingSelf(enable);
        }

        // ── Advanced ───────────────────────────────────────────────────────────

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.ConfirmDelete = ConfirmDeleteToggle.IsOn;
            LogService.Info($"Confirm Task Deletion: {(ConfirmDeleteToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableLogging = LoggingToggle.IsOn;
            if (LoggingToggle.IsOn)
                LogService.Info("Application Logging: enabled");
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogService.OpenLogFile();
        }

        // ── Data ───────────────────────────────────────────────────────────────

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

                    _isLoaded = false;
                    SettingsPage_Loaded(this, new RoutedEventArgs());

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

        // ── About ──────────────────────────────────────────────────────────────

        private async void VersionButton_Click(object sender, RoutedEventArgs e)
        {
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
            Services.SettingsService.HasCompletedOnboarding = false;
            var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = SettingsService.Theme };
            await dialog.ShowAsync();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

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
    }
}
