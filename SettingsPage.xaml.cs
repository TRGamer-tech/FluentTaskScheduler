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
            OledModeToggle.IsOn = SettingsService.IsOledMode;
            MicaModeToggle.IsOn = SettingsService.IsMicaEnabled;
            MicaModeToggle.IsEnabled = !SettingsService.IsOledMode;

            // General
            ConfirmDeleteToggle.IsOn = SettingsService.ConfirmDelete;

            // Notifications
            NotificationsToggle.IsOn = SettingsService.ShowNotifications;
            
            // System Tray
            TrayIconToggle.IsOn = SettingsService.EnableTrayIcon;
            MinimizeToTrayCheck.IsChecked = SettingsService.MinimizeToTray;
            MinimizeToTrayCheck.IsEnabled = SettingsService.EnableTrayIcon;

            // Run on Startup
            RunOnStartupToggle.IsOn = SettingsService.RunOnStartup;

            // Logging
            LoggingToggle.IsOn = SettingsService.EnableLogging;

            // Subscribe events
            NotificationsToggle.Toggled += NotificationsToggle_Toggled;
            TrayIconToggle.Toggled += TrayIconToggle_Toggled;
            MinimizeToTrayCheck.Click += MinimizeToTrayCheck_Click;
            RunOnStartupToggle.Toggled += RunOnStartupToggle_Toggled;
            LoggingToggle.Toggled += LoggingToggle_Toggled;

            _isLoaded = true;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void OledModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsOledMode = OledModeToggle.IsOn;
            MicaModeToggle.IsEnabled = !OledModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
            LogService.Info($"OLED Mode: {(OledModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void MicaModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.IsMicaEnabled = MicaModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
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
            LogService.Info($"Task Notifications: {(NotificationsToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void TrayIconToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.EnableTrayIcon = TrayIconToggle.IsOn;
            MinimizeToTrayCheck.IsEnabled = TrayIconToggle.IsOn;
            TrayIconService.UpdateVisibility();
            LogService.Info($"System Tray: {(TrayIconToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void MinimizeToTrayCheck_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsService.MinimizeToTray = MinimizeToTrayCheck.IsChecked ?? false;
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
                    (Application.Current as App)?.ApplyTheme(ElementTheme.Dark);
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
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void UpdateOledToggleState()
        {
            var currentTheme = SettingsService.Theme;
            bool isDark = currentTheme == ElementTheme.Dark;
            if (currentTheme == ElementTheme.Default) isDark = false;
            OledModeToggle.IsEnabled = isDark;
        }
    }
}
