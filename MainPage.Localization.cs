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
    /// <summary>Localized UI text refresh.</summary>
    public sealed partial class MainPage
    {
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

    }
}
