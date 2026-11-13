using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Collections.Generic;

namespace FluentTaskScheduler
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoaded = false;
        private StackPanel[]? _panels;
        private readonly Dictionary<string, string> _sectionTitles = new();
        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        private static readonly int[] _leadMinuteOptions = { 1, 5, 10, 15, 30 };

        public SettingsPage()
        {
            this.InitializeComponent();
            Loaded += SettingsPage_Loaded;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;

            // With "System Default" selected, the OS switching to dark must un-grey OLED mode.
            ActualThemeChanged += SettingsPage_ActualThemeChanged;
        }

        /// <summary>
        /// Reads the version off the running assembly so it can never drift from the csproj.
        /// </summary>
        internal static string GetAppVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not read the assembly version: {ex.Message}");
                return "";
            }
        }

        private void SettingsPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (!_isLoaded) return;
            UpdateOledToggleState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ActualThemeChanged -= SettingsPage_ActualThemeChanged;
            base.OnNavigatedFrom(e);
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyLocalizedUi();
                SyncPanelVisibility();
            });
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;

            // Appearance
            ThemeComboBox.SelectedIndex = Settings.Theme switch
            {
                ElementTheme.Light => 0,
                ElementTheme.Dark => 1,
                _ => 2
            };
            OledModeToggle.IsOn = Settings.IsOledMode;
            MicaModeToggle.IsOn = Settings.IsMicaEnabled;
            LanguageComboBox.SelectedIndex = Settings.Language switch
            {
                "de-DE" => 1,
                "zh-CN" => 2,
                "ja-JP" => 3,
                _ => 0
            };
            UpdateOledToggleState();

            // Notifications
            NotificationsToggle.IsOn = Settings.ShowNotifications;
            UpcomingRemindersToggle.IsOn = Settings.EnableUpcomingReminders;
            UpcomingRemindersToggle.IsEnabled = Settings.ShowNotifications;

            int leadIdx = Array.IndexOf(_leadMinuteOptions, Settings.ReminderLeadMinutes);
            ReminderLeadTimeComboBox.SelectedIndex = leadIdx >= 0 ? leadIdx : 1;
            ReminderLeadTimeComboBox.IsEnabled = Settings.ShowNotifications && Settings.EnableUpcomingReminders;

            // System
            RunOnStartupToggle.IsOn = Settings.RunOnStartup;
            TrayIconToggle.IsOn = Settings.EnableTrayIcon;
            MinimizeToTrayToggle.IsOn = Settings.MinimizeToTray;
            MinimizeToTrayToggle.IsEnabled = Settings.EnableTrayIcon;
            SmoothScrollingToggle.IsOn = Settings.SmoothScrolling;
            ShowHiddenTasksToggle.IsOn = Settings.ShowHiddenTasks;
            TaskPipelinesToggle.IsOn = Settings.EnableTaskPipelines;

            // Advanced
            ConfirmDeleteToggle.IsOn = Settings.ConfirmDelete;
            LoggingToggle.IsOn = Settings.EnableLogging;
            SeparateLogsToggle.IsOn = Settings.SeparateLogFiles;
            SpecificLogsCard.Visibility = Settings.EnableLogging ? Visibility.Visible : Visibility.Collapsed;
            ExecHistoryLogToggle.IsOn = Settings.EnableExecutionHistoryLog;
            ExecHistoryFolderCard.Visibility = Settings.EnableExecutionHistoryLog ? Visibility.Visible : Visibility.Collapsed;

            // Init sidebar panels — sync visibility with current selection
            _panels = new[] { PanelAppearance, PanelNotifications, PanelSystem, PanelAdvanced, PanelCategories, PanelAbout };
            SyncPanelVisibility();

            // Categories & Tags initial load
            RefreshCategoriesList();
            RefreshTagsList();

            _isLoaded = true;
            PageScrollViewer.IsScrollInertiaEnabled = Settings.SmoothScrolling;

            ApplyLocalizedUi();

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

            SettingsPanel? panel = Enum.TryParse<SettingsPanel>(selectedItem.Tag?.ToString(), out var parsed)
                ? parsed
                : null;

            // Header assignment removed as AlwaysShowHeader is False

            PanelAppearance.Visibility = panel == SettingsPanel.Appearance ? Visibility.Visible : Visibility.Collapsed;
            PanelNotifications.Visibility = panel == SettingsPanel.Notifications ? Visibility.Visible : Visibility.Collapsed;
            PanelSystem.Visibility = panel == SettingsPanel.System ? Visibility.Visible : Visibility.Collapsed;
            PanelAdvanced.Visibility = panel == SettingsPanel.Advanced ? Visibility.Visible : Visibility.Collapsed;
            PanelCategories.Visibility = panel == SettingsPanel.Categories ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = panel == SettingsPanel.About ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Navigation ─────────────────────────────────────────────────────────

        // ── Navigation ─────────────────────────────────────────────────────────

        // ── Appearance ─────────────────────────────────────────────────────────

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.Theme = ThemeComboBox.SelectedIndex switch
            {
                0 => ElementTheme.Light,
                1 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            (Application.Current as App)?.ApplyTheme(Settings.Theme);
            UpdateOledToggleState();
            Serilog.Log.Information("{Message}", $"App Theme: {Settings.Theme}");
        }

        private void MicaModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.IsMicaEnabled = MicaModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(Settings.Theme);
            Serilog.Log.Information("{Message}", $"Mica Effect: {(MicaModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void OledModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.IsOledMode = OledModeToggle.IsOn;
            MicaModeToggle.IsEnabled = !OledModeToggle.IsOn;
            (Application.Current as App)?.ApplyTheme(Settings.Theme);
            Serilog.Log.Information("{Message}", $"OLED Mode: {(OledModeToggle.IsOn ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// OLED mode only makes sense on a dark surface. "System Default" resolves to whatever the
        /// OS is currently using, so the toggle must follow the *effective* theme rather than the
        /// stored preference - otherwise it stayed greyed out on a dark-themed system.
        /// </summary>
        private void UpdateOledToggleState()
        {
            bool isDark = IsEffectivelyDark();
            OledModeToggle.IsEnabled = isDark;
            MicaModeToggle.IsEnabled = !isDark || !Settings.IsOledMode;
        }

        private bool IsEffectivelyDark()
        {
            return Settings.Theme switch
            {
                ElementTheme.Dark => true,
                ElementTheme.Light => false,
                // Default: ask the framework what it actually resolved to for this page.
                _ => ActualTheme == ElementTheme.Dark
            };
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (LanguageComboBox.SelectedItem is not ComboBoxItem selected) return;

            string language = selected.Tag?.ToString() ?? "en-US";
            bool changed = LocalizationService.ChangeLanguage(language);
            if (changed)
            {
                Serilog.Log.Information("{Message}", $"Language switched to {language}");
            }
        }

        private void ApplyLocalizedUi()
        {
            string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

            NavAppearanceItem.Content = L("Settings.Nav.Appearance", "Appearance");
            NavNotificationsItem.Content = L("Settings.Nav.Notifications", "Notifications");
            NavSystemItem.Content = L("Settings.Nav.System", "System");
            NavAdvancedItem.Content = L("Settings.Nav.Advanced", "Advanced");
            NavCategoriesItem.Content = L("Settings.Nav.Categories", "Categories & Tags");
            NavAboutItem.Content = L("Settings.Nav.About", "About");

            AppearanceHeaderText.Text = L("Settings.Section.Appearance", "Appearance");
            NotificationsHeaderText.Text = L("Settings.Section.Notifications", "Notifications");
            SystemHeaderText.Text = L("Settings.Section.System", "System");
            AdvancedHeaderText.Text = L("Settings.Section.Advanced", "Advanced");
            DataHeaderText.Text = L("Settings.Section.Data", "Data");
            CategoriesHeaderText.Text = L("Settings.Section.Categories", "Categories & Tags");
            AboutHeaderText.Text = L("Settings.Section.About", "About");
            AboutVersionText.Text = string.Format(L("Settings.About.VersionFormat", "Version {0}"), GetAppVersion());
            LanguageTitleText.Text = L("Settings.Appearance.Language.Title", "Language");
            LanguageDescriptionText.Text = L("Settings.Appearance.Language.Description", "Choose the display language for the app.");
            AppThemeTitleText.Text = L("Settings.Appearance.Theme.Title", "App Theme");
            AppThemeDescriptionText.Text = L("Settings.Appearance.Theme.Description", "Choose Light, Dark, or follow the system setting.");
            MicaTitleText.Text = L("Settings.Appearance.Mica.Title", "Mica Effect");
            MicaDescriptionText.Text = L("Settings.Appearance.Mica.Description", "Apply the Mica translucent background material.");
            OledTitleText.Text = L("Settings.Appearance.Oled.Title", "OLED Mode");
            OledDescriptionText.Text = L("Settings.Appearance.Oled.Description", "Pure black background to save power on OLED displays. Requires Dark theme.");

            // Notifications
            NotifTaskTitle.Text = L("Settings.Notif.Task.Title", "Task Notifications");
            NotifTaskDesc.Text = L("Settings.Notif.Task.Desc", "Show a toast when a task starts or fails.");
            NotifRemindersTitle.Text = L("Settings.Notif.Reminders.Title", "Upcoming Task Reminders");
            NotifRemindersDesc.Text = L("Settings.Notif.Reminders.Desc", "Show a reminder toast before a scheduled task is about to run.");
            NotifTimingTitle.Text = L("Settings.Notif.Timing.Title", "Reminder Timing");
            NotifTimingDesc.Text = L("Settings.Notif.Timing.Desc", "How far in advance to send the reminder.");

            // System
            SysStartupTitle.Text = L("Settings.Sys.Startup.Title", "Run on Startup");
            SysStartupDesc.Text = L("Settings.Sys.Startup.Desc", "Automatically launch the app when you sign in to Windows.");
            SysTrayTitle.Text = L("Settings.Sys.Tray.Title", "Minimize to Tray");
            SysTrayDesc.Text = L("Settings.Sys.Tray.Desc", "Hide the window to the system tray instead of closing.");
            SysSmoothTitle.Text = L("Settings.Sys.Smooth.Title", "Smooth Scrolling");
            SysSmoothDesc.Text = L("Settings.Sys.Smooth.Desc", "Enable inertia-based scrolling throughout the app.");
            SysPipelineTitle.Text = L("Settings.Sys.Pipelines.Title", "Task Pipelines");
            SysPipelineDesc.Text = L("Settings.Sys.Pipelines.Desc",
                "Watch the Task Scheduler event log and start the downstream tasks configured under a task's Completion Actions.");
            SysHiddenTitle.Text = L("Settings.Sys.Hidden.Title", "Show Hidden Tasks");
            SysHiddenDesc.Text = L("Settings.Sys.Hidden.Desc", "Display tasks that are marked as hidden in the Windows Task Scheduler.");

            // Advanced
            AdvConfirmTitle.Text = L("Settings.Adv.Confirm.Title", "Confirm Before Deleting");
            AdvConfirmDesc.Text = L("Settings.Adv.Confirm.Desc", "Show a confirmation dialog before removing a task.");
            AdvLoggingTitle.Text = L("Settings.Adv.Logging.Title", "Application Logging");
            AdvLoggingDesc.Text = L("Settings.Adv.Logging.Desc", "Write internal events and errors to a log file for troubleshooting.");
            AdvSepLogsTitle.Text = L("Settings.Adv.SepLogs.Title", "Separate Log Files");
            AdvSepLogsDesc.Text = L("Settings.Adv.SepLogs.Desc", "Store error and crash logs in dedicated files instead of the main log.");
            AdvSpecLogsTitle.Text = L("Settings.Adv.SpecLogs.Title", "Specific Logs");
            AdvSpecLogsDesc.Text = L("Settings.Adv.SpecLogs.Desc", "Direct access to error and crash records.");
            OpenLogButton.Content = L("Settings.Adv.OpenLog", "Open Log");
            OpenErrorLogButton.Content = L("Settings.Adv.ErrorLog", "Error Log");
            OpenCrashLogButton.Content = L("Settings.Adv.CrashLog", "Crash Log");

            // Data
            DataExportTitle.Text = L("Settings.Data.Export.Title", "Export Settings");
            DataExportDesc.Text = L("Settings.Data.Export.Desc", "Save your current settings to a JSON file.");
            ExportSettingsButton.Content = L("Settings.Data.ExportBtn", "Export");
            DataImportTitle.Text = L("Settings.Data.Import.Title", "Import Settings");
            DataImportDesc.Text = L("Settings.Data.Import.Desc", "Restore settings from a previously exported file.");
            ImportSettingsButton.Content = L("Settings.Data.ImportBtn", "Import");

            // Categories & Tags
            CatManageTitle.Text = L("Settings.Cat.Manage.Title", "Manage Categories");
            CatManageDesc.Text = L("Settings.Cat.Manage.Desc", "Predefined categories for your tasks.");
            NewCategoryBox.PlaceholderText = L("Settings.Cat.Placeholder", "New category name...");
            AddCategoryBtn.Content = L("Settings.Cat.AddBtn", "Add");
            TagManageTitle.Text = L("Settings.Tag.Manage.Title", "Manage Tags");
            TagManageDesc.Text = L("Settings.Tag.Manage.Desc", "Predefined tags for your tasks.");
            NewTagBox.PlaceholderText = L("Settings.Tag.Placeholder", "New tag name...");
            AddTagBtn.Content = L("Settings.Tag.AddBtn", "Add");

            // About
            AboutAppTitle.Text = L("Settings.About.App.Title", "Fluent Task Scheduler");
            AboutAppDesc.Text = L("Settings.About.App.Desc", "Built with WinUI 3 · Windows App SDK");
            AboutUpdateTitle.Text = L("Settings.About.Update.Title", "Check for Updates");
            AboutUpdateDesc.Text = L("Settings.About.Update.Desc", "Check if a newer version is available.");
            AboutCheckBtnText.Text = L("Settings.About.CheckBtn", "Check");
            AboutOnboardTitle.Text = L("Settings.About.Onboard.Title", "Onboarding");
            AboutOnboardDesc.Text = L("Settings.About.Onboard.Desc", "Replay the welcome walkthrough from first launch.");
            AboutViewAgainText.Text = L("Settings.About.ViewAgain", "View Again");

            _sectionTitles["Appearance"] = L("Settings.Section.Appearance", "Appearance");
            _sectionTitles["Notifications"] = L("Settings.Section.Notifications", "Notifications");
            _sectionTitles["System"] = L("Settings.Section.System", "System");
            _sectionTitles["Advanced"] = L("Settings.Section.Advanced", "Advanced");
            _sectionTitles["Data"] = L("Settings.Section.Data", "Data");
            _sectionTitles["Categories"] = L("Settings.Section.Categories", "Categories & Tags");
            _sectionTitles["About"] = L("Settings.Section.About", "About");

            // Dropdown items
            ThemeLightItem.Content = L("Settings.Theme.Light", "Light");
            ThemeDarkItem.Content = L("Settings.Theme.Dark", "Dark");
            ThemeSystemItem.Content = L("Settings.Theme.System", "System Default");

            Lead1mItem.Content = L("Settings.Lead.1m", "1 minute before");
            Lead5mItem.Content = L("Settings.Lead.5m", "5 minutes before");
            Lead10mItem.Content = L("Settings.Lead.10m", "10 minutes before");
            Lead15mItem.Content = L("Settings.Lead.15m", "15 minutes before");
            Lead30mItem.Content = L("Settings.Lead.30m", "30 minutes before");
        }

        // ── Notifications ──────────────────────────────────────────────────────

        private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.ShowNotifications = NotificationsToggle.IsOn;
            UpcomingRemindersToggle.IsEnabled = NotificationsToggle.IsOn;
            ReminderLeadTimeComboBox.IsEnabled = NotificationsToggle.IsOn && UpcomingRemindersToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Task Notifications: {(NotificationsToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void UpcomingRemindersToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.EnableUpcomingReminders = UpcomingRemindersToggle.IsOn;
            ReminderLeadTimeComboBox.IsEnabled = UpcomingRemindersToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Upcoming Task Reminders: {(UpcomingRemindersToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void ReminderLeadTimeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            int idx = ReminderLeadTimeComboBox.SelectedIndex;
            if (idx >= 0 && idx < _leadMinuteOptions.Length)
            {
                Settings.ReminderLeadMinutes = _leadMinuteOptions[idx];
                Serilog.Log.Information("{Message}", $"Reminder lead time: {_leadMinuteOptions[idx]} min");
            }
        }

        // ── System ─────────────────────────────────────────────────────────────

        private void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.RunOnStartup = RunOnStartupToggle.IsOn;
            StartupService.UpdateFromSettings();
        }

        private void TrayIconToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.EnableTrayIcon = TrayIconToggle.IsOn;
            TrayIconService.UpdateVisibility();
            MinimizeToTrayToggle.IsEnabled = TrayIconToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Tray icon: {(TrayIconToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.MinimizeToTray = MinimizeToTrayToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Minimize to Tray: {(MinimizeToTrayToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void SmoothScrollingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool enable = SmoothScrollingToggle.IsOn;
            Settings.SmoothScrolling = enable;
            Serilog.Log.Information("{Message}", $"Smooth Scrolling: {(enable ? "enabled" : "disabled")}");
            PageScrollViewer.IsScrollInertiaEnabled = enable;
            (Application.Current as App)?.ApplySmoothScrolling(enable);
            MainPage.Current?.ApplySmoothScrollingSelf(enable);
        }

        private void ShowHiddenTasksToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.ShowHiddenTasks = ShowHiddenTasksToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Show Hidden Tasks: {(ShowHiddenTasksToggle.IsOn ? "enabled" : "disabled")}");
            // Trigger refresh in main view if it exists
            MainPage.Current?.ViewModel.ApplyFilters();
        }

        private void TaskPipelinesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.EnableTaskPipelines = TaskPipelinesToggle.IsOn;
            // Start/stop the event-log watcher immediately so no restart is needed.
            TaskPipelineService.ApplyEnabledSetting();
            Serilog.Log.Information("{Message}", $"Task Pipelines: {(TaskPipelinesToggle.IsOn ? "enabled" : "disabled")}");
        }

        // ── Advanced ───────────────────────────────────────────────────────────

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.ConfirmDelete = ConfirmDeleteToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Confirm Task Deletion: {(ConfirmDeleteToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.EnableLogging = LoggingToggle.IsOn;
            SpecificLogsCard.Visibility = LoggingToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (LoggingToggle.IsOn)
                Serilog.Log.Information("Application Logging: enabled");
        }

        private void SeparateLogsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.SeparateLogFiles = SeparateLogsToggle.IsOn;
            Serilog.Log.Information("{Message}", $"Separate Log Files: {(SeparateLogsToggle.IsOn ? "enabled" : "disabled")}");
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            LoggingConfig.OpenLogFile();
        }

        private void ExecHistoryLogToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Settings.EnableExecutionHistoryLog = ExecHistoryLogToggle.IsOn;
            ExecHistoryFolderCard.Visibility = ExecHistoryLogToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (ExecHistoryLogToggle.IsOn)
            {
                ExecutionHistoryLogService.Start();
                Serilog.Log.Information("Execution History Log: enabled");
            }
            else
            {
                ExecutionHistoryLogService.Stop();
                Serilog.Log.Information("Execution History Log: disabled");
            }
        }

        private void OpenExecHistoryFolderButton_Click(object sender, RoutedEventArgs e)
        {
            ExecutionHistoryLogService.OpenLogFolder();
        }

        private void OpenErrorLogButton_Click(object sender, RoutedEventArgs e)
        {
            LoggingConfig.OpenErrorLog();
        }

        private void OpenCrashLogButton_Click(object sender, RoutedEventArgs e)
        {
            LoggingConfig.OpenCrashLog();
        }

        // ── Data ───────────────────────────────────────────────────────────────

        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Uses the shared Win32/WinRT picker helper — the plain WinRT FileSavePicker used
                // here previously throws when the app is running elevated (4.4).
                string? filePath = await Helpers.FilePickerHelper.PickSaveFileAsync(
                    App.m_window!, "Export Settings", "JSON", "json", "FluentTaskScheduler_Settings");
                if (!string.IsNullOrEmpty(filePath))
                {
                    Settings.ExportSettings(filePath);
                    await ShowDialog(
                        LocalizationService.GetString("Settings.Export.Success.Title", "Export Successful"),
                        string.Format(LocalizationService.GetString("Settings.Export.Success.ContentFormat", "Settings exported to:\n{0}"), filePath));
                }
            }
            catch (Exception ex)
            {
                await ShowDialog(LocalizationService.GetString("Settings.Export.Failed.Title", "Export Failed"), ex.Message);
            }
        }

        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Uses the shared Win32/WinRT picker helper — the plain WinRT FileOpenPicker used
                // here previously throws when the app is running elevated (4.4).
                string? filePath = await Helpers.FilePickerHelper.PickOpenFileAsync(
                    App.m_window!, "Import Settings", "JSON", "json");
                if (!string.IsNullOrEmpty(filePath))
                {
                    Settings.ImportSettings(filePath);

                    _isLoaded = false;
                    SettingsPage_Loaded(this, new RoutedEventArgs());

                    (Application.Current as App)?.ApplyTheme(Settings.Theme);
                    TrayIconService.UpdateVisibility();
                    StartupService.UpdateFromSettings();
                    LocalizationService.ChangeLanguage(Settings.Language);

                    await ShowDialog(
                        LocalizationService.GetString("Settings.Import.Success.Title", "Import Successful"),
                        LocalizationService.GetString("Settings.Import.Success.Content", "Settings have been restored. Some changes may require an app restart."));
                }
            }
            catch (Exception ex)
            {
                await ShowDialog(LocalizationService.GetString("Settings.Import.Failed.Title", "Import Failed"), ex.Message);
            }
        }

        // ── About ──────────────────────────────────────────────────────────────

        private async void VersionButton_Click(object sender, RoutedEventArgs e)
        {
            var release = await Services.GitHubReleaseService.GetLatestReleaseAsync();
            if (release == null)
            {
                await ShowDialog(
                    LocalizationService.GetString("Settings.WhatsNew.Title", "What's New"),
                    LocalizationService.GetString("Settings.WhatsNew.FetchFailed", "Could not fetch release notes. Check your internet connection and try again."));
                return;
            }
            var dialog = new Dialogs.WhatsNewDialog(release) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdatesButton.IsEnabled = false;
            UpdateCheckProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            UpdateCheckProgressRing.IsActive = true;

            var result = await Services.VeloPackUpdateService.CheckAndDownloadAsync();

            UpdateCheckProgressRing.IsActive = false;
            UpdateCheckProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            CheckForUpdatesButton.IsEnabled = true;

            if (result.Status == Services.VeloPackUpdateService.UpdateResultStatus.Error)
            {
                await ShowDialog(
                    LocalizationService.GetString("Settings.UpdateError.Title", "Update Error"),
                    string.Format(LocalizationService.GetString("Settings.UpdateError.ContentFormat", "An error occurred while checking for updates:\n{0}"), result.ErrorMessage));
                return;
            }

            if (result.Status == Services.VeloPackUpdateService.UpdateResultStatus.NoUpdate || result.Info == null)
            {
                await ShowDialog(
                    LocalizationService.GetString("Settings.UpToDate.Title", "Up to Date"),
                    LocalizationService.GetString("Settings.UpToDate.Content", "You're already running the latest version."));
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizationService.GetString("Dialog.UpdateAvailable.Title", "Update Available"),
                Content = string.Format(
                    LocalizationService.GetString("Dialog.UpdateAvailable.ContentFormat", "Version {0} has been downloaded and is ready to install.\nRestart now to apply the update?"),
                    result.NewVersion),
                PrimaryButtonText = LocalizationService.GetString("Dialog.UpdateAvailable.RestartNow", "Restart Now"),
                CloseButtonText = LocalizationService.GetString("Dialog.Common.Later", "Later"),
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
                bool applied = Services.VeloPackUpdateService.ApplyAndRestart(result.Info);
                if (!applied)
                {
                    await ShowDialog(
                        LocalizationService.GetString("Settings.UpdateError.Title", "Update Error"),
                        LocalizationService.GetString("Settings.UpdateApplyFailed.Content", "Failed to apply the update. Check the log for details, or try again later."));
                }
            }
        }

        private async void ReplayOnboardingButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.HasCompletedOnboarding = false;
            var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = Settings.Theme };
            await dialog.ShowAsync();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private System.Threading.Tasks.Task ShowDialog(string title, string message) =>
            Helpers.DialogHelper.ShowMessageAsync(this.XamlRoot, title, message);

        // ── Categories & Tags ──────────────────────────────────────────────────

        private void RefreshCategoriesList()
        {
            CategoriesItemsControl.ItemsSource = null;
            CategoriesItemsControl.ItemsSource = Settings.SavedCategories;
        }

        private void RefreshTagsList()
        {
            TagsItemsControl.ItemsSource = null;
            TagsItemsControl.ItemsSource = Settings.SavedTags;
        }

        private void AddCategory_Click(object sender, RoutedEventArgs e) => AddCategory();
        private void NewCategoryBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) AddCategory();
        }

        private void AddCategory()
        {
            string cat = NewCategoryBox.Text.Trim();
            if (!string.IsNullOrEmpty(cat) && !Settings.SavedCategories.Contains(cat))
            {
                Settings.AddSavedCategory(cat);
                NewCategoryBox.Text = "";
                RefreshCategoriesList();
            }
        }

        private void RemoveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cat)
            {
                Settings.RemoveSavedCategory(cat);
                RefreshCategoriesList();
            }
        }

        private void AddTag_Click(object sender, RoutedEventArgs e) => AddTag();
        private void NewTagBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) AddTag();
        }

        private void AddTag()
        {
            string tag = NewTagBox.Text.Trim();
            if (!string.IsNullOrEmpty(tag) && !Settings.SavedTags.Contains(tag))
            {
                Settings.AddSavedTag(tag);
                NewTagBox.Text = "";
                RefreshTagsList();
            }
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                Settings.RemoveSavedTag(tag);
                RefreshTagsList();
            }
        }
    }
}
