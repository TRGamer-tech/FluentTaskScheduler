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
    /// <summary>Create/edit task dialog: trigger, action and option control handlers.</summary>
    public sealed partial class MainPage
    {
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

                // Expiration — each trigger has its own
                bool triggerHasExpiration = tr.ExpirationDate.HasValue;
                EditTaskExpires.IsChecked = triggerHasExpiration;
                EditTaskExpirationDate.Date = triggerHasExpiration ? tr.ExpirationDate!.Value.Date : DateTime.Today;
                EditTaskExpirationTime.Time = triggerHasExpiration ? tr.ExpirationDate!.Value.TimeOfDay : DateTime.Now.TimeOfDay;
                EditTaskExpirationDate.IsEnabled = triggerHasExpiration;
                EditTaskExpirationTime.IsEnabled = triggerHasExpiration;

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
            UpdateTriggerExpiration();
        }
        private void EditTaskExpirationDate_SelectedDateChanged(object sender, DatePickerSelectedValueChangedEventArgs e) => UpdateTriggerExpiration();
        private void EditTaskExpirationTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e) => UpdateTriggerExpiration();

        private void UpdateTriggerExpiration()
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                tr.ExpirationDate = EditTaskExpires.IsChecked == true
                    ? EditTaskExpirationDate.Date.Date + EditTaskExpirationTime.Time
                    : (DateTime?)null;
            }
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
            ViewModel.UpdateBatchSelection(SelectedTasks());
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
        private void BatchStop_Click(object sender, RoutedEventArgs e) => ViewModel.BatchStop(SelectedTasks());
        private async void BatchEnable_Click(object sender, RoutedEventArgs e) { var denied = ViewModel.BatchSetEnabled(SelectedTasks(), true); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to enable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDisable_Click(object sender, RoutedEventArgs e) { var denied = ViewModel.BatchSetEnabled(SelectedTasks(), false); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to disable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
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
                ViewModel.BatchDelete(tasks);
            }
        }
        private List<ScheduledTaskModel> SelectedTasks() => TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

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
            void AddItem(string label, SortColumn col)
            {
                var item = new MenuFlyoutItem { Text = label + ViewModel.GetSortIndicator(col), Command = ViewModel.SortByCommand, CommandParameter = col };
                flyout.Items.Add(item);
            }

            AddItem(L("Main.Sort.Name", "Name"), SortColumn.Name);
            AddItem(L("Main.Sort.Status", "Status"), SortColumn.Status);
            AddItem(L("Main.Sort.NextRun", "Next Run"), SortColumn.NextRun);
            AddItem(L("Main.Sort.LastRun", "Last Run"), SortColumn.LastRun);
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = L("Main.Sort.Clear", "Clear Sort"), Command = ViewModel.ClearSortCommand };
            flyout.Items.Add(clear);

            flyout.ShowAt(SortButton);
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


    }
}
