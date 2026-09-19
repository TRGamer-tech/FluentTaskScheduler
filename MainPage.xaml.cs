using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using FluentTaskScheduler.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new();

        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        // Forwarding property for x:Bind compatibility
        public ObservableCollection<ScheduledTaskModel> FilteredTasks => ViewModel.FilteredTasks;

        private DispatcherQueueTimer _searchDebounceTimer;
        private List<TaskHistoryEntry> _fullHistory = new List<TaskHistoryEntry>(); 
        private string _historyStatusFilter = "Total";
        
        // Dialog State
        private ObservableCollection<TaskActionModel> _tempActions = new();
        private ObservableCollection<TaskTriggerModel> _tempTriggers = new();
        private bool _isEditMode = false;
        private bool _isPopulatingDetails = false;
        private bool _isFromTemplate = false;

        /// <summary>Pipeline being edited in the currently open task dialog.</summary>
        private TaskPipeline _tempPipeline = new();
        
        // Current folder path for new task creation
        private string _currentFolderPath = "\\";
        private Dictionary<string, bool> _folderExpandedState = new();

        public static MainPage? Current { get; private set; }

        public MainPage()
        {
            Current = this;
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ViewModel.SearchText = SearchBox.Text;
            };
            
            NavView.SelectedItem = NavAllTasks;
            ApplyLocalizedUi();

            SnoozeService.SnoozeChanged += SnoozeService_SnoozeChanged;
            TrayIconService.CustomSnoozeRequested += TrayIconService_CustomSnoozeRequested;
            UpdateSnoozeBanner();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            SnoozeService.SnoozeChanged -= SnoozeService_SnoozeChanged;
            TrayIconService.CustomSnoozeRequested -= TrayIconService_CustomSnoozeRequested;
            ViewModel.Cleanup();
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        // ========================================================================================================
        // Global Snooze (v1.9)
        // ========================================================================================================

        private void SnoozeService_SnoozeChanged(object? sender, EventArgs e)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                UpdateSnoozeBanner();
                TrayIconService.RefreshSnoozeState();
            });
        }

        private void TrayIconService_CustomSnoozeRequested()
        {
            DispatcherQueue?.TryEnqueue(() => ShowSnoozeDialog());
        }

        private void SnoozeToolbarButton_Click(object sender, RoutedEventArgs e) => ShowSnoozeDialog();

        private void StatusFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StatusFilterBox.SelectedItem is ComboBoxItem item)
                ViewModel.StatusFilter = Enum.TryParse<StatusFilter>(item.Tag?.ToString(), ignoreCase: true, out var status)
                    ? status : StatusFilter.All;
        }

        private void UpdateSnoozeBanner()
        {
            bool active = SnoozeService.IsActive;
            SnoozeBanner.IsOpen = active;

            // Toolbar button doubles as the snooze state indicator.
            SnoozeToolbarText.Text = active
                ? L("Snooze.Toolbar.Active", "Snoozed")
                : L("Snooze.Toolbar.Idle", "Snooze All");
            // Pause glyph while running normally, Play glyph while snoozed (clicking it resumes).
            SnoozeToolbarIcon.Glyph = active ? "" : "";
            ToolTipService.SetToolTip(SnoozeToolbarButton, active
                ? SnoozeService.StatusText
                : L("Snooze.Menu.SnoozeAll", "Snooze All Tasks..."));

            // A snoozed-only view must refresh when the suspended set changes.
            if (ViewModel.StatusFilter == StatusFilter.Snoozed) ViewModel.ApplyFilters();

            if (!active) return;

            SnoozeBanner.Title = SnoozeService.StatusText;
            SnoozeBanner.Message = Settings.SnoozeSuspendsScheduledTasks
                ? L("Snooze.Banner.Suspended", "Scheduled triggers are suspended and manual runs are blocked. Tasks are re-enabled when the snooze ends.")
                : L("Snooze.Banner.ManualOnly", "Runs started from this app are blocked. Windows will still fire scheduled triggers — enable \"Suspend scheduled triggers\" when snoozing to stop those too.");
            SnoozeBannerResumeBtn.Content = L("Snooze.Menu.Resume", "Resume All Tasks");
        }

        private void SnoozeResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SnoozeService.Cancel();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Failed to resume from global snooze.");
                _ = ShowErrorDialog(L("Snooze.Error.Resume", "Could not resume all tasks: ") + ex.Message);
            }
            UpdateSnoozeBanner();
            TrayIconService.RefreshSnoozeState();
        }

        private async void ShowSnoozeDialog()
        {
            if (this.Content?.XamlRoot == null) return;

            if (SnoozeService.IsActive)
            {
                // Already snoozing — offer to resume instead of stacking another window.
                var confirm = new ContentDialog
                {
                    Title = L("Snooze.Dialog.ActiveTitle", "Global Snooze is active"),
                    Content = SnoozeService.StatusText,
                    PrimaryButtonText = L("Snooze.Menu.Resume", "Resume All Tasks"),
                    CloseButtonText = L("Dialog.Common.Close", "Close"),
                    XamlRoot = this.Content.XamlRoot,
                    RequestedTheme = Settings.Theme
                };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary) SnoozeResume_Click(this, new RoutedEventArgs());
                return;
            }

            SnoozeDialog.Title = L("Snooze.Dialog.Title", "Snooze All Tasks");
            SnoozeDialog.PrimaryButtonText = L("Snooze.Dialog.Confirm", "Snooze");
            SnoozeDialog.CloseButtonText = L("Dialog.Common.Cancel", "Cancel");
            SnoozeDialogIntro.Text = L("Snooze.Dialog.Intro",
                "While snoozed, FluentTaskScheduler refuses to start any task — including chained pipeline runs.");
            Snooze30m.Content = L("Snooze.Duration.30m", "30 Minutes");
            Snooze1h.Content = L("Snooze.Duration.1h", "1 Hour");
            Snooze3h.Content = L("Snooze.Duration.3h", "3 Hours");
            SnoozeReboot.Content = L("Snooze.Duration.Reboot", "Until Next Reboot");
            SnoozeCustom.Content = L("Snooze.Duration.Custom", "Custom Time...");

            SnoozeSuspendTriggers.Content = L("Snooze.SuspendTriggers", "Also suspend scheduled triggers");
            SnoozeSuspendHint.Text = L("Snooze.SuspendTriggersHint",
                "Disables every enabled task through the Task Scheduler API and re-enables exactly those tasks when the snooze ends. Tasks under \\Microsoft\\ (Defender, Windows Update, maintenance) are never touched unless you enable the option below. Protected system tasks are skipped.");
            SnoozeIncludeMicrosoftTasks.Content = L("Snooze.IncludeMicrosoftTasks", "Also suspend Microsoft's own scheduled tasks (Defender, Windows Update, maintenance...)");
            SnoozeMicrosoftWarning.Message = L("Snooze.IncludeMicrosoftTasksWarning",
                "Not recommended: disabling these can break Windows security scans, updates, and maintenance until the snooze ends.");

            SnoozeSuspendTriggers.IsChecked = Settings.SnoozeSuspendsScheduledTasks;
            SnoozeIncludeMicrosoftTasks.IsChecked = Settings.SnoozeIncludeMicrosoftTasks;
            UpdateSnoozeSuspendOptionsVisibility();
            Snooze30m.IsChecked = true;
            SnoozeCustomDate.Date = DateTimeOffset.Now;
            SnoozeCustomTime.Time = DateTime.Now.AddHours(2).TimeOfDay;
            SnoozeDialogError.IsOpen = false;

            SnoozeDialog.XamlRoot = this.Content.XamlRoot;
            SnoozeDialog.RequestedTheme = Settings.Theme;
            await SnoozeDialog.ShowAsync();
        }

        private void SnoozeCustom_Changed(object sender, RoutedEventArgs e)
        {
            bool custom = SnoozeCustom.IsChecked == true;
            if (SnoozeCustomDate != null) SnoozeCustomDate.IsEnabled = custom;
            if (SnoozeCustomTime != null) SnoozeCustomTime.IsEnabled = custom;
        }

        private void SnoozeSuspendTriggers_Changed(object sender, RoutedEventArgs e) => UpdateSnoozeSuspendOptionsVisibility();

        private void UpdateSnoozeSuspendOptionsVisibility()
        {
            bool suspend = SnoozeSuspendTriggers.IsChecked == true;
            SnoozeIncludeMicrosoftTasks.Visibility = suspend ? Visibility.Visible : Visibility.Collapsed;
            SnoozeMicrosoftWarning.IsOpen = suspend && SnoozeIncludeMicrosoftTasks.IsChecked == true;
        }

        private void SnoozeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            try
            {
                // Persist the suspension preferences first: SnoozeService reads them while activating.
                Settings.SnoozeSuspendsScheduledTasks = SnoozeSuspendTriggers.IsChecked == true;
                Settings.SnoozeIncludeMicrosoftTasks = SnoozeSuspendTriggers.IsChecked == true && SnoozeIncludeMicrosoftTasks.IsChecked == true;

                if (SnoozeReboot.IsChecked == true)
                {
                    SnoozeService.SnoozeUntilReboot();
                }
                else if (SnoozeCustom.IsChecked == true)
                {
                    var end = SnoozeCustomDate.Date.Date + SnoozeCustomTime.Time;
                    if (end <= DateTime.Now)
                    {
                        args.Cancel = true;
                        SnoozeDialogError.Message = L("Snooze.Error.PastTime", "Pick a time in the future.");
                        SnoozeDialogError.IsOpen = true;
                        return;
                    }
                    SnoozeService.SnoozeUntilLocalTime(end);
                }
                else
                {
                    int minutes = 30;
                    foreach (var rb in new[] { Snooze30m, Snooze1h, Snooze3h })
                        if (rb.IsChecked == true && int.TryParse(rb.Tag?.ToString(), out int m)) minutes = m;

                    SnoozeService.Snooze(TimeSpan.FromMinutes(minutes));
                }

                UpdateSnoozeBanner();
                TrayIconService.RefreshSnoozeState();
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                Serilog.Log.Error(ex, "{Message}", "Failed to start global snooze from the dialog.");
                SnoozeDialogError.Message = ex.Message;
                SnoozeDialogError.IsOpen = true;
            }
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(ApplyLocalizedUi);
        }

        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        /// <summary>Public counterpart of <see cref="L"/> for x:Bind function calls from the task
        /// ListView's DataTemplate, which compiles into a separate generated class (see 3.2).</summary>
        public static string Loc(string key, string fallback) => LocalizationService.GetString(key, fallback);

        private static bool TryParseIsoDuration(string value, out TimeSpan result) => DurationUtil.TryParseIsoDuration(value, out result);

        public void RefreshLocalizedUi() => ApplyLocalizedUi();

        private void ApplyLocalizedUi()
        {
            NavDashboard.Content = L("Main.Nav.Dashboard", "Dashboard");
            NavQuickActions.Content = L("Main.Nav.QuickActions", "Quick Actions");
            NavScriptLibrary.Content = L("Main.Nav.Library", "Library");
            NavScriptEditor.Content = L("Main.Nav.ScriptEditor", "Script Editor");
            FoldersHeader.Text = L("Main.FoldersHeader", "Folders");

            // Per-task snooze, in the task detail dialog. These belong here and not in the global
            // snooze dialog's click handler: that handler only runs if the user opens "Snooze All",
            // which left these controls showing their raw XAML literals (or nothing at all).
            SnoozeTaskButton.Content = L("Task.Snooze.Label", "Snooze");
            SnoozeTask30m.Text = L("Snooze.Duration.30m", "30 Minutes");
            SnoozeTask1h.Text = L("Snooze.Duration.1h", "1 Hour");
            SnoozeTask3h.Text = L("Snooze.Duration.3h", "3 Hours");
            SnoozeTaskReboot.Text = L("Snooze.Duration.Reboot", "Until Next Reboot");
            SnoozeTaskCustom.Text = L("Snooze.Duration.Custom", "Custom Time...");
            SnoozeTaskCancel.Text = L("Task.Snooze.Resume", "Resume now");
            TaskSnoozeCustomApply.Content = L("Snooze.Dialog.Confirm", "Snooze");
            TaskSnoozeCustomCancel.Content = L("Dialog.Common.Cancel", "Cancel");
            TaskSnoozeCustomIntro.Text = L("Task.Snooze.CustomIntro", "Disable this task until:");
            NavAdd.Content = L("Main.Nav.NewTask", "New Task");
            NavAllTasks.Content = L("Main.Nav.AllTasks", "All Tasks");
            NavSettings.Content = L("Main.Nav.Settings", "Settings");

            // Toolbar status filter
            StatusFilterAll.Content = L("Main.Status.All", "All statuses");
            StatusFilterRunning.Content = L("Main.Status.Running", "Running");
            StatusFilterEnabled.Content = L("Main.Status.Enabled", "Enabled");
            StatusFilterDisabled.Content = L("Main.Status.Disabled", "Disabled");
            StatusFilterSnoozed.Content = L("Main.Status.Snoozed", "Snoozed");
            UpdateSnoozeBanner();

            RefreshButton.Content = L("Main.Toolbar.Refresh", "Refresh");
            ImportTaskButton.Content = L("Main.Toolbar.ImportTask", "Import Task");
            ShortcutsButton.Content = L("Main.Toolbar.ShortcutsButton", "?");
            ToolTipService.SetToolTip(ShortcutsButton, L("Main.Toolbar.ShortcutsTooltip", "Keyboard Shortcuts (F1)"));
            UpdateSortButtonText();

            // Batch action bar
            BatchRunBtnText.Text = L("Main.Batch.Run", "Run");
            ToolTipService.SetToolTip(BatchRunBtn, L("Main.Batch.RunTooltip", "Run Selected"));
            BatchStopBtnText.Text = L("Main.Batch.Stop", "Stop");
            ToolTipService.SetToolTip(BatchStopBtn, L("Main.Batch.StopTooltip", "Stop Selected"));
            BatchEnableBtnText.Text = L("Main.Batch.Enable", "Enable");
            ToolTipService.SetToolTip(BatchEnableBtn, L("Main.Batch.EnableTooltip", "Enable Selected"));
            BatchDisableBtnText.Text = L("Main.Batch.Disable", "Disable");
            ToolTipService.SetToolTip(BatchDisableBtn, L("Main.Batch.DisableTooltip", "Disable Selected"));
            BatchDeleteBtnText.Text = L("Main.Batch.Delete", "Delete");
            ToolTipService.SetToolTip(BatchDeleteBtn, L("Main.Batch.DeleteTooltip", "Delete Selected"));
            ToolTipService.SetToolTip(BatchCancelBtn, L("Main.Batch.ClearSelectionTooltip", "Clear Selection"));
            UpdateBatchCountText();

            CopyHistoryBtn.Content = L("Main.History.Copy", "📋 Copy");
            TaskHistoryDialog.Title = L("Main.HistoryDialog.Title", "Task History");
            TaskHistoryDialog.CloseButtonText = L("Dialog.Common.Close", "Close");

            ShortcutsDialog.Title = L("Main.ShortcutsDialog.Title", "Keyboard Shortcuts");
            ShortcutsDialog.CloseButtonText = L("Dialog.Common.Close", "Close");

            TaskDetailsDialog.CloseButtonText = L("Dialog.Common.Close", "Close");
            RunTaskButton.Content = L("Main.Task.RunNow", "Run Now");
            StopTaskButton.Content = L("Main.Task.Stop", "Stop");
            EditTaskButton.Content = L("Main.Task.Edit", "Edit");
            ExportTaskButton.Content = L("Main.Task.Export", "Export");
            DeleteTaskButton.Content = L("Main.Task.Delete", "Delete");

            TaskEditDialog.PrimaryButtonText = L("Dialog.Common.Save", "Save");
            TaskEditDialog.CloseButtonText = L("Dialog.Common.Cancel", "Cancel");

            AdminDragWarning.Title = L("Main.AdminDragWarning.Title", "Drag & Drop Restricted");
            AdminDragWarning.Message = L("Main.AdminDragWarning.Message", "Windows does not support drag-and-drop operations when the app is running as Administrator.");

            SearchBox.PlaceholderText = L("SearchBox.PlaceholderText", "Search tasks...");

            // --- Edit/Add Dialog ---
            // These used to come from x:Uid, which resolves against the Windows display language
            // instead of the app's language picker - hence German text in an English app.
            DlgTitleText.Text = L("DialogTitle.Text", "Add or edit task");
            DlgTriggersTitle.Text = L("TriggerTitle.Text", "Triggers");
            DlgActionsTitle.Text = L("ActionTitle.Text", "Actions");
            DlgRepetitionTitle.Text = L("RepetitionSectionTitle.Text", "Repetition");
            DlgRepeatEveryLabel.Text = L("RepetitionIntervalText.Text", "Repeat task every");
            DlgRepeatNone.Content = L("RepetitionNone.Content", "(No repetition)");
            DlgDurationLabel.Text = L("RepetitionDurationText.Text", "For a duration of");
            DlgRepeatIndefinitely.Content = L("RepetitionIndefinitely.Content", "Indefinitely");
            DlgConditionsTitle.Text = L("ConditionsTitle.Text", "Conditions");
            DlgStartOnlyIfLabel.Text = L("ConditionStartOnlyIf.Text", "Start the task only if:");
            DlgIdleForLabel.Text = L("ConditionIdleFor.Text", "Idle for:");
            DlgSpecificNetworkLabel.Text = L("ConditionSpecificNetwork.Text", "Specific network:");
            DlgAnyNetworkItem.Content = L("ConditionAnyNetwork.Content", "Any network");
            DlgSettingsTitle.Text = L("SettingsTitle.Text", "Settings");

            EditTaskOnlyIfIdle.Content = L("ConditionIdle.Content", "Computer is idle");
            EditTaskStopOnIdleEnd.Content = L("ConditionStopOnIdleEnd.Content", "Stop when idle ends");
            EditTaskOnlyIfAC.Content = L("ConditionAC.Content", "Computer is on AC power");
            EditTaskStopBatterySwitch.Content = L("ConditionStopBatterySwitch.Content", "Stop if switching to battery power");
            EditTaskOnBattery.Content = L("ConditionOnBattery.Content", "Computer is on battery power");
            EditTaskOnlyIfNetwork.Content = L("ConditionNetwork.Content", "Network is available");
            EditTaskWakeToRun.Content = L("ConditionWake.Content", "Wake the computer to run this task");
            EditTaskRunIfMissed.Content = L("RunIfMissed.Content", "Run task as soon as possible after a scheduled start is missed");
            EditTaskRestartOnFailure.Content = L("RestartOnFailure.Content", "If the task fails, restart every:");
            BrowseActionButton.Content = L("BrowseButton.Content", "Browse...");

            DlgTaskNameLabel.Text = L("Dialog.TaskName", "Task Name");
            DlgDescLabel.Text = L("Dialog.Description", "Description");
            DlgAuthorLabel.Text = L("Dialog.Author", "Author");
            DlgCategoryLabel.Text = L("Dialog.Category", "Category");
            DlgTagsLabel.Text = L("Dialog.Tags", "Tags");
            DlgEnabledLabel.Text = L("Dialog.Enabled", "Enabled");
            DlgPipelineLabel.Text = L("Pipeline.SectionTitle", "Completion Actions");
            ConfigurePipelineButton.Content = L("Pipeline.Configure", "Configure...");
            UpdatePipelineSummaryLabel();

            // Trigger types
            DlgTriggerTypeLabel.Text = L("Dialog.TriggerType", "Trigger Type");
            DlgTriggerDaily.Content = L("Dialog.Trigger.Daily", "Daily");
            DlgTriggerWeekly.Content = L("Dialog.Trigger.Weekly", "Weekly");
            DlgTriggerMonthly.Content = L("Dialog.Trigger.Monthly", "Monthly");
            DlgTriggerLogon.Content = L("Dialog.Trigger.AtLogon", "At Logon");
            DlgTriggerStartup.Content = L("Dialog.Trigger.AtStartup", "At Startup");
            DlgTriggerOnce.Content = L("Dialog.Trigger.OneTime", "One Time");
            DlgTriggerEvent.Content = L("Dialog.Trigger.OnEvent", "On an event");
            DlgTriggerSession.Content = L("Dialog.Trigger.Session", "On Workstation Lock/Unlock");

            EditTaskRandomDelay.Content = L("Dialog.RandomDelay", "Delay task for up to (random delay):");
            EditTaskStopAfter.Content = L("Dialog.StopAfter", "Stop task if runs longer than:");

            // Actions
            DlgProgramLabel.Text = L("Dialog.ProgramScript", "Program / Script");
            DlgArgsLabel.Text = L("Dialog.Arguments", "Arguments (optional)");
            DlgWorkDirLabel.Text = L("Dialog.WorkingDir", "Run in (optional)");
            DlgPsTip.Text = L("Dialog.PsTip", "Tip: For PowerShell scripts, use 'powershell.exe' as Program and '-ExecutionPolicy Bypass -File \"C:\\path\\to\\script.ps1\"' as Arguments");

            // Settings section
            EditTaskRunWithHighestPrivileges.Content = L("Dialog.HighPriv", "Run with highest privileges");
            EditTaskIsHidden.Content = L("Dialog.Hidden", "Hidden task");
            EditTaskDeleteExpired.Content = L("Dialog.DeleteExpired", "Delete the task if it is not scheduled to run again");
            EditTaskAllowHardTerminate.Content = L("Dialog.HardTerminate", "Allow task to be forcefully terminated");

            DlgMultiInstanceLabel.Text = L("Dialog.MultiInstance", "If the task is already running:");
            DlgMultiIgnore.Content = L("Dialog.Multi.Ignore", "Do not start a new instance");
            DlgMultiParallel.Content = L("Dialog.Multi.Parallel", "Run a new instance in parallel");
            DlgMultiQueue.Content = L("Dialog.Multi.Queue", "Queue a new instance");
            DlgMultiStop.Content = L("Dialog.Multi.Stop", "Stop the existing instance");

            DlgPriorityLabel.Text = L("Dialog.Priority", "Task Priority:");

            DlgRestartUpTo.Text = L("Dialog.RestartUpTo", "Attempt to restart up to:");
            DlgRestartTimes.Text = L("Dialog.RestartTimes", "times");

            // User Context
            DlgUserContextTitle.Text = L("Dialog.UserContext", "User Context");
            RunAsCurrentUser.Content = L("Dialog.RunAsCurrent", "Run as current user");
            RunAsSpecificUser.Content = L("Dialog.RunAsSpecific", "Run as specific user");
            RunAsSystem.Content = L("Dialog.RunAsSystem", "Run as SYSTEM (requires admin)");

            // Placeholder texts
            EditTaskName.PlaceholderText = L("Dialog.Ph.TaskName", "Enter task name");
            EditTaskCategory.PlaceholderText = L("Dialog.Ph.Category", "e.g. Work");
            EditTaskTags.PlaceholderText = L("Dialog.Ph.Tags", "e.g. urgent, sync (comma separated)");
            EditTaskActionCommand.PlaceholderText = L("Dialog.Ph.Command", "e.g., notepad.exe or C:\\Scripts\\myscript.ps1");
            EditTaskArguments.PlaceholderText = L("Dialog.Ph.Args", "e.g., /c echo hello or -File script.ps1");
            EditTaskWorkingDirectory.PlaceholderText = L("Dialog.Ph.WorkDir", "e.g., C:\\Scripts");
            EditTaskRandomDelayVal.PlaceholderText = L("Dialog.Ph.Delay", "e.g. 1 hour");
            EditTaskIdleDuration.PlaceholderText = L("Dialog.Ph.Idle", "e.g., 10m");
            EditTaskIdleDurationSetting.PlaceholderText = L("Dialog.Ph.Idle", "e.g., 10m");
            MonthlyDaysInput.PlaceholderText = L("Dialog.Ph.MonthDays", "e.g. 1, 15, Last");
            EditTaskRestartInterval.PlaceholderText = L("Dialog.Ph.RestartInt", "e.g. 1 minute");
            EditTaskRunAsUser.PlaceholderText = L("Dialog.Ph.Username", "DOMAIN\\Username or username@domain.com");
            EditTaskRunAsUser.Header = L("Dialog.UsernameHeader", "Username");
            EditTaskEventLog.Header = L("Dialog.EventLog", "Log");
            EditTaskEventLog.PlaceholderText = L("Dialog.Ph.EventLog", "Application, System, Security, etc.");
            EditTaskEventSource.Header = L("Dialog.EventSource", "Source");
            EditTaskEventSource.PlaceholderText = L("Dialog.Ph.EventSource", "e.g., VSS, Outlook (Optional)");
            EditTaskEventId.Header = L("Dialog.EventId", "Event ID");
            EditTaskEventId.PlaceholderText = L("Dialog.Ph.EventId", "e.g., 1000 (Optional)");

            // Hint texts
            DlgDelayHint.Text = L("Dialog.Hint.Delay", "(e.g. 30s, 1m, 1h)");
            DlgIdleHint.Text = L("Dialog.Hint.Idle", "(e.g. 5m, 10m, 30m)");
            DlgRestartHint.Text = L("Dialog.Hint.Restart", "(e.g. 30s, 1m, 5m)");
            DlgMonthlyDaysHint.Text = L("Dialog.Hint.MonthDays", "(comma separated, use 'Last' for last day)");

            // Daily/Weekly/Monthly labels
            EditTaskDailyRecurrence.Content = L("Dialog.RecurEvery", "Recur every");
            DlgDaysSuffix.Text = L("Dialog.DaysSuffix", "day(s)");
            DlgWeeklyRecur.Text = L("Dialog.RecurEvery", "Recur every");
            DlgWeeksOn.Text = L("Dialog.WeeksOn", "weeks on:");
            DlgMonthsLabel.Text = L("Dialog.Months", "Months:");
            MonthlyRadioDays.Content = L("Dialog.Days", "Days");
            MonthlyRadioOn.Content = L("Dialog.On", "On");
            DlgIdleWait.Text = L("Dialog.IdleWait", "Wait for the computer to be idle for:");

            // Weekday checkboxes
            WeeklyMon.Content = L("Dialog.Day.Mon", "Mon");
            WeeklyTue.Content = L("Dialog.Day.Tue", "Tue");
            WeeklyWed.Content = L("Dialog.Day.Wed", "Wed");
            WeeklyThu.Content = L("Dialog.Day.Thu", "Thu");
            WeeklyFri.Content = L("Dialog.Day.Fri", "Fri");
            WeeklySat.Content = L("Dialog.Day.Sat", "Sat");
            WeeklySun.Content = L("Dialog.Day.Sun", "Sun");

            // Month abbreviations
            MonthJan.Content = L("Dialog.Month.Jan", "Jan");
            MonthFeb.Content = L("Dialog.Month.Feb", "Feb");
            MonthMar.Content = L("Dialog.Month.Mar", "Mar");
            MonthApr.Content = L("Dialog.Month.Apr", "Apr");
            MonthMay.Content = L("Dialog.Month.May", "May");
            MonthJun.Content = L("Dialog.Month.Jun", "Jun");
            MonthJul.Content = L("Dialog.Month.Jul", "Jul");
            MonthAug.Content = L("Dialog.Month.Aug", "Aug");
            MonthSep.Content = L("Dialog.Month.Sep", "Sep");
            MonthOct.Content = L("Dialog.Month.Oct", "Oct");
            MonthNov.Content = L("Dialog.Month.Nov", "Nov");
            MonthDec.Content = L("Dialog.Month.Dec", "Dec");

            // Monthly week ordinals
            DlgWeekFirst.Content = L("Dialog.Week.First", "First");
            DlgWeekSecond.Content = L("Dialog.Week.Second", "Second");
            DlgWeekThird.Content = L("Dialog.Week.Third", "Third");
            DlgWeekFourth.Content = L("Dialog.Week.Fourth", "Fourth");
            DlgWeekLast.Content = L("Dialog.Week.Last", "Last");

            // Monthly day names
            DlgDayMon.Content = L("Dialog.Weekday.Mon", "Monday");
            DlgDayTue.Content = L("Dialog.Weekday.Tue", "Tuesday");
            DlgDayWed.Content = L("Dialog.Weekday.Wed", "Wednesday");
            DlgDayThu.Content = L("Dialog.Weekday.Thu", "Thursday");
            DlgDayFri.Content = L("Dialog.Weekday.Fri", "Friday");
            DlgDaySat.Content = L("Dialog.Weekday.Sat", "Saturday");
            DlgDaySun.Content = L("Dialog.Weekday.Sun", "Sunday");

            // Session state items
            EditTaskSessionStateType.Header = L("Dialog.TriggerOn", "Trigger on");
            DlgSessLock.Content = L("Dialog.Sess.Lock", "Workstation Lock");
            DlgSessUnlock.Content = L("Dialog.Sess.Unlock", "Workstation Unlock");
            DlgSessRdpOn.Content = L("Dialog.Sess.RdpConnect", "Remote Desktop Connect");
            DlgSessRdpOff.Content = L("Dialog.Sess.RdpDisconnect", "Remote Desktop Disconnect");

            EditTaskExpires.Content = L("Dialog.Expire", "Expire task on:");

            // Action menu items
            DlgActionRunProg.Text = L("Dialog.Action.RunProg", "Run Program");
            DlgActionEmail.Text = L("Dialog.Action.Email", "Send Email");
            DlgActionNotif.Text = L("Dialog.Action.Notif", "Show Notification");
            BrowseActionButton.Content = L("Dialog.Browse", "Browse...");

            // Time duration items - Stop After
            DlgStop15m.Content = L("Dialog.Time.15m", "15 minutes");
            DlgStop30m.Content = L("Dialog.Time.30m", "30 minutes");
            DlgStop1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgStop2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgStop4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgStop8h.Content = L("Dialog.Time.8h", "8 hours");
            DlgStop12h.Content = L("Dialog.Time.12h", "12 hours");
            DlgStop1d.Content = L("Dialog.Time.1d", "1 day");
            DlgStop2d.Content = L("Dialog.Time.2d", "2 days");
            DlgStop3d.Content = L("Dialog.Time.3d", "3 days");
            DlgStop5d.Content = L("Dialog.Time.5d", "5 days");

            // Repetition interval items
            DlgRep5m.Content = L("Dialog.Time.5m", "5 minutes");
            DlgRep10m.Content = L("Dialog.Time.10m", "10 minutes");
            DlgRep15m.Content = L("Dialog.Time.15m", "15 minutes");
            DlgRep30m.Content = L("Dialog.Time.30m", "30 minutes");
            DlgRep1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgRep2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgRep4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgRep6h.Content = L("Dialog.Time.6h", "6 hours");
            DlgRep12h.Content = L("Dialog.Time.12h", "12 hours");

            // Repetition duration items
            DlgDur1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgDur2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgDur4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgDur6h.Content = L("Dialog.Time.6h", "6 hours");
            DlgDur12h.Content = L("Dialog.Time.12h", "12 hours");
            DlgDur24h.Content = L("Dialog.Time.24h", "24 hours");
            DlgDur1d.Content = L("Dialog.Time.1d", "1 day");

            // Priority items
            DlgPri0.Content = L("Dialog.Pri.Realtime", "Realtime (0)");
            DlgPri1.Content = L("Dialog.Pri.High", "High (1)");
            DlgPri3.Content = L("Dialog.Pri.AboveNormal", "Above Normal (3)");
            DlgPri7.Content = L("Dialog.Pri.Normal", "Normal (7)");
            DlgPri9.Content = L("Dialog.Pri.BelowNormal", "Below Normal (9)");
            DlgPri10.Content = L("Dialog.Pri.Idle", "Idle (10)");

            // InfoBars
            NetworkAdminNotice.Title = L("Dialog.NetAdmin.Title", "Administrator required");
            NetworkAdminNotice.Message = L("Dialog.NetAdmin.Msg", "Specific network selection requires the app to run as administrator.");
            SystemUserWarning.Title = L("Dialog.SysWarn.Title", "Administrator Privileges Required");
            SystemUserWarning.Message = L("Dialog.SysWarn.Msg", "To create tasks that run as SYSTEM, this application must be running with administrator privileges. Right-click the app and select \"Run as administrator\".");

            if (NavView.SelectedItem is NavigationViewItem selectedItem && selectedItem.Tag != null)
            {
                string tag = selectedItem.Tag.ToString() ?? string.Empty;
                NavView.Header = tag switch
                {
                    "Dashboard" => L("Main.Header.Dashboard", "Dashboard"),
                    "QuickActions" => L("Main.Header.QuickActions", "Quick Actions"),
                    "ScriptLibrary" => L("Main.Header.Library", "Library"),
                    "ScriptEditor" => L("Main.Header.ScriptEditor.Text", "Script Editor"),
                    "settings" => L("Main.Header.Settings", "Settings"),
                    _ => L("Main.Header.ScheduledTasks", "Scheduled Tasks")
                };
            }
            else
            {
                NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            }
        }

        public void OpenCreateTaskFromTemplate(ViewModels.ScriptTemplateModel template) => OpenCreateTaskDialog(template);
        private void NewTaskButton_Click(object sender, RoutedEventArgs e) => OpenCreateTaskDialog(null);

        // ========================================================================================================
        // Completion actions / pipelines (v1.9)
        // ========================================================================================================

        private void UpdatePipelineSummaryLabel()
        {
            if (DlgPipelineSummary == null) return;

            if (_tempPipeline == null || !_tempPipeline.HasAnyTargets)
            {
                DlgPipelineSummary.Text = L("Pipeline.Summary.None", "No downstream tasks configured.");
                return;
            }

            string state = _tempPipeline.IsEnabled
                ? L("Pipeline.Summary.Enabled", "Enabled")
                : L("Pipeline.Summary.Disabled", "Disabled");

            DlgPipelineSummary.Text = string.Format(
                L("Pipeline.Summary.Format", "{0} — {1} on success, {2} on failure"),
                state, _tempPipeline.OnSuccessTasks.Count, _tempPipeline.OnFailureTasks.Count);
        }

        private async void ConfigurePipeline_Click(object sender, RoutedEventArgs e)
        {
            if (this.Content?.XamlRoot == null) return;

            try
            {
                string ownPath = _isEditMode && ViewModel.SelectedTask != null ? ViewModel.SelectedTask.Path : "";

                var available = await System.Threading.Tasks.Task.Run(
                    () => ViewModel.TaskService.GetAllTasks(recursive: true));

                var dialog = new Dialogs.TaskPipelineDialog(ownPath, _tempPipeline, available)
                {
                    XamlRoot = this.Content.XamlRoot
                };

                // WinUI allows only one dialog per XamlRoot, so the editor must step aside.
                TaskEditDialog.Hide();
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _tempPipeline = dialog.Result;
                    UpdatePipelineSummaryLabel();
                }

                await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Failed to open the completion actions dialog.");
                await ShowErrorDialog(L("Pipeline.Error.Open", "Could not open completion actions: ") + ex.Message);
            }
        }

        /// <summary>Opens the task editor pre-filled from a built-in task template.</summary>
        public async void OpenCreateTaskFromTaskTemplate(TaskTemplate template)
        {
            if (this.Content?.XamlRoot == null || template == null) return;

            try
            {
                try { TaskDetailsDialog.Hide(); } catch { }

                var model = Services.TaskTemplateLibrary.ToTaskModel(template);

                _isEditMode = false;
                _isFromTemplate = true;
                _isPopulatingDetails = true;
                _tempPipeline = new TaskPipeline();

                EditTaskName.Text = model.Name;
                EditTaskDescription.Text = model.Description;
                EditTaskAuthor.Text = model.Author;
                EditTaskCategory.Text = model.Category;
                EditTaskTags.Text = string.Join(", ", model.Tags);
                EditTaskEnabled.IsOn = true;

                _tempActions = new ObservableCollection<TaskActionModel>(model.Actions);
                _tempTriggers = new ObservableCollection<TaskTriggerModel>(model.TriggersList);
                ActionList.ItemsSource = _tempActions;
                TriggerList.ItemsSource = _tempTriggers;

                EditTaskRunWithHighestPrivileges.IsChecked = model.RunWithHighestPrivileges;
                EditTaskRunIfMissed.IsChecked = model.RunIfMissed;
                EditTaskOnlyIfIdle.IsChecked = model.OnlyIfIdle;
                EditTaskOnlyIfAC.IsChecked = model.OnlyIfAC;
                EditTaskWakeToRun.IsChecked = model.WakeToRun;

                PopulateNetworkList();
                UpdatePipelineSummaryLabel();

                _isPopulatingDetails = false;
                ActionList.SelectedIndex = 0;
                TriggerList.SelectedIndex = 0;

                EditTaskErrorBar.IsOpen = false;
                TaskEditDialog.XamlRoot = this.Content.XamlRoot;
                await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Failed to open the editor for template '{template.Id}'.");
                await ShowErrorDialog(L("Templates.Error.Deploy", "Could not open this template: ") + ex.Message);
            }
        }

        private async void OpenCreateTaskDialog(ViewModels.ScriptTemplateModel? template)
        {
            if (this.Content?.XamlRoot == null) return;
            // WinUI only allows one open ContentDialog per XamlRoot - avoid throwing if
            // Task Details (or another dialog) is already showing when Ctrl+N is pressed.
            try { TaskDetailsDialog.Hide(); } catch { }
            _isEditMode = false;
            _isFromTemplate = template != null;
            _tempPipeline = new TaskPipeline();
            UpdatePipelineSummaryLabel();

            EditTaskName.Text = template?.Name ?? "";
            EditTaskDescription.Text = template?.Description ?? "";
            EditTaskAuthor.Text = Environment.UserName;
            EditTaskCategory.Text = "";
            EditTaskTags.Text = "";
            EditTaskEnabled.IsOn = true;
            
            _tempActions = new ObservableCollection<TaskActionModel>();
            if (template != null)
            {
                _tempActions.Add(new TaskActionModel { Command = template.Command, Arguments = template.Arguments });
            }
            else
            {
                _tempActions.Add(new TaskActionModel { Command = "notepad.exe" });
            }

            _tempTriggers = new ObservableCollection<TaskTriggerModel> { new TaskTriggerModel { TriggerType = TriggerType.Daily, ScheduleInfo = FormatScheduleInfo(DateTime.Now), DailyInterval = 1 } };
            
            ActionList.ItemsSource = _tempActions;
            TriggerList.ItemsSource = _tempTriggers;
            ActionList.SelectedIndex = 0;
            TriggerList.SelectedIndex = 0;
            
            // Settings defaults
            EditTaskRunWithHighestPrivileges.IsChecked = template?.RunAsAdmin ?? false;

            PopulateNetworkList();
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            await TaskEditDialog.ShowAsync();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFolderStructure();
            _ = ViewModel.LoadTasksAsync();
            TaskListView.Focus(FocusState.Programmatic);
            UpdateFolderTreeMaxHeight();

            // Restore the last-used folder (see 2.2)
            string saved = Settings.LastFolderPath;
            if (!string.IsNullOrEmpty(saved) && saved != "\\")
            {
                _currentFolderPath = saved;
                ViewModel.SetFilter(saved);
                SelectFolderTreeNodeForPath(saved);
            }

            // Defer one frame so the ListView control template is fully applied before we set its internal ScrollViewer
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrollingSelf(Settings.SmoothScrolling);
                
                // Set custom title bar drag region
                App.m_window?.SetTitleBar(AppTitleBarDragArea);

                // Check for elevation and handle drag-and-drop limitations
                if (Helpers.ElevationHelper.IsElevated())
                {
                    AdminDragWarning.Visibility = Visibility.Collapsed; // We handle it via custom drag
                    TaskListView.CanDragItems = false;
                    TaskListView.AllowDrop = false;
                    FolderTreeView.AllowDrop = false;
                    
                    // Hook up custom drag events
                    this.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCustomDragPointerPressed), true);
                    this.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnCustomDragPointerMoved), true);
                    this.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnCustomDragPointerReleased), true);
                    // Note: Folder item dragging is disabled via early-return in FolderItem_DragStarting
                }
            });

            // Show startup dialogs in order: onboarding first, then changelog
            _ = CheckStartupDialogsAsync();
        }

        private async System.Threading.Tasks.Task CheckStartupDialogsAsync()
        {
            // Await onboarding first — on a fresh install the user must finish the
            // walkthrough before the "What's New" popup is shown on top.
            await CheckAndShowOnboardingAsync();

            // Only reaches here once onboarding is fully dismissed.
            await CheckAndShowChangelogAsync();
        }

        private async System.Threading.Tasks.Task CheckAndShowOnboardingAsync()
        {
            if (Settings.HasCompletedOnboarding) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = Settings.Theme };
                    await dialog.ShowAsync();
                }
                catch { /* XamlRoot not ready or dialog already open — skip silently */ }
                finally { tcs.TrySetResult(); }
            });
            await tcs.Task;
        }

        private async System.Threading.Tasks.Task CheckAndShowChangelogAsync()
        {
            try
            {
                var release = await Services.GitHubReleaseService.GetLatestReleaseAsync();
                if (release == null) return;

                string lastSeen = Settings.LastSeenVersion;
                if (string.Equals(release.TagName, lastSeen, StringComparison.OrdinalIgnoreCase)) return;

                // New version — marshal back to UI thread via TCS
                var tcs = new System.Threading.Tasks.TaskCompletionSource();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new Dialogs.WhatsNewDialog(release)
                        {
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Settings.Theme
                        };
                        await dialog.ShowAsync();
                        // Only persist after the user has actually seen the dialog
                        Settings.LastSeenVersion = release.TagName;
                    }
                    catch { /* dialog already open or XamlRoot not ready — skip silently */ }
                    finally { tcs.TrySetResult(); }
                });
                await tcs.Task;
            }
            catch { /* network unavailable or any other error — fail silently */ }
        }

        /// <summary>Directly applies smooth scrolling to all ScrollViewers owned by MainPage,
        /// including hidden dialog content and the ListView's internal ScrollViewer.
        /// Called both from Loaded and from the Settings toggle handler.</summary>
        public void ApplySmoothScrollingSelf(bool enable)
        {
            DetailsScrollViewer.IsScrollInertiaEnabled = enable;
            EditScrollViewer.IsScrollInertiaEnabled = enable;
            HistoryScrollViewer.IsScrollInertiaEnabled = enable;
            // TaskListView has an internal ScrollViewer in its control template
            foreach (var sv in FindDescendants<ScrollViewer>(TaskListView))
                sv.IsScrollInertiaEnabled = enable;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T match) return match;
            return FindParent<T>(parent);
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFolderTreeMaxHeight();


        private void UpdateFolderTreeMaxHeight()
        {
            if (NavView == null || FolderTreeView == null) return;
            // Estimated height of Footer Items (4 items + Settings) + Header ("New Task") + Margins
            // Footer: ~200px
            // Header (PaneCustomContent top part): 
            //   Dashboard (40) + ScriptLib (40) + NewTask (40) + Separator (10) + Margins (~20) = ~150px
            // "Folders" Label: ~30px
            // Buffer: ~50px 
            // Total deduction: ~430px
            double availableHeight = NavView.ActualHeight - 430; 
            if (availableHeight < 100) availableHeight = 100;
            FolderTreeView.MaxHeight = availableHeight;
        }

        // ========================================================================================================
        // Navigation & Loading
        // ========================================================================================================

        private void LoadFolderStructure()
        {
            try
            {
                var rootFolder = ViewModel.TaskService.GetFolderStructure();

                // Unregister the previous pass's property-changed callbacks before discarding those
                // nodes — RegisterPropertyChangedCallback tokens are otherwise never released, which
                // leaks a callback per folder on every reload (3.11).
                foreach (var kv in _treeNodeCallbackTokens)
                    kv.Key.UnregisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, kv.Value);
                _treeNodeCallbackTokens.Clear();

                _treeNodeFolderMap.Clear();
                FolderTreeView.RootNodes.Clear();
                AddFolderToTree(rootFolder, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
        }

        private Dictionary<TreeViewNode, TaskFolderModel> _treeNodeFolderMap = new();
        private Dictionary<TreeViewNode, long> _treeNodeCallbackTokens = new();

        /// <summary>Expands and selects the tree node for the given folder path, if it still exists
        /// (used to restore the last-used folder — see 2.2).</summary>
        private void SelectFolderTreeNodeForPath(string path)
        {
            var entry = _treeNodeFolderMap.FirstOrDefault(kv => string.Equals(kv.Value.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry.Key == null) return;

            for (var ancestor = entry.Key.Parent; ancestor != null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;

            FolderTreeView.SelectedNode = entry.Key;
        }

        private void AddFolderToTree(TaskFolderModel folder, TreeViewNode? parentNode)
        {
            var displayName = folder.Name == "\\" ? "Task Scheduler Library" : folder.Name;
            var treeNode = new TreeViewNode
            {
                Content = displayName,  
                IsExpanded = _folderExpandedState.ContainsKey(folder.Path) ? _folderExpandedState[folder.Path] : (folder.Path == "\\")
            };

            // Store folder in our mapping dictionary
            _treeNodeFolderMap[treeNode] = folder;

            // Track expansion state changes
            long token = treeNode.RegisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, (sender, dp) =>
            {
                if (sender is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var f))
                    _folderExpandedState[f.Path] = node.IsExpanded;
            });
            _treeNodeCallbackTokens[treeNode] = token;
            
            // Add to parent or root
            if (parentNode != null)
                parentNode.Children.Add(treeNode);
            else
                FolderTreeView.RootNodes.Add(treeNode);

            // Add subfolders
            foreach (var sub in folder.SubFolders)
                AddFolderToTree(sub, treeNode);
        }

        private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var folder))
            {
                _currentFolderPath = folder.Path;
                Settings.LastFolderPath = folder.Path;
                ViewModel.SetFilter(folder.Path);
                
                // Restore Task View
                NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                TasksViewGrid.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Collapsed;
                
                NavView.SelectedItem = null; // Native indicator for Dashboard/ScriptLib disappears
                FolderTreeView.SelectedItem = node; 
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected || (args.SelectedItem is NavigationViewItem settingsItem && settingsItem.Tag?.ToString() == "settings")) 
            {
                 NavView.Header = L("Main.Header.Settings", "Settings");
                 ContentFrame.Visibility = Visibility.Visible;
                 TasksViewGrid.Visibility = Visibility.Collapsed;
                 ContentFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                var tag = item.Tag.ToString() ?? "";

                if (tag == "Dashboard")
                {
                    NavView.Header = L("Main.Header.Dashboard", "Dashboard");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(DashboardPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptLibrary")
                {
                    NavView.Header = L("Main.Header.Library", "Library");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptLibraryPage), this);
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptEditor")
                {
                    NavView.Header = L("Main.Header.ScriptEditor.Text", "Script Editor");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptEditorPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "QuickActions")
                {
                    NavView.Header = L("Main.Header.QuickActions", "Quick Actions");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(QuickActionsPage));
                    FolderTreeView.SelectedItem = null;
                }
                else
                {
                    // Standard Task Views (if any)
                    NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                    TasksViewGrid.Visibility = Visibility.Visible;
                    ContentFrame.Visibility = Visibility.Collapsed;
                    FolderTreeView.SelectedItem = null;

                    if (tag.StartsWith("\\"))
                        _currentFolderPath = tag;
                    
                    ViewModel.SetFilter(tag);
                }
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "Add")
            {
                NewTaskButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadTasksAsync();
        private void ImportTask_Click(object sender, RoutedEventArgs e) => ImportTask(); // Implement if needed, kept generic

        public async void NavigateToTask(string taskPath)
        {
            // Switch to Tasks View
            NavView.SelectedItem = null; // Clear selection to indicate custom state or select "All Tasks"
            NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            TasksViewGrid.Visibility = Visibility.Visible;
            ContentFrame.Visibility = Visibility.Collapsed;
            FolderTreeView.SelectedItem = null;

            // Set filter to show this task (or all tasks)
            _currentFolderPath = System.IO.Path.GetDirectoryName(taskPath) ?? "\\";
            ViewModel.SetFilter("all"); // Reset filter to show everything in the folder, or just "all" global
            
            // Wait for load if needed
            if (ViewModel.FilteredTasks.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadTasksAsync();
            }

            // Find the task
            var task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            
            // If not found in current view, try to load specific folder? 
            // For now, let's assume it's in the list if we load all. 
            // Actually SetFilter("all") loads everything? No, SetFilter("all") is global filter.
            
            if (task == null)
            {
                // Try reloading
                await ViewModel.LoadTasksAsync();
                task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            }

            if (task != null)
            {
                ViewModel.SelectedTask = task;
                TaskListView.ScrollIntoView(task);
                await ShowTaskDetails();
            }
        }

        // ========================================================================================================
        // Task List & Selection
        // ========================================================================================================

        private async void TaskListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            
            if (TaskListView.SelectedItems.Count > 1 || ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) || shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;

            if (e.ClickedItem is ScheduledTaskModel task)
            {
               ViewModel.SelectedTask = task;
               await ShowTaskDetails();
            }
        }

        private void TaskListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (ScheduledTaskModel added in e.AddedItems) added.IsSelected = true;
            foreach (ScheduledTaskModel removed in e.RemovedItems) removed.IsSelected = false;

            int count = TaskListView.SelectedItems.Count;
            if (BatchActionBar != null)
            {
                BatchActionBar.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
                UpdateBatchCountText();
                UpdateBatchActionsState();
            }
            if (count == 1) ViewModel.SelectedTask = (ScheduledTaskModel)TaskListView.SelectedItem;
        }

        private void UpdateBatchCountText()
        {
            if (BatchCountText == null) return;
            BatchCountText.Text = string.Format(L("Main.Batch.SelectedCountFormat", "{0} selected"), TaskListView.SelectedItems.Count);
        }

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
             if (sender is CheckBox cb && cb.DataContext is ScheduledTaskModel task)
             {
                 var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                 bool isShiftHeld = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                 if (isShiftHeld && ViewModel.SelectedTask != null && ViewModel.SelectedTask != task)
                 {
                     var list = FilteredTasks;
                     int start = list.IndexOf(ViewModel.SelectedTask);
                     int end = list.IndexOf(task);

                     if (start > -1 && end > -1)
                     {
                         int min = Math.Min(start, end);
                         int max = Math.Max(start, end);
                         for (int i = min; i <= max; i++)
                         {
                             if (!TaskListView.SelectedItems.Contains(list[i])) TaskListView.SelectedItems.Add(list[i]);
                         }
                     }
                 }
                 else
                 {
                     if (cb.IsChecked == true) { TaskListView.SelectedItems.Add(task); ViewModel.SelectedTask = task; }
                     else TaskListView.SelectedItems.Remove(task);
                 }
             }
        }
        
        private void ToggleSwitch_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true; // Prevent row click

        // Set while a ListView container is being recycled/rebound to a different task, so the
        // resulting programmatic IsOn change (which still raises Toggled) isn't mistaken for a user
        // click. Replaces a FocusState-based heuristic that broke for touch/UIA input (3.11).
        private bool _isPopulatingToggle = false;

        private void TaskListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            _isPopulatingToggle = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isPopulatingToggle = false);
        }

        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsLoading || _isPopulatingToggle) return;
            if (sender is ToggleSwitch ts && ts.IsLoaded && ts.DataContext is ScheduledTaskModel task)
            {
                try
                {
                    if (task.IsEnabled != ts.IsOn) 
                        ViewModel.TaskService.SetTaskEnabled(task.Path, ts.IsOn);
                    task.IsEnabled = ts.IsOn;
                }
                catch (Exception ex) 
                { 
                    // Revert UI if failed
                    ts.Toggled -= ToggleSwitch_Toggled;
                    ts.IsOn = !ts.IsOn;
                    ts.Toggled += ToggleSwitch_Toggled;
                    await ShowErrorDialog(ex.Message);
                }
            }
        }

        // ========================================================================================================
        // Task Details & History
        // ========================================================================================================

        private async Task ShowTaskDetails()
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            DialogTaskName.Text = task.Name;
            DialogTaskDescription.Text = task.Description;
            DialogTaskAuthor.Text = task.Author;
            DialogTaskCategory.Text = task.Category;
            DialogTaskTagsItems.ItemsSource = task.Tags;
            DialogTaskTagsPanel.Visibility = task.Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DialogTaskCategory.Visibility = !string.IsNullOrEmpty(task.Category) ? Visibility.Visible : Visibility.Collapsed;
            
            // Load History
            _fullHistory = await Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            UpdateHistoryList();
            UpdateHistoryStats();
            
            UpdateTaskSnoozeUi();

            TaskDetailsDialog.XamlRoot = this.Content.XamlRoot;
            await TaskDetailsDialog.ShowAsync();
        }

        // ── Per-task snooze ─────────────────────────────────────────────────────

        /// <summary>
        /// Syncs the detail dialog's snooze controls with the selected task: the status line and
        /// "Resume now" only make sense while that task is actually snoozed.
        /// </summary>
        private void UpdateTaskSnoozeUi()
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            string status = TaskSnoozeService.StatusTextFor(task.Path);
            bool snoozed = !string.IsNullOrEmpty(status);

            TaskSnoozeStatusText.Text = status;
            TaskSnoozeStatusText.Visibility = snoozed ? Visibility.Visible : Visibility.Collapsed;
            SnoozeTaskCancel.Visibility = snoozed ? Visibility.Visible : Visibility.Collapsed;

            // The picker is transient — never leave it open across tasks or reopens.
            TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
            TaskSnoozeCustomError.IsOpen = false;
        }

        private void SnoozeTaskDuration_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;
            if (sender is not MenuFlyoutItem item || !int.TryParse(item.Tag?.ToString(), out int minutes)) return;

            TaskSnoozeService.Snooze(task.Path, TimeSpan.FromMinutes(minutes));
            AfterTaskSnoozeChanged();
        }

        private void SnoozeTaskUntilReboot_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            TaskSnoozeService.SnoozeUntilReboot(task.Path);
            AfterTaskSnoozeChanged();
        }

        private void SnoozeTaskCancel_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            TaskSnoozeService.Cancel(task.Path);
            AfterTaskSnoozeChanged();
        }

        /// <summary>
        /// Reveals the inline custom-end-time picker. This cannot be a ContentDialog of its own:
        /// it is invoked from inside TaskDetailsDialog, and WinUI permits only one ContentDialog
        /// open at a time — the nested ShowAsync threw and the click appeared to do nothing.
        /// </summary>
        private void SnoozeTaskCustom_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            TaskSnoozeCustomError.IsOpen = false;
            TaskSnoozeCustomDate.Date = DateTimeOffset.Now;
            TaskSnoozeCustomTime.Time = DateTime.Now.AddHours(1).TimeOfDay;
            TaskSnoozeCustomPanel.Visibility = Visibility.Visible;
        }

        private void TaskSnoozeCustomCancel_Click(object sender, RoutedEventArgs e)
        {
            TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
            TaskSnoozeCustomError.IsOpen = false;
        }

        private void TaskSnoozeCustomApply_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            try
            {
                var end = TaskSnoozeCustomDate.Date.Date + TaskSnoozeCustomTime.Time;
                if (end <= DateTime.Now)
                {
                    TaskSnoozeCustomError.Message = L("Snooze.Error.PastTime", "Pick a time in the future.");
                    TaskSnoozeCustomError.IsOpen = true;
                    return;
                }

                TaskSnoozeService.SnoozeUntilLocalTime(task.Path, end);
                TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
                TaskSnoozeCustomError.IsOpen = false;
                AfterTaskSnoozeChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Failed to snooze task '{task.Path}' until a custom time.");
                TaskSnoozeCustomError.Message = ex.Message;
                TaskSnoozeCustomError.IsOpen = true;
            }
        }

        /// <summary>Refreshes the dialog and the task list after a task's snooze state changed.</summary>
        private void AfterTaskSnoozeChanged()
        {
            UpdateTaskSnoozeUi();
            _ = ViewModel.LoadTasksAsync();
        }

        private void CategoryBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            string cat = "";
            if (sender is Border b && b.Child is TextBlock tb) cat = tb.Text;
            else if (sender is Grid g && g.Children.LastOrDefault() is TextBlock tbg) cat = tbg.Text;

            if (!string.IsNullOrEmpty(cat))
            {
                SearchBox.Text = cat;
                TaskDetailsDialog.Hide();
            }
        }

        private void TagBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Border b && b.Child is TextBlock tb)
            {
                SearchBox.Text = tb.Text;
                TaskDetailsDialog.Hide();
            }
        }

        private void UpdateHistoryList()
        {
            if (InlineHistoryListView == null) return;

            IEnumerable<TaskHistoryEntry> filtered = _fullHistory;

            // Date filtering (Combo)
            if (HistoryFilterCombo != null && HistoryFilterCombo.SelectedItem is ComboBoxItem dateItem)
            {
                string dateTag = dateItem.Tag?.ToString() ?? "All";
                if (dateTag != "All")
                {
                    filtered = filtered.Where(h =>
                    {
                        if (!DateTime.TryParse(h.Time, out var entryTime)) return true;
                        return dateTag switch
                        {
                            "Today" => entryTime.Date == DateTime.Today,
                            "Yesterday" => entryTime.Date == DateTime.Today.AddDays(-1),
                            "Week" => entryTime.Date >= DateTime.Today.AddDays(-7),
                            _ => true
                        };
                    });
                }
            }

            if (_historyStatusFilter == "Success") filtered = filtered.Where(TaskHistoryClassifier.IsSuccess);
            else if (_historyStatusFilter == "Failed") filtered = filtered.Where(TaskHistoryClassifier.IsFailure);

            InlineHistoryListView.ItemsSource = filtered.ToList();
        }

        private void UpdateHistoryStats()
        {
            StatTotalRuns.Text = _fullHistory.Count.ToString();
            StatSuccess.Text = _fullHistory.Count(TaskHistoryClassifier.IsSuccess).ToString();
            StatFailed.Text = _fullHistory.Count(TaskHistoryClassifier.IsFailure).ToString();
            StatLastResult.Text = _fullHistory.FirstOrDefault()?.Result ?? "-";
            HistoryStatsGrid.Visibility = Visibility.Visible;
        }

        private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) => UpdateHistoryList(); // Placeholder for actual date logic
        private async void ExportHistoryCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_fullHistory == null || _fullHistory.Count == 0) return;
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add("CSV File", new List<string>() { ".csv" });
            picker.SuggestedFileName = (ViewModel.SelectedTask?.Name ?? "history") + "_history";
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Time,EventId,Result,User,ExitCode,Message");
                    foreach (var h in _fullHistory)
                    {
                        sb.AppendLine($"\"{h.Time}\",{h.EventId},\"{h.Result}\",\"{h.User}\",{h.ExitCode},\"{h.Message?.Replace("\"", "\"\"") ?? ""}\"");
                    }
                    // UTF-8 *with* BOM so Excel and PowerShell don't mangle non-ASCII names.
                    System.IO.File.WriteAllText(file.Path, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void CopyHistory_Click(object sender, RoutedEventArgs e)
        {
             var dp = new DataPackage();
             dp.SetText(string.Join("\n", _fullHistory.Select(h => $"{h.Time}\t{h.Result}\t{h.Message}")));
             Clipboard.SetContent(dp);
             CopyHistoryBtn.Content = L("Main.History.Copied", "✅ Copied!");
             await Task.Delay(2000);
             CopyHistoryBtn.Content = L("Main.History.Copy", "📋 Copy");
         }
        
        private void StatTotal_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Total"; UpdateHistoryList(); }
        private void StatSuccess_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Success"; UpdateHistoryList(); }
        private void StatFailed_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Failed"; UpdateHistoryList(); }
        
        private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            RefreshHistoryBtn.IsEnabled = false;
            try { await RefreshTaskHistoryAsync(ViewModel.SelectedTask); }
            finally { RefreshHistoryBtn.IsEnabled = true; }
        }

        private async System.Threading.Tasks.Task RefreshTaskHistoryAsync(ScheduledTaskModel task)
        {
            if (task == null) return;
            var history = await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            
            // Only update if the user is still looking at the same task
            if (ViewModel.SelectedTask != null && ViewModel.SelectedTask.Path == task.Path)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _fullHistory = history;
                    UpdateHistoryList();
                    UpdateHistoryStats();
                });
            }
        }


        // ========================================================================================================
        // Task Operations (Single)
        // ========================================================================================================

        private void RunTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.RunTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = TaskState.Running;
                ViewModel.SelectedTask.IsRunning = true;
                WatchTaskUntilFinished(ViewModel.SelectedTask);
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask); // Refresh to show "Task Started"
            }
            catch (TaskSnoozedException ex)
            {
                // Snooze is a deliberate refusal, not an error the user needs a stack for.
                ViewModel.SelectedTask.State = ViewModel.SelectedTask.IsEnabled ? TaskState.Ready : TaskState.Disabled;
                ViewModel.SelectedTask.IsRunning = false;
                _ = ShowErrorDialog(ex.Message);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        private void StopTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.StopTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = TaskState.Ready;
                ViewModel.SelectedTask.IsRunning = false;
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        /// <summary>
        /// Polls Task Scheduler every 2 s until the task leaves the Running state,
        /// then writes the real state back to the model on the UI thread.
        /// </summary>
        // Shared by every WatchTaskUntilFinished caller: previously each started task got its own
        // 2-second poller that opened a brand-new TaskService and looked itself up individually, so
        // a batch run of N tasks spawned N parallel pollers each doing their own COM round-trip
        // every tick. One shared loop now polls every watched path in a single bulk call (3.5).
        private readonly Dictionary<string, ScheduledTaskModel> _watchedTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _watchStartTimesUtc = new(StringComparer.OrdinalIgnoreCase);
        private bool _taskWatcherRunning = false;
        private const int TaskWatcherPollIntervalMs = 2000;
        private static readonly TimeSpan MaxTaskWatchDuration = TimeSpan.FromMinutes(10);

        private void WatchTaskUntilFinished(ScheduledTaskModel task)
        {
            if (string.IsNullOrEmpty(task.Path)) return;

            bool needsStart;
            lock (_watchedTasks)
            {
                _watchedTasks[task.Path] = task;
                _watchStartTimesUtc[task.Path] = DateTime.UtcNow;
                needsStart = !_taskWatcherRunning;
                if (needsStart) _taskWatcherRunning = true;
            }

            if (needsStart) _ = RunTaskWatcherLoop();
        }

        private async System.Threading.Tasks.Task RunTaskWatcherLoop()
        {
            try
            {
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(TaskWatcherPollIntervalMs);

                    List<string> paths;
                    lock (_watchedTasks) { paths = _watchedTasks.Keys.ToList(); }
                    if (paths.Count == 0) break;

                    Dictionary<string, TaskState> statesByPath;
                    try
                    {
                        statesByPath = await System.Threading.Tasks.Task.Run(() =>
                            ViewModel.TaskService.GetAllTasks(recursive: true)
                                .Where(t => !string.IsNullOrEmpty(t.Path))
                                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().State, StringComparer.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        continue; // transient error — retry next tick rather than abandoning every watch
                    }

                    var now = DateTime.UtcNow;
                    var finishedPaths = new List<string>();
                    foreach (var path in paths)
                    {
                        if (!_watchedTasks.TryGetValue(path, out var task)) continue;

                        bool taskDeleted = !statesByPath.TryGetValue(path, out var liveState);
                        bool timedOut = now - _watchStartTimesUtc.GetValueOrDefault(path, now) > MaxTaskWatchDuration;
                        bool stillRunning = !taskDeleted && liveState == TaskState.Running;

                        if (!stillRunning || timedOut)
                        {
                            finishedPaths.Add(path);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (!taskDeleted) task.State = liveState;
                                task.IsRunning = false;
                                _ = RefreshTaskHistoryAsync(task); // Final refresh when finished
                            });
                        }
                        else
                        {
                            DispatcherQueue.TryEnqueue(() => task.State = liveState);
                        }
                    }

                    if (finishedPaths.Count > 0)
                    {
                        lock (_watchedTasks)
                        {
                            foreach (var p in finishedPaths) { _watchedTasks.Remove(p); _watchStartTimesUtc.Remove(p); }
                        }
                    }
                }
            }
            finally
            {
                lock (_watchedTasks) { _taskWatcherRunning = false; }
            }
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            // Hide the details dialog first to avoid "Only a single ContentDialog can be open" error
            try { TaskDetailsDialog.Hide(); } catch { }

            bool confirmed = !Settings.ConfirmDelete;
            if (!confirmed)
            {
                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"),
                    Content = string.Format(L("Dialog.DeleteTask.ContentFormat", "Are you sure you want to delete '{0}'?"), ViewModel.SelectedTask.Name),
                    PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            }

            if (confirmed)
            {
                try
                {
                    ViewModel.TaskService.DeleteTask(ViewModel.SelectedTask.Path);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void ExportTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            string? filePath = await Helpers.FilePickerHelper.PickSaveFileAsync(
                App.m_window!, "Export Task", "XML File", "xml", ViewModel.SelectedTask.Name);

            if (!string.IsNullOrEmpty(filePath))
            {
                try { ViewModel.TaskService.ExportTask(ViewModel.SelectedTask.Path, filePath); }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

        private async void ImportTask()
        {
            string? filePath = await Helpers.FilePickerHelper.PickOpenFileAsync(
                App.m_window!, "Import Task", "XML File", "xml");

            if (!string.IsNullOrEmpty(filePath))
            {
                var folderList = _treeNodeFolderMap.Values.Select(f => f.Path).Distinct().OrderBy(p => p).ToList();
                if (folderList.Count == 0) folderList.Add("\\");

                var comboBox = new ComboBox
                {
                    ItemsSource = folderList,
                    SelectedItem = _currentFolderPath ?? "\\",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = L("Dialog.ImportTask.SelectFolder", "Select the folder to import this task into:") });
                panel.Children.Add(comboBox);

                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ImportTask.Title", "Import Task"),
                    Content = panel,
                    PrimaryButtonText = L("Dialog.Common.Import", "Import"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                var folder = comboBox.SelectedItem?.ToString() ?? "\\";
                
                try
                {
                    string xml = System.IO.File.ReadAllText(filePath);
                    string taskName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    ViewModel.TaskService.RegisterTaskFromXml(folder, taskName, xml);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

        // ========================================================================================================
        // Task Editing (Dialog)
        // ========================================================================================================

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try { TaskDetailsDialog.Hide(); } catch { }

            if (ViewModel.SelectedTask.HasUnsupportedElements)
            {
                // Editing would silently drop or corrupt elements this app can't represent
                // (non-Exec actions, unrecognized trigger types) — refuse rather than risk it.
                await ShowErrorDialog(string.Format(
                    L("Dialog.UnsupportedElements",
                      "This task contains elements FluentTaskScheduler can't edit safely ({0}). Editing and saving it here would remove or corrupt those elements. Use Windows Task Scheduler to modify this task instead."),
                    ViewModel.SelectedTask.UnsupportedElementsDescription));
                return;
            }

            _isEditMode = true;
            _isPopulatingDetails = true;
            _isFromTemplate = false;
            _tempPipeline = ViewModel.SelectedTask.Pipeline?.Clone() ?? new TaskPipeline();
            UpdatePipelineSummaryLabel();

            // Populate Dialog
            EditTaskName.Text = ViewModel.SelectedTask.Name;
            EditTaskDescription.Text = ViewModel.SelectedTask.Description;
            EditTaskAuthor.Text = ViewModel.SelectedTask.Author;
            EditTaskCategory.Text = ViewModel.SelectedTask.Category;
            EditTaskTags.Text = ViewModel.SelectedTask.Tags != null ? string.Join(", ", ViewModel.SelectedTask.Tags) : "";
            EditTaskEnabled.IsOn = ViewModel.SelectedTask.IsEnabled;
            
            // Triggers
            _tempTriggers = new ObservableCollection<TaskTriggerModel>(ViewModel.SelectedTask.TriggersList);
            TriggerList.ItemsSource = _tempTriggers;
            
            // Actions
            _tempActions = new ObservableCollection<TaskActionModel>(ViewModel.SelectedTask.Actions);
            ActionList.ItemsSource = _tempActions;
            
            // Settings - simplified map back?
            // This is hard to "Refactor Cleanly" without binding everything.
            // For now, retaining basic load logic manually.
            EditTaskOnlyIfIdle.IsChecked = ViewModel.SelectedTask.OnlyIfIdle;
            EditTaskIdleDurationSetting.Text = ViewModel.SelectedTask.IdleDuration;
            EditTaskStopOnIdleEnd.IsChecked = ViewModel.SelectedTask.StopOnIdleEnd;
            EditTaskOnlyIfAC.IsChecked = ViewModel.SelectedTask.OnlyIfAC;
            EditTaskStopBatterySwitch.IsChecked = ViewModel.SelectedTask.StopOnBattery;
            EditTaskOnBattery.IsChecked = ViewModel.SelectedTask.DisallowStartOnBatteries;
            EditTaskOnlyIfNetwork.IsChecked = ViewModel.SelectedTask.OnlyIfNetwork;
            EditTaskWakeToRun.IsChecked = ViewModel.SelectedTask.WakeToRun;
            EditTaskIsHidden.IsChecked = ViewModel.SelectedTask.IsHidden;
            EditTaskRunWithHighestPrivileges.IsChecked = ViewModel.SelectedTask.RunWithHighestPrivileges;
            
            if (ViewModel.SelectedTask.RunAsSystem)
            {
                RunAsSystem.IsChecked = true;
            }
            else if (!string.IsNullOrEmpty(ViewModel.SelectedTask.RunAsUser))
            {
                RunAsSpecificUser.IsChecked = true;
                EditTaskRunAsUser.Text = ViewModel.SelectedTask.RunAsUser;
            }
            else
            {
                RunAsCurrentUser.IsChecked = true;
            }
            EditTaskRunIfMissed.IsChecked = ViewModel.SelectedTask.RunIfMissed;
            foreach (var item in EditTaskMultipleInstances.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.MultipleInstancesPolicy.ToString()) { EditTaskMultipleInstances.SelectedItem = item; break; }
            foreach (var item in EditTaskPriority.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.TaskPriority.ToString()) { EditTaskPriority.SelectedItem = item; break; }
            EditTaskDeleteExpired.IsChecked = ViewModel.SelectedTask.DeleteExpiredTaskAfter;
            EditTaskAllowHardTerminate.IsChecked = ViewModel.SelectedTask.AllowHardTerminate;
            EditTaskRestartOnFailure.IsChecked = ViewModel.SelectedTask.RestartOnFailure;
            EditTaskRestartInterval.Text = ViewModel.SelectedTask.RestartInterval;
            if (EditTaskRestartCount != null) EditTaskRestartCount.Value = ViewModel.SelectedTask.RestartCount;

            // Expiration
            DateTime? expirationDate = ViewModel.SelectedTask.TriggersList.FirstOrDefault()?.ExpirationDate;
            bool hasExpiration = expirationDate.HasValue;
            EditTaskExpires.IsChecked = hasExpiration;
            EditTaskExpirationDate.Date = hasExpiration ? expirationDate!.Value.Date : DateTime.Today;
            EditTaskExpirationTime.Time = hasExpiration ? expirationDate!.Value.TimeOfDay : DateTime.Now.TimeOfDay;
            EditTaskExpirationDate.IsEnabled = hasExpiration;
            EditTaskExpirationTime.IsEnabled = hasExpiration;

            // Stop task if runs longer than
            bool hasStopAfter = !string.IsNullOrWhiteSpace(ViewModel.SelectedTask.StopIfRunsLongerThan);
            EditTaskStopAfter.IsChecked = hasStopAfter;
            EditTaskStopAfterVal.IsEnabled = hasStopAfter;
            if (hasStopAfter)
            {
                bool matchedStopAfter = false;
                foreach (var item in EditTaskStopAfterVal.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                    if (item.Tag?.ToString() == ViewModel.SelectedTask.StopIfRunsLongerThan) { EditTaskStopAfterVal.SelectedItem = item; matchedStopAfter = true; break; }
                if (!matchedStopAfter) EditTaskStopAfterVal.Text = ViewModel.SelectedTask.StopIfRunsLongerThan;
            }
            // All settings mapped
            
            PopulateNetworkList();
            if (!string.IsNullOrEmpty(ViewModel.SelectedTask.NetworkId) &&
                Guid.TryParse(ViewModel.SelectedTask.NetworkId, out var taskNetworkGuid))
            {
                foreach (Microsoft.UI.Xaml.Controls.ComboBoxItem item in EditTaskNetworkSelection.Items)
                {
                    if (Guid.TryParse(item.Tag?.ToString(), out var itemGuid) && itemGuid == taskNetworkGuid)
                    {
                        EditTaskNetworkSelection.SelectedItem = item;
                        break;
                    }
                }
            }
            else
                EditTaskNetworkSelection.SelectedIndex = 0;
            
            _isPopulatingDetails = false;
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            EditTaskErrorBar.IsOpen = false;
            await TaskEditDialog.ShowAsync();
        }



        private async void TaskEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true; // Handle async manually

            if (string.IsNullOrWhiteSpace(EditTaskName.Text))
            {
                EditTaskErrorBar.Message = L("Dialog.Error.EmptyName", "Task name cannot be empty.");
                EditTaskErrorBar.IsOpen = true;
                return;
            }

            // "Stop task if runs longer than" is an editable combo box: a typed custom value has no
            // SelectedItem, so it must be read from .Text — falling back to the preset's Tag only
            // silently replaced any custom duration with 72h (see 1.4).
            string stopAfterValue = "";
            if (EditTaskStopAfter.IsChecked == true)
            {
                stopAfterValue = (EditTaskStopAfterVal.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString()
                    ?? EditTaskStopAfterVal.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(stopAfterValue) || !TryParseIsoDuration(stopAfterValue, out _))
                {
                    EditTaskErrorBar.Message = string.Format(
                        L("Dialog.Error.InvalidStopAfter", "\"{0}\" is not a valid duration for \"Stop task if runs longer than\". Use an ISO-8601 duration such as PT90M or P1D."),
                        stopAfterValue);
                    EditTaskErrorBar.IsOpen = true;
                    return;
                }
            }

            // Random delay is validated per-trigger here rather than silently discarded on a parse
            // failure (see 1.3).
            foreach (var trig in _tempTriggers)
            {
                if (!string.IsNullOrWhiteSpace(trig.RandomDelay) && !TryParseIsoDuration(trig.RandomDelay, out _))
                {
                    EditTaskErrorBar.Message = string.Format(
                        L("Dialog.Error.InvalidRandomDelay", "\"{0}\" is not a valid random delay value. Use an ISO-8601 duration such as PT30M or PT1H."),
                        trig.RandomDelay);
                    EditTaskErrorBar.IsOpen = true;
                    return;
                }
            }

            // IdleDuration/RestartInterval placeholders advertise a friendly shorthand ("10m", "1h")
            // but were previously validated with the strict ISO-8601-only parser and silently
            // discarded on failure (see 2.3).
            if (EditTaskOnlyIfIdle.IsChecked == true &&
                !DurationUtil.TryParseFlexibleDuration(EditTaskIdleDurationSetting.Text, out _))
            {
                EditTaskErrorBar.Message = string.Format(
                    L("Dialog.Error.InvalidIdleDuration", "\"{0}\" is not a valid idle duration. Use a value like 10m, 1h, or an ISO-8601 duration such as PT10M."),
                    EditTaskIdleDurationSetting.Text);
                EditTaskErrorBar.IsOpen = true;
                return;
            }
            if (EditTaskRestartOnFailure.IsChecked == true &&
                !string.IsNullOrWhiteSpace(EditTaskRestartInterval.Text) &&
                !DurationUtil.TryParseFlexibleDuration(EditTaskRestartInterval.Text, out _))
            {
                EditTaskErrorBar.Message = string.Format(
                    L("Dialog.Error.InvalidRestartInterval", "\"{0}\" is not a valid restart interval. Use a value like 1m, 30s, or an ISO-8601 duration such as PT1M."),
                    EditTaskRestartInterval.Text);
                EditTaskErrorBar.IsOpen = true;
                return;
            }

            var model = new ScheduledTaskModel
            {
                Name = EditTaskName.Text,
                Description = EditTaskDescription.Text,
                Author = EditTaskAuthor.Text,
                IsEnabled = EditTaskEnabled.IsOn,
                Category = EditTaskCategory.Text,
                Tags = new ObservableCollection<string>(EditTaskTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                Actions = new ObservableCollection<TaskActionModel>(_tempActions),
                TriggersList = new ObservableCollection<TaskTriggerModel>(_tempTriggers),
                // Map Settings
                OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked == true,
                IdleDuration = EditTaskIdleDurationSetting.Text ?? "",
                StopOnIdleEnd = EditTaskStopOnIdleEnd.IsChecked == true,
                OnlyIfAC = EditTaskOnlyIfAC.IsChecked == true,
                StopOnBattery = EditTaskStopBatterySwitch.IsChecked == true,
                DisallowStartOnBatteries = EditTaskOnBattery.IsChecked == true,
                OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked == true,
                NetworkId = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString() ?? "",
                NetworkName = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content?.ToString() ?? "",
                WakeToRun = EditTaskWakeToRun.IsChecked == true,
                IsHidden = EditTaskIsHidden.IsChecked == true,
                RunWithHighestPrivileges = EditTaskRunWithHighestPrivileges.IsChecked == true,
                RunAsSystem = RunAsSystem.IsChecked == true,
                RunAsUser = RunAsSpecificUser.IsChecked == true ? EditTaskRunAsUser.Text : "",
                RunIfMissed = EditTaskRunIfMissed.IsChecked == true,
                MultipleInstancesPolicy = Enum.TryParse<Microsoft.Win32.TaskScheduler.TaskInstancesPolicy>(
                    (EditTaskMultipleInstances.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString(), out var mip)
                    ? mip : Microsoft.Win32.TaskScheduler.TaskInstancesPolicy.IgnoreNew,
                TaskPriority = int.TryParse((EditTaskPriority.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString(), out int p) ? p : 7,
                DeleteExpiredTaskAfter = EditTaskDeleteExpired.IsChecked == true,
                AllowHardTerminate = EditTaskAllowHardTerminate.IsChecked == true,
                RestartOnFailure = EditTaskRestartOnFailure.IsChecked == true,
                RestartInterval = EditTaskRestartInterval.Text ?? "",
                RestartCount = EditTaskRestartCount != null ? (int)double.Round(EditTaskRestartCount.Value) : 3,
                StopIfRunsLongerThan = stopAfterValue
            };

            // Expiration is edited as a single value and applies to the first trigger.
            var expirationTarget = model.TriggersList.FirstOrDefault();
            if (expirationTarget == null)
            {
                expirationTarget = new TaskTriggerModel();
                model.TriggersList.Add(expirationTarget);
            }
            expirationTarget.ExpirationDate = EditTaskExpires.IsChecked == true
                ? EditTaskExpirationDate.Date.Date + EditTaskExpirationTime.Time
                : (DateTime?)null;

            model.Pipeline = _tempPipeline?.Clone() ?? new TaskPipeline();
            
            // Handle folder
            string folder = "\\";
            if (_isFromTemplate)
            {
                folder = "\\";
            }
            else if (!_isEditMode) // New Task
            {
                folder = _currentFolderPath;  // Use tracked folder path
            }
            else // Edit - keep original folder logic (extracted from Path)
            {
                if (ViewModel.SelectedTask != null)
                    folder = System.IO.Path.GetDirectoryName(ViewModel.SelectedTask.Path) ?? "\\";
            }

            try
            {
                ViewModel.TaskService.RegisterTask(folder ?? "\\", model);

                // Handle renaming: if name changed (case-sensitive check for the file system/TS behavior)
                // but Task Scheduler is case-insensitive, so we only delete if it's truly a different task
                if (_isEditMode && ViewModel.SelectedTask != null &&
                    !model.Name.Equals(ViewModel.SelectedTask.Name, StringComparison.OrdinalIgnoreCase))
                {
                    string oldPath = ViewModel.SelectedTask.Path;
                    string newPath = (folder ?? "\\").TrimEnd('\\') + "\\" + model.Name;
                    try
                    {
                        ViewModel.TaskService.DeleteTask(oldPath);
                        Serilog.Log.Information("{Message}", $"Renamed task - deleted old task at '{oldPath}'");
                    }
                    catch (Exception deleteEx)
                    {
                        // The new copy already exists at this point (RegisterTask above succeeded),
                        // so a failed delete of the old one would leave a duplicate. Undo the new
                        // copy so the rename fails cleanly instead of silently duplicating the task.
                        Serilog.Log.Error(deleteEx, "{Message}", $"Rename failed: could not delete old task '{oldPath}' after registering '{newPath}'. Rolling back the new copy.");
                        try { ViewModel.TaskService.DeleteTask(newPath); }
                        catch (Exception rollbackEx)
                        {
                            Serilog.Log.Error(rollbackEx, "{Message}", $"Rollback also failed: could not delete the new copy '{newPath}'. Both '{oldPath}' and '{newPath}' may now exist.");
                            EditTaskErrorBar.Message = string.Format(
                                L("Dialog.Error.RenameDuplicated", "Rename failed: both \"{0}\" and \"{1}\" now exist. Please delete one manually in Task Scheduler."),
                                System.IO.Path.GetFileName(oldPath), System.IO.Path.GetFileName(newPath));
                            EditTaskErrorBar.IsOpen = true;
                            return;
                        }
                        EditTaskErrorBar.Message = string.Format(
                            L("Dialog.Error.RenameFailed", "Could not rename \"{0}\" to \"{1}\": the old task could not be deleted ({2})."),
                            System.IO.Path.GetFileName(oldPath), model.Name, deleteEx.Message);
                        EditTaskErrorBar.IsOpen = true;
                        return;
                    }
                }

                // The pipeline watcher caches configuration; force it to re-read after a save.
                TaskPipelineService.InvalidatePipelineCache();

                TaskEditDialog.Hide();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex)
            {
                EditTaskErrorBar.Message = L("Dialog.Error.SaveTaskFailedPrefix", "Failed to save task: ") + ex.Message;
                EditTaskErrorBar.IsOpen = true;
            }
        }

        // ========================================================================================================
        // UI Logic (Dialogs)
        // ========================================================================================================

        private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                _isPopulatingDetails = true;
                // Map Trigger Model -> UI
                foreach(var item in EditTaskTriggerType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.TriggerType.ToString()) EditTaskTriggerType.SelectedItem = item;
                }
                
                var dt = TryParseScheduleInfo(tr.ScheduleInfo) ?? DateTime.MinValue;
                EditTaskStartDate.Date = dt == DateTime.MinValue ? DateTime.Today : dt;
                EditTaskStartTime.Time = dt == DateTime.MinValue ? DateTime.Now.TimeOfDay : dt.TimeOfDay;
                
                // Session State mapping
                foreach(var item in EditTaskSessionStateType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.SessionStateChangeType) EditTaskSessionStateType.SelectedItem = item;
                }

                // Repetition mapping
                foreach (var item in EditTaskRepetitionInterval.Items.Cast<ComboBoxItem>())
                    if (item.Tag?.ToString() == (tr.RepetitionInterval ?? "")) { EditTaskRepetitionInterval.SelectedItem = item; break; }
                foreach (var item in EditTaskRepetitionDuration.Items.Cast<ComboBoxItem>())
                    if (item.Tag?.ToString() == (tr.RepetitionDuration ?? "")) { EditTaskRepetitionDuration.SelectedItem = item; break; }

                // Daily recurrence mapping
                DailyInterval.Text = tr.DailyInterval.ToString();

                // Weekly recurrence mapping
                WeeklyInterval.Text = tr.WeeklyInterval.ToString();
                WeeklyMon.IsChecked = tr.WeeklyDays.Contains("Monday");
                WeeklyTue.IsChecked = tr.WeeklyDays.Contains("Tuesday");
                WeeklyWed.IsChecked = tr.WeeklyDays.Contains("Wednesday");
                WeeklyThu.IsChecked = tr.WeeklyDays.Contains("Thursday");
                WeeklyFri.IsChecked = tr.WeeklyDays.Contains("Friday");
                WeeklySat.IsChecked = tr.WeeklyDays.Contains("Saturday");
                WeeklySun.IsChecked = tr.WeeklyDays.Contains("Sunday");

                // Monthly recurrence mapping
                MonthlyDaysInput.Text = string.Join(", ", tr.MonthlyDays.Select(d => d == 32 ? "Last" : d.ToString()));
                MonthJan.IsChecked = tr.MonthlyMonths.Contains("January");
                MonthFeb.IsChecked = tr.MonthlyMonths.Contains("February");
                MonthMar.IsChecked = tr.MonthlyMonths.Contains("March");
                MonthApr.IsChecked = tr.MonthlyMonths.Contains("April");
                MonthMay.IsChecked = tr.MonthlyMonths.Contains("May");
                MonthJun.IsChecked = tr.MonthlyMonths.Contains("June");
                MonthJul.IsChecked = tr.MonthlyMonths.Contains("July");
                MonthAug.IsChecked = tr.MonthlyMonths.Contains("August");
                MonthSep.IsChecked = tr.MonthlyMonths.Contains("September");
                MonthOct.IsChecked = tr.MonthlyMonths.Contains("October");
                MonthNov.IsChecked = tr.MonthlyMonths.Contains("November");
                MonthDec.IsChecked = tr.MonthlyMonths.Contains("December");
                MonthlyRadioDays.IsChecked = !tr.MonthlyIsDayOfWeek;
                MonthlyRadioOn.IsChecked = tr.MonthlyIsDayOfWeek;
                MonthlyWeekCombo.SelectedIndex = tr.MonthlyWeek switch
                {
                    "First" => 0, "Second" => 1, "Third" => 2, "Fourth" => 3, "Last" => 4, _ => 0
                };
                MonthlyDayCombo.SelectedIndex = tr.MonthlyDayOfWeek switch
                {
                    "Monday" => 0, "Tuesday" => 1, "Wednesday" => 2, "Thursday" => 3, "Friday" => 4, "Saturday" => 5, "Sunday" => 6, _ => 0
                };

                // Random delay — independent of repetition, so populated unconditionally (see 1.3)
                bool hasRandomDelay = !string.IsNullOrWhiteSpace(tr.RandomDelay);
                EditTaskRandomDelay.IsChecked = hasRandomDelay;
                EditTaskRandomDelayVal.Text = tr.RandomDelay;
                EditTaskRandomDelayVal.IsEnabled = hasRandomDelay;

                // Idle trigger
                EditTaskIdleDuration.Text = tr.IdleDuration;

                // Event trigger
                EditTaskEventLog.Text = tr.EventLog;
                EditTaskEventSource.Text = tr.EventSource;
                EditTaskEventId.Text = tr.EventId?.ToString() ?? "";

                UpdateTriggerPanelVisibility();
                _isPopulatingDetails = false;
            }
        }

        private void EditTaskStartDate_SelectedDateChanged(object sender, DatePickerSelectedValueChangedEventArgs e) => UpdateTriggerScheduleInfo();
        private void EditTaskStartTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e) => UpdateTriggerScheduleInfo();

        private void UpdateTriggerScheduleInfo()
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                var combined = EditTaskStartDate.Date.Date + EditTaskStartTime.Time;
                tr.ScheduleInfo = FormatScheduleInfo(combined);
            }
        }

        private static string FormatScheduleInfo(DateTime value) => DurationUtil.FormatScheduleInfo(value);
        private static DateTime? TryParseScheduleInfo(string? value) => DurationUtil.TryParseScheduleInfo(value);

        private void DailyInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && short.TryParse(DailyInterval.Text, out short interval) && interval > 0)
                tr.DailyInterval = interval;
        }

        private void WeeklyInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && short.TryParse(WeeklyInterval.Text, out short interval) && interval > 0)
                tr.WeeklyInterval = interval;
        }

        private void WeeklyDay_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                var days = new List<string>();
                if (WeeklyMon.IsChecked == true) days.Add("Monday");
                if (WeeklyTue.IsChecked == true) days.Add("Tuesday");
                if (WeeklyWed.IsChecked == true) days.Add("Wednesday");
                if (WeeklyThu.IsChecked == true) days.Add("Thursday");
                if (WeeklyFri.IsChecked == true) days.Add("Friday");
                if (WeeklySat.IsChecked == true) days.Add("Saturday");
                if (WeeklySun.IsChecked == true) days.Add("Sunday");
                tr.WeeklyDays = days;
            }
        }

        private void MonthlyDaysInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                var days = new List<int>();
                foreach (var part in MonthlyDaysInput.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Equals("Last", StringComparison.OrdinalIgnoreCase)) days.Add(32);
                    else if (int.TryParse(part, out int d) && d >= 1 && d <= 31) days.Add(d);
                }
                tr.MonthlyDays = days;
            }
        }

        private void MonthlyMonth_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                var months = new List<string>();
                if (MonthJan.IsChecked == true) months.Add("January");
                if (MonthFeb.IsChecked == true) months.Add("February");
                if (MonthMar.IsChecked == true) months.Add("March");
                if (MonthApr.IsChecked == true) months.Add("April");
                if (MonthMay.IsChecked == true) months.Add("May");
                if (MonthJun.IsChecked == true) months.Add("June");
                if (MonthJul.IsChecked == true) months.Add("July");
                if (MonthAug.IsChecked == true) months.Add("August");
                if (MonthSep.IsChecked == true) months.Add("September");
                if (MonthOct.IsChecked == true) months.Add("October");
                if (MonthNov.IsChecked == true) months.Add("November");
                if (MonthDec.IsChecked == true) months.Add("December");
                tr.MonthlyMonths = months;
            }
        }

        private void MonthlyMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.MonthlyIsDayOfWeek = MonthlyRadioOn.IsChecked == true;
        }

        private void MonthlyWeekOrDay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                string[] weeks = { "First", "Second", "Third", "Fourth", "Last" };
                string[] days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
                if (MonthlyWeekCombo.SelectedIndex >= 0 && MonthlyWeekCombo.SelectedIndex < weeks.Length)
                    tr.MonthlyWeek = weeks[MonthlyWeekCombo.SelectedIndex];
                if (MonthlyDayCombo.SelectedIndex >= 0 && MonthlyDayCombo.SelectedIndex < days.Length)
                    tr.MonthlyDayOfWeek = days[MonthlyDayCombo.SelectedIndex];
            }
        }

        private void EditTaskIdleDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.IdleDuration = EditTaskIdleDuration.Text ?? "";
        }

        private void EditTaskEventTrigger_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                tr.EventLog = EditTaskEventLog.Text ?? "";
                tr.EventSource = EditTaskEventSource.Text ?? "";
                tr.EventId = int.TryParse(EditTaskEventId.Text, out int id) ? id : (int?)null;
            }
        }

        private void EditTaskSessionStateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskSessionStateType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.SessionStateChangeType = item.Tag.ToString()!;
            }
        }

        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            UpdateTriggerPanelVisibility();
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskTriggerType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null && Enum.TryParse<TriggerType>(item.Tag.ToString(), out var newTriggerType))
                    tr.TriggerType = newTriggerType;
            }
        }

        private void UpdateTriggerPanelVisibility()
        {
            if (TriggerDetailsPanel == null || PanelDaily == null || PanelWeekly == null || 
                PanelMonthly == null || PanelEvent == null || PanelIdle == null || 
                PanelSessionState == null || PanelStartTime == null || EditTaskTriggerType == null) return;

             TriggerDetailsPanel.Visibility = Visibility.Visible;
             PanelDaily.Visibility = Visibility.Collapsed;
             PanelWeekly.Visibility = Visibility.Collapsed;
             PanelMonthly.Visibility = Visibility.Collapsed;
             PanelEvent.Visibility = Visibility.Collapsed;
             PanelIdle.Visibility = Visibility.Collapsed;
             PanelSessionState.Visibility = Visibility.Collapsed;
             PanelStartTime.Visibility = Visibility.Visible;

             if (EditTaskTriggerType.SelectedItem is ComboBoxItem item &&
                 Enum.TryParse<TriggerType>(item.Tag?.ToString(), out var type))
             {
                 switch (type)
                 {
                     case TriggerType.Daily: PanelDaily.Visibility = Visibility.Visible; break;
                     case TriggerType.Weekly: PanelWeekly.Visibility = Visibility.Visible; break;
                     case TriggerType.Monthly: PanelMonthly.Visibility = Visibility.Visible; break;
                     case TriggerType.Event: PanelEvent.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case TriggerType.OnIdle: PanelIdle.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case TriggerType.SessionStateChange: PanelSessionState.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                 }
             }
        }

        private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionList.SelectedItem is TaskActionModel act)
            {
                ActionDetailsPanel.Visibility = Visibility.Visible;
                EditTaskActionCommand.Text = act.Command ?? "";
                EditTaskArguments.Text = act.Arguments ?? "";
                EditTaskWorkingDirectory.Text = act.WorkingDirectory ?? "";
            }
            else
            {
                ActionDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
        
        private void EditTaskActionCommand_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Command = EditTaskActionCommand.Text; }
        private void EditTaskArguments_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Arguments = EditTaskArguments.Text; }
        private void EditTaskWorkingDirectory_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.WorkingDirectory = EditTaskWorkingDirectory.Text; }

        private async void BrowseAction_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = await Helpers.FilePickerHelper.PickOpenFileAsync(
                App.m_window!, "Select File", "All Files", "*");

            if (!string.IsNullOrEmpty(filePath)) EditTaskActionCommand.Text = filePath;
        }

        private void PopulateNetworkList()
        {
            bool isAdmin = Helpers.ElevationHelper.IsElevated();

            if (!isAdmin)
            {
                // Non-admin: disable dropdown and show explanation notice
                EditTaskNetworkSelection.IsEnabled = false;
                EditTaskNetworkSelection.Items.Clear();
                EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });
                EditTaskNetworkSelection.SelectedIndex = 0;
                NetworkAdminNotice.IsOpen = true;
                return;
            }

            // Admin: populate from registry (exact NLM profile GUIDs)
            NetworkAdminNotice.IsOpen = false;
            EditTaskNetworkSelection.IsEnabled = true;
            EditTaskNetworkSelection.Items.Clear();
            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });

            try
            {
                using var profilesKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
                if (profilesKey != null)
                {
                    foreach (var subKeyName in profilesKey.GetSubKeyNames())
                    {
                        using var profileKey = profilesKey.OpenSubKey(subKeyName);
                        var name = profileKey?.GetValue("ProfileName") as string;
                        if (!string.IsNullOrWhiteSpace(name))
                            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = name, Tag = subKeyName });
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Warning("{Message}", $"Could not populate network list: {ex.Message}"); }
        }

        // List Buttons
        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e) { _tempTriggers.Add(new TaskTriggerModel { TriggerType=TriggerType.Daily, ScheduleInfo=FormatScheduleInfo(DateTime.Now) }); TriggerList.SelectedIndex = _tempTriggers.Count - 1; }
        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e) { if (TriggerList.SelectedItem is TaskTriggerModel t) _tempTriggers.Remove(t); }
        private void BtnMoveTriggerUp_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = TriggerList.SelectedIndex;
            if (idx > 0) {
                var item = _tempTriggers[idx];
                _tempTriggers.RemoveAt(idx);
                _tempTriggers.Insert(idx - 1, item);
                TriggerList.SelectedIndex = idx - 1;
            }
        }
        private void BtnMoveTriggerDown_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = TriggerList.SelectedIndex;
            if (idx >= 0 && idx < _tempTriggers.Count - 1) {
                var item = _tempTriggers[idx];
                _tempTriggers.RemoveAt(idx);
                _tempTriggers.Insert(idx + 1, item);
                TriggerList.SelectedIndex = idx + 1;
            }
        }

        private void BtnAddAction_Click(object sender, RoutedEventArgs e) { _tempActions.Add(new TaskActionModel { Command="notepad.exe" }); ActionList.SelectedIndex = _tempActions.Count - 1; }
        
        private void AddAction_SendEmail_Click(object sender, RoutedEventArgs e) 
        {
            string ps = "powershell.exe";
            string args = "-ExecutionPolicy Bypass -Command \"Send-MailMessage -To 'recipient@example.com' -From 'scheduler@example.com' -Subject 'Task Started' -Body 'The task has started.' -SmtpServer 'smtp.example.com'\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void AddAction_ShowNotification_Click(object sender, RoutedEventArgs e)
        {
            string ps = "powershell.exe";
            string args = "-WindowStyle Hidden -Command \"& {Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Task Notification', 'FluentTaskScheduler')}\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel t) _tempActions.Remove(t); }
        private void BtnMoveActionUp_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx > 0) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx - 1, item);
                ActionList.SelectedIndex = idx - 1;
            }
        }
        private void BtnMoveActionDown_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx >= 0 && idx < _tempActions.Count - 1) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx + 1, item);
                ActionList.SelectedIndex = idx + 1;
            }
        }

        // Handlers to satisfy XAML connection
        private void EditTaskExpires_Click(object sender, RoutedEventArgs e) 
        { 
            bool enabled = EditTaskExpires.IsChecked == true;
            EditTaskExpirationDate.IsEnabled = enabled;
            EditTaskExpirationTime.IsEnabled = enabled;
        }
        private void EditTaskRandomDelay_Click(object sender, RoutedEventArgs e)
        {
            if (EditTaskRandomDelayVal == null) return;
            bool on = EditTaskRandomDelay.IsChecked == true;
            EditTaskRandomDelayVal.IsEnabled = on;
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.RandomDelay = on ? EditTaskRandomDelayVal.Text ?? "" : "";
        }

        private void EditTaskRandomDelayVal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (EditTaskRandomDelay.IsChecked != true) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.RandomDelay = EditTaskRandomDelayVal.Text ?? "";
        }
        private void EditTaskRepetitionInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskRepetitionInterval.SelectedItem is ComboBoxItem item)
                tr.RepetitionInterval = item.Tag?.ToString() ?? "";
        }
        private void EditTaskRepetitionDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskRepetitionDuration.SelectedItem is ComboBoxItem item)
                tr.RepetitionDuration = item.Tag?.ToString() ?? "";
        }
        private void EditTaskStopAfter_Click(object sender, RoutedEventArgs e) { if (EditTaskStopAfterVal != null) EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked == true; }
        private void EditTaskDailyRecurrence_Checked(object sender, RoutedEventArgs e) { if (DailyInterval != null) DailyInterval.IsEnabled = EditTaskDailyRecurrence.IsChecked == true; }
        private void UserContextRadio_Checked(object sender, RoutedEventArgs e) 
        { 
             if (EditTaskRunAsUser != null) EditTaskRunAsUser.IsEnabled = RunAsSpecificUser.IsChecked == true; 
             if (SystemUserWarning != null)
             {
                 bool isElevated = Helpers.ElevationHelper.IsElevated();
                 SystemUserWarning.IsOpen = (!isElevated) && (RunAsSystem.IsChecked == true);
             }
        }
        private void RunAsSystem_Click(object sender, RoutedEventArgs e) => RunAsSystem.IsChecked = true;
        private void DialogScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // When the user clicks on empty space (no interactive element), WinUI shifts focus to
            // the ScrollViewer and calls BringIntoView on it, which resets the scroll position to
            // the top. We prevent this by capturing the current vertical offset and restoring it
            // on the next dispatcher frame (after BringIntoView has already fired).
            if (sender is not ScrollViewer sv) return;
            double savedOffset = sv.VerticalOffset;
            DispatcherQueue.TryEnqueue(() => sv.ChangeView(null, savedOffset, null, true));
        }

        private void InfoIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true; // Don't bubble to ScrollViewer
            if (sender is not FrameworkElement icon) return;

            var text = ToolTipService.GetToolTip(icon) as string;
            if (string.IsNullOrEmpty(text)) return;

            // Un-escape XML character references that appear literally in the string
            text = text.Replace("&#x0a;", "\n").Replace("&#x2022;", "\u2022");

            var content = new TextBlock
            {
                Text = text,
                MaxWidth = 300,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            };

            var flyout = new Flyout
            {
                Content = content,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
            };

            // Save the current scroll offset now. When the flyout light-dismisses, WinUI
            // processes the outside tap as a focus change on the ScrollViewer and calls
            // BringIntoView, jumping the scroll to the top. Restoring the saved offset on
            // the next dispatcher frame (after BringIntoView has already fired) undoes that.
            double savedOffset = EditScrollViewer.VerticalOffset;
            flyout.Closed += (_, _) =>
                DispatcherQueue.TryEnqueue(() => EditScrollViewer.ChangeView(null, savedOffset, null, true));

            flyout.ShowAt(icon);
        }

        // Batch
        private void UpdateBatchActionsState()
        {
            if (TaskListView.SelectedItems.Count <= 1) return;
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>();
            bool anyDisabled = tasks.Any(t => !t.IsEnabled);
            if (BatchRunBtn != null) BatchRunBtn.IsEnabled = !anyDisabled;
            if (BatchStopBtn != null) BatchStopBtn.IsEnabled = !anyDisabled;
        }
        private void BatchCancel_Click(object sender, RoutedEventArgs e) => TaskListView.SelectedItems.Clear();
        private async void BatchRun_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot selection before anything changes.
            // Set IsRunning=true BEFORE calling RunTask so the ring appears immediately,
            // independently of the volatile State string.
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

            if (SnoozeService.IsActive)
            {
                foreach (var t in tasks) SnoozeService.RecordSuppressedRun(t.Path, "Manual");
                await ShowErrorDialog(string.Format(
                    L("Snooze.Error.BatchBlocked", "{0} task(s) were not started because Global Snooze is active."),
                    tasks.Count));
                return;
            }

            foreach (var t in tasks)
            {
                t.State = TaskState.Running;
                t.IsRunning = true;           // show the ring immediately
                try
                {
                    ViewModel.TaskService.RunTask(t.Path);
                }
                catch (Exception ex)
                {
                    // The watcher below corrects IsRunning; log so a silent failure is traceable.
                    Serilog.Log.Error(ex, "{Message}", $"Batch run could not start task '{t.Path}'.");
                }
                WatchTaskUntilFinished(t);
            }
        }
        private void BatchStop_Click(object sender, RoutedEventArgs e) => PerformBatchAction(t => { ViewModel.TaskService.StopTask(t.Path); t.State = TaskState.Ready; t.IsRunning = false; });
        private async void BatchEnable_Click(object sender, RoutedEventArgs e) { var denied = PerformBatchActionWithErrors(t => { if (!t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, true); t.IsEnabled = true; } }); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to enable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDisable_Click(object sender, RoutedEventArgs e) { var denied = PerformBatchActionWithErrors(t => { if (t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, false); t.IsEnabled = false; } }); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to disable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDelete_Click(object sender, RoutedEventArgs e)
        {
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

            bool confirmed = !Settings.ConfirmDelete;
            if (!confirmed)
            {
                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"),
                    Content = string.Format(L("Dialog.BatchDelete.ContentFormat", "Delete {0} tasks?"), tasks.Count),
                    PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            }

            if (confirmed)
            {
                foreach (var t in tasks) try { ViewModel.TaskService.DeleteTask(t.Path); } catch { }
                _ = ViewModel.LoadTasksAsync();
            }
        }
        private void PerformBatchAction(System.Action<ScheduledTaskModel> action) { foreach (var task in TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList()) try { action(task); } catch { } }
        private List<string> PerformBatchActionWithErrors(System.Action<ScheduledTaskModel> action) { var denied = new List<string>(); foreach (var task in TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList()) try { action(task); } catch (UnauthorizedAccessException) { denied.Add(task.Name); } catch { } return denied; }

        // Keyboard Accelerators
        protected override void OnKeyDown(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F5) { e.Handled = true; _ = ViewModel.LoadTasksAsync(); return; }
            base.OnKeyDown(e);
        }
        private void NewTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; NewTaskButton_Click(sender, new RoutedEventArgs()); }
        private void EditTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) EditTask_Click(sender, new RoutedEventArgs()); }
        private void RunTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) RunTask_Click(sender, new RoutedEventArgs()); }
        private void DeleteTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (FocusManager.GetFocusedElement() is not TextBox) DeleteTask_Click(sender, new RoutedEventArgs()); }
        private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; try { TaskDetailsDialog.Hide(); } catch { } try { TaskEditDialog.Hide(); } catch { } }
        private void ShortcutsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; ShowShortcutsDialog(); }

        // Feature 1: Keyboard shortcuts dialog
        private void ShortcutsButton_Click(object sender, RoutedEventArgs e) => ShowShortcutsDialog();

        private async void ShowShortcutsDialog()
        {
            ShortcutsDialog.XamlRoot = this.XamlRoot;
            try { await ShortcutsDialog.ShowAsync(); } catch { }
        }

        // Feature 2: Sort button with flyout
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            string arrow(SortColumn col) =>
                ViewModel.SortColumn == col ? (ViewModel.SortAscending ? " ▲" : " ▼") : "";

            void AddItem(string label, SortColumn col)
            {
                var item = new MenuFlyoutItem { Text = label + arrow(col) };
                item.Click += (s, _) => { ViewModel.SortBy(col); UpdateSortButtonText(); };
                flyout.Items.Add(item);
            }

            AddItem(L("Main.Sort.Name", "Name"), SortColumn.Name);
            AddItem(L("Main.Sort.Status", "Status"), SortColumn.Status);
            AddItem(L("Main.Sort.NextRun", "Next Run"), SortColumn.NextRun);
            AddItem(L("Main.Sort.LastRun", "Last Run"), SortColumn.LastRun);
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = L("Main.Sort.Clear", "Clear Sort") };
            clear.Click += (s, _) => { ViewModel.ClearSort(); UpdateSortButtonText(); };
            flyout.Items.Add(clear);

            flyout.ShowAt(SortButton);
        }

        private void UpdateSortButtonText()
        {
            string arrow = ViewModel.SortAscending ? "▲" : "▼";
            SortButton.Content = ViewModel.SortColumn == SortColumn.None
                ? L("Main.Toolbar.SortButton", "Sort ↕")
                : string.Format(L("Main.Toolbar.SortActiveFormat", "Sort {0} {1}"), arrow, ViewModel.SortColumn);
        }

        private async void ReloadFolders_Click(object sender, RoutedEventArgs e)
        {
            FolderRefreshIcon.Visibility = Visibility.Collapsed;
            FolderRefreshRing.Visibility = Visibility.Visible;
            FolderRefreshRing.IsActive = true;

            await Task.Run(() => 
            {
                DispatcherQueue.TryEnqueue(() => LoadFolderStructure());
            });

            await Task.Delay(300); // Give a little visual feedback

            FolderRefreshRing.IsActive = false;
            FolderRefreshRing.Visibility = Visibility.Collapsed;
            FolderRefreshIcon.Visibility = Visibility.Visible;
        }

        private void CreateRootFolder_Click(object sender, RoutedEventArgs e)
        {
            CreateFolder_Click("\\");
        }

        private void FolderTreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var fe = e.OriginalSource as FrameworkElement;
            if (fe == null) return;
            
            var tvi = FindParent<TreeViewItem>(fe);
            if (tvi != null)
            {
                var node = FolderTreeView.NodeFromContainer(tvi);
                if (node != null && _treeNodeFolderMap.TryGetValue(node, out var folder))
                {
                    ShowFolderContextMenu(fe, e.GetPosition(fe), folder);
                }
            }
        }

        private void ShowFolderContextMenu(FrameworkElement targetElement, Windows.Foundation.Point position, TaskFolderModel folder)
        {
            var flyout = new MenuFlyout();

            var newFolderItem = new MenuFlyoutItem { Text = L("FolderMenu.NewSubfolder", "New Subfolder"), Icon = new SymbolIcon(Symbol.Add) };
            newFolderItem.Click += (s, args) => CreateFolder_Click(folder.Path);
            flyout.Items.Add(newFolderItem);

            if (folder.Path != "\\")
            {
                var renameItem = new MenuFlyoutItem { Text = L("FolderMenu.Rename", "Rename"), Icon = new SymbolIcon(Symbol.Rename) };
                renameItem.Click += (s, args) => RenameFolder_Click(folder.Path, folder.Name);
                flyout.Items.Add(renameItem);

                var deleteItem = new MenuFlyoutItem { Text = L("FolderMenu.Delete", "Delete"), Icon = new SymbolIcon(Symbol.Delete) };
                deleteItem.Click += (s, args) => DeleteFolder_Click(folder.Path);
                flyout.Items.Add(deleteItem);
            }

            flyout.ShowAt(targetElement, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
        }

        private async void CreateFolder_Click(string parentPath)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.NewFolder.Title", "New Folder"),
                Content = new TextBox { PlaceholderText = L("Dialog.NewFolder.NamePlaceholder", "Name") },
                PrimaryButtonText = L("Dialog.Common.Create", "Create"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Content is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) 
            { 
                try 
                { 
                    ViewModel.TaskService.CreateFolder(parentPath == "\\" ? "\\" + tb.Text : parentPath + "\\" + tb.Text); 
                    LoadFolderStructure(); 
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }

        private async void RenameFolder_Click(string path, string oldName)
        {
            var tb = new TextBox { Text = oldName, PlaceholderText = L("Dialog.RenameFolder.NewNamePlaceholder", "New Name") };
            tb.SelectAll();
            
            var dialog = new ContentDialog
            {
                Title = L("Dialog.RenameFolder.Title", "Rename Folder"),
                Content = tb,
                PrimaryButtonText = L("Dialog.Common.Rename", "Rename"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };
            
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text != oldName) 
            { 
                try 
                { 
                    ViewModel.TaskService.RenameFolder(path, tb.Text); 
                    LoadFolderStructure(); 
                    
                    if (_currentFolderPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentFolderPath = "\\";
                        ViewModel.SetFilter("all");
                        NavView.SelectedItem = NavAllTasks;
                    }
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }

        private async void DeleteFolder_Click(string path)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.DeleteFolder.Title", "Delete Folder"),
                Content = string.Format(L("Dialog.DeleteFolder.ContentFormat", "Delete '{0}' and ALL tasks in it?"), path),
                PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) 
            { 
                try 
                { 
                    ViewModel.TaskService.DeleteFolder(path); 
                    LoadFolderStructure(); 
                    ViewModel.SetFilter("all"); 
                    NavView.SelectedItem = NavAllTasks; 
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }


        private bool _isDialogOpen = false;

        // ========================================================================================================
        // Drag and Drop
        // ========================================================================================================

        private const string DragTaskPrefix  = "FTS_TASKS:";
        private const string DragFolderPrefix = "FTS_FOLDER:";
        private Grid? _dragHighlightedGrid;

        // --- Task dragging from TaskListView ---

        private void TaskListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                var paths = e.Items
                    .OfType<ScheduledTaskModel>()
                    .Where(t => !t.IsReadOnlyFallback)
                    .Select(t => t.Path)
                    .ToList();

                if (paths.Count == 0) { e.Cancel = true; return; }

                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragTaskPrefix + string.Join("\n", paths));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Task DragItemsStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        // --- Per-folder-item DataTemplate Grid events ---

        private TaskFolderModel? FindFolderFromItemGrid(DependencyObject element)
        {
            var tvi = FindParent<TreeViewItem>(element);
            if (tvi == null) return null;
            var node = FolderTreeView.NodeFromContainer(tvi);
            if (node == null) return null;
            return _treeNodeFolderMap.TryGetValue(node, out var f) ? f : null;
        }

        private void SetDragHighlight(Grid? grid, bool on)
        {
            if (grid == null) return;
            grid.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 103, 192))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        private void FolderItem_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                if (sender is not FrameworkElement fe) return;
                var folder = FindFolderFromItemGrid(fe);
                if (folder == null || folder.Path == "\\") { e.Cancel = true; return; }
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragFolderPrefix + folder.Path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Folder DragStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        private void FolderItem_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            if (folder == null) { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; return; }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move to \"{folder.Name}\"";
            e.DragUIOverride.IsGlyphVisible = true;

            if (_dragHighlightedGrid != grid)
            {
                SetDragHighlight(_dragHighlightedGrid, false);
                _dragHighlightedGrid = grid;
                SetDragHighlight(grid, true);
            }
        }

        private void FolderItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && grid == _dragHighlightedGrid)
            {
                SetDragHighlight(grid, false);
                _dragHighlightedGrid = null;
            }
        }

        private async void FolderItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            SetDragHighlight(grid, false);
            _dragHighlightedGrid = null;
            if (folder == null) return;
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;

            string payload;
            try { payload = await e.DataView.GetTextAsync(); }
            catch { return; }

            if (payload.StartsWith(DragTaskPrefix))
                await MoveDraggedTasksAsync(payload.Substring(DragTaskPrefix.Length), folder.Path);
            else if (payload.StartsWith(DragFolderPrefix))
                await MoveDraggedFolderAsync(payload.Substring(DragFolderPrefix.Length), folder.Path);
        }

        // --- TreeView-level fallback handlers ---

        private void FolderTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }

        private void FolderTreeView_DragLeave(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Custom Elevated Drag-and-Drop Implementation ---
        private bool _isCustomDragging = false;
        private Windows.Foundation.Point _customDragStartPos;
        private object? _customDragItem; // string (folder path) or List<string> (task paths)
        private Grid? _customDragHoveredFolderGrid;

        private void OnCustomDragPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!Helpers.ElevationHelper.IsElevated()) return;
            var pt = e.GetCurrentPoint(this).Position;
            var element = e.OriginalSource as DependencyObject;
            if (element == null) return;

            // Check if dragging a Task
            var taskListViewItem = FindParent<ListViewItem>(element);
            if (taskListViewItem != null && FindParent<ListView>(taskListViewItem) == TaskListView)
            {
                // Ignore clicks on ToggleSwitch or CheckBox or Button
                if (element is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton || 
                    FindParent<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(element) != null ||
                    element is Button || FindParent<Button>(element) != null) return;

                var model = taskListViewItem.Content as ScheduledTaskModel;
                if (model == null || model.IsReadOnlyFallback) return;

                bool isSelected = false;
                foreach (ScheduledTaskModel sel in TaskListView.SelectedItems) {
                    if (sel == model) { isSelected = true; break; }
                }

                _customDragItem = isSelected && TaskListView.SelectedItems.Count > 0 
                    ? TaskListView.SelectedItems.OfType<ScheduledTaskModel>().Where(t => !t.IsReadOnlyFallback).Select(t => t.Path).ToList()
                    : new List<string> { model.Path };
                
                _customDragStartPos = pt;
                return;
            }

            // Check if dragging a Folder
            var treeViewItem = FindParent<TreeViewItem>(element);
            if (treeViewItem != null && FindParent<TreeView>(treeViewItem) == FolderTreeView)
            {
                // Ignore button clicks
                if (element is Button || FindParent<Button>(element) != null) return;

                var folder = FindFolderFromItemGrid(element);
                if (folder != null && folder.Path != "\\")
                {
                    _customDragItem = folder.Path;
                    _customDragStartPos = pt;
                }
            }
        }

        private void OnCustomDragPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;
            
            var pt = e.GetCurrentPoint(this).Position;
            if (!_isCustomDragging)
            {
                double dx = pt.X - _customDragStartPos.X;
                double dy = pt.Y - _customDragStartPos.Y;
                if (dx * dx + dy * dy > 25) // 5 pixel threshold
                {
                    _isCustomDragging = true;
                    this.CapturePointer(e.Pointer);
                    CustomDragCanvas.Visibility = Visibility.Visible;
                    
                    if (_customDragItem is List<string> tasks)
                    {
                        CustomDragIcon.Glyph = "\uE8F1"; // Task icon
                        CustomDragText.Text = tasks.Count > 1 ? $"Move {tasks.Count} tasks" : "Move task";
                    }
                    else if (_customDragItem is string folderPath)
                    {
                        CustomDragIcon.Glyph = "\uE8B7"; // Folder icon
                        var folderName = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? folderPath;
                        CustomDragText.Text = $"Move {folderName}";
                    }
                }
            }

            if (_isCustomDragging)
            {
                Canvas.SetLeft(CustomDragVisual, pt.X + 15);
                Canvas.SetTop(CustomDragVisual, pt.Y + 15);

                // Hit testing for drop target (FolderTreeView item)
                var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(e.GetCurrentPoint(null).Position, FolderTreeView);
                Grid? targetGrid = null;
                foreach (var el in elements)
                {
                    if (el is Grid g && FindFolderFromItemGrid(g) != null)
                    {
                        targetGrid = g;
                        break;
                    }
                }

                if (_customDragHoveredFolderGrid != targetGrid)
                {
                    SetDragHighlight(_customDragHoveredFolderGrid, false);
                    _customDragHoveredFolderGrid = targetGrid;
                    SetDragHighlight(_customDragHoveredFolderGrid, true);
                }
            }
        }

        private async void OnCustomDragPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;

            if (_isCustomDragging)
            {
                this.ReleasePointerCapture(e.Pointer);
                CustomDragCanvas.Visibility = Visibility.Collapsed;
                _isCustomDragging = false;
                SetDragHighlight(_customDragHoveredFolderGrid, false);

                if (_customDragHoveredFolderGrid != null)
                {
                    var targetFolder = FindFolderFromItemGrid(_customDragHoveredFolderGrid);
                    if (targetFolder != null)
                    {
                        if (_customDragItem is List<string> tasks)
                        {
                            await MoveDraggedTasksAsync(string.Join("\n", tasks), targetFolder.Path);
                        }
                        else if (_customDragItem is string folderPath)
                        {
                            await MoveDraggedFolderAsync(folderPath, targetFolder.Path);
                        }
                    }
                }
                _customDragHoveredFolderGrid = null;
            }
            _customDragItem = null;
        }

        private void FolderTreeView_Drop(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Move helpers ---

        private async System.Threading.Tasks.Task MoveDraggedTasksAsync(string rawPaths, string targetFolderPath)
        {
            var paths = rawPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            foreach (var path in paths)
            {
                try { await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveTask(path, targetFolderPath)); }
                catch (Exception ex) { errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
            }

            await ViewModel.LoadTasksAsync();
            LoadFolderStructure();

            if (errors.Count > 0)
                await ShowErrorDialog("Some tasks could not be moved:\n\n" + string.Join("\n", errors));
        }

        private async System.Threading.Tasks.Task MoveDraggedFolderAsync(string sourceFolderPath, string targetFolderPath)
        {
            try
            {
                // Ensure target folder is expanded so user sees the change
                _folderExpandedState[targetFolderPath] = true;

                await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveFolder(sourceFolderPath, targetFolderPath));
                LoadFolderStructure();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex) { await ShowErrorDialog($"Could not move folder: {ex.Message}"); }
        }


        private async Task ShowErrorDialog(string message)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try
            {
                await Helpers.DialogHelper.ShowErrorAsync(this.XamlRoot, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show error dialog: {ex.Message}");
            }
            finally { _isDialogOpen = false; }
        }

        // --- AutoSuggest Interactivity ---

        private void EditTaskCategory_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb) asb.IsSuggestionListOpen = true;
        }

        private void EditTaskCategory_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();
                var suggestions = new List<string>();

                if (string.IsNullOrEmpty(query))
                {
                    suggestions.AddRange(ViewModel.SavedCategories);
                }
                else
                {
                    suggestions.AddRange(ViewModel.SavedCategories
                        .Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList());

                    if (!ViewModel.SavedCategories.Any(c => c.Equals(query, StringComparison.OrdinalIgnoreCase)))
                    {
                        suggestions.Add($"Add \"{query}\"");
                    }
                }
                sender.ItemsSource = suggestions;
            }
        }

        private void EditTaskCategory_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var selected = args.SelectedItem.ToString() ?? "";
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                var newCat = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedCategories.Any(c => c.Equals(newCat, StringComparison.OrdinalIgnoreCase)))
                {
                    Settings.AddSavedCategory(newCat);
                    ViewModel.RefreshSavedCategories();
                }
                sender.Text = newCat;
            }
            else
            {
                sender.Text = selected;
            }
        }

        private void EditTaskTags_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb)
            {
                RefreshTagSuggestions(asb);
                asb.IsSuggestionListOpen = true;
            }
        }

        private void EditTaskTags_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                RefreshTagSuggestions(sender);
            }
        }

        private void RefreshTagSuggestions(AutoSuggestBox sender)
        {
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            var existingTags = (parts.Count > 1) ? parts.Take(parts.Count - 1).ToList() : new List<string>();

            var availableTags = ViewModel.SavedTags
                .Where(t => !existingTags.Any(et => et.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var suggestions = new List<string>();
            bool isExactMatch = ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(lastPart))
            {
                suggestions.AddRange(availableTags);
            }
            else if (isExactMatch)
            {
                // If the last part is a complete tag, show all other available tags
                suggestions.AddRange(availableTags.Where(t => !t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var filtered = availableTags
                    .Where(t => t.Contains(lastPart, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                suggestions.AddRange(filtered);

                if (!ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)))
                {
                    suggestions.Add($"Add \"{lastPart}\"");
                }
            }
            sender.ItemsSource = suggestions;
        }

        private void EditTaskTags_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not string selected) return;
            
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);

            string finalTag = selected;
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                finalTag = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedTags.Any(t => t.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
                {
                    Settings.AddSavedTag(finalTag);
                    ViewModel.RefreshSavedCategories();
                }
            }

            // If the last part was already a complete tag and we chose something else, restore it.
            if (ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)) && 
                !lastPart.Equals(finalTag, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(lastPart);
            }

            if (!parts.Any(p => p.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(finalTag);
            }

            sender.Text = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p))) + ", ";
        }
        private void Settings_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "PointerOver");
        }

        private void Settings_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "Normal");
        }

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.TaskHistoryEntry entry)
            {
                var detailView = new Dialogs.HistoryEntryDetailDialog(entry);
                var flyout = new Flyout
                {
                    Content = detailView,
                    Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                    FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
                    {
                        Setters = { new Setter(FlyoutPresenter.MaxWidthProperty, 1000) }
                    }
                };
                
                if (sender is ListView lv)
                {
                    var container = lv.ContainerFromItem(e.ClickedItem) as FrameworkElement;
                    flyout.ShowAt(container ?? lv);
                }
            }
        }
    }
}
