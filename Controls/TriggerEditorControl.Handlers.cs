using System;
using System.Collections.Generic;
using System.Linq;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls
{
    /// <summary>Loads the selected trigger into the controls and writes edits back to it.</summary>
    public sealed partial class TriggerEditorControl
    {
        private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                _isPopulating = true;
                // Map Trigger Model -> UI
                foreach(var item in EditTaskTriggerType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.TriggerType.ToString()) EditTaskTriggerType.SelectedItem = item;
                }
                
                var dt = DurationUtil.TryParseScheduleInfo(tr.ScheduleInfo) ?? DateTime.MinValue;
                EditTaskStartDate.Date = dt == DateTime.MinValue ? DateTime.Today : dt;
                EditTaskStartTime.Time = dt == DateTime.MinValue ? DateTime.Now.TimeOfDay : dt.TimeOfDay;
                
                // Session State mapping
                foreach(var item in EditTaskSessionStateType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.SessionStateChangeType) EditTaskSessionStateType.SelectedItem = item;
                }

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

                SyncTypeFromCombo();
                _isPopulating = false;
                SelectedTriggerChanged?.Invoke(this, tr);
            }
        }

        private void EditTaskStartDate_SelectedDateChanged(object sender, DatePickerSelectedValueChangedEventArgs e) => UpdateTriggerScheduleInfo();
        private void EditTaskStartTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e) => UpdateTriggerScheduleInfo();

        private void UpdateTriggerScheduleInfo()
        {
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                var combined = EditTaskStartDate.Date.Date + EditTaskStartTime.Time;
                tr.ScheduleInfo = DurationUtil.FormatScheduleInfo(combined);
            }
        }

        private void DailyInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && short.TryParse(DailyInterval.Text, out short interval) && interval > 0)
                tr.DailyInterval = interval;
        }

        private void WeeklyInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && short.TryParse(WeeklyInterval.Text, out short interval) && interval > 0)
                tr.WeeklyInterval = interval;
        }

        private void WeeklyDay_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (_isPopulating) return;
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
            if (_isPopulating) return;
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
            if (_isPopulating) return;
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
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.MonthlyIsDayOfWeek = MonthlyRadioOn.IsChecked == true;
        }

        private void MonthlyWeekOrDay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;
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
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.IdleDuration = EditTaskIdleDuration.Text ?? "";
        }

        private void EditTaskEventTrigger_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                tr.EventLog = EditTaskEventLog.Text ?? "";
                tr.EventSource = EditTaskEventSource.Text ?? "";
                tr.EventId = int.TryParse(EditTaskEventId.Text, out int id) ? id : (int?)null;
            }
        }

        private void EditTaskSessionStateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskSessionStateType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.SessionStateChangeType = item.Tag.ToString()!;
            }
        }

        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;
            SyncTypeFromCombo();
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskTriggerType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null && Enum.TryParse<TriggerType>(item.Tag.ToString(), out var newTriggerType))
                    tr.TriggerType = newTriggerType;
            }
        }

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
            if (_isPopulating) return;
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
            if (_isPopulating) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.RandomDelay = on ? EditTaskRandomDelayVal.Text ?? "" : "";
        }

        private void EditTaskRandomDelayVal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (EditTaskRandomDelay.IsChecked != true) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
                tr.RandomDelay = EditTaskRandomDelayVal.Text ?? "";
        }
        private void EditTaskStopAfter_Click(object sender, RoutedEventArgs e) { if (EditTaskStopAfterVal != null) EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked == true; }
        private void EditTaskDailyRecurrence_Checked(object sender, RoutedEventArgs e) { if (DailyInterval != null) DailyInterval.IsEnabled = EditTaskDailyRecurrence.IsChecked == true; }
    }
}
