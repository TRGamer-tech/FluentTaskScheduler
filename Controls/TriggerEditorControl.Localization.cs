using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Controls
{
    /// <summary>Localized text of the trigger editor.</summary>
    public sealed partial class TriggerEditorControl
    {
        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        private void ApplyLocalizedText()
        {
            DlgTriggersTitle.Text = L("TriggerTitle.Text", "Triggers");
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
            EditTaskRandomDelayVal.PlaceholderText = L("Dialog.Ph.Delay", "e.g. 1 hour");
            EditTaskIdleDuration.PlaceholderText = L("Dialog.Ph.Idle", "e.g., 10m");
            MonthlyDaysInput.PlaceholderText = L("Dialog.Ph.MonthDays", "e.g. 1, 15, Last");
            EditTaskEventLog.Header = L("Dialog.EventLog", "Log");
            EditTaskEventLog.PlaceholderText = L("Dialog.Ph.EventLog", "Application, System, Security, etc.");
            EditTaskEventSource.Header = L("Dialog.EventSource", "Source");
            EditTaskEventSource.PlaceholderText = L("Dialog.Ph.EventSource", "e.g., VSS, Outlook (Optional)");
            EditTaskEventId.Header = L("Dialog.EventId", "Event ID");
            EditTaskEventId.PlaceholderText = L("Dialog.Ph.EventId", "e.g., 1000 (Optional)");
            DlgDelayHint.Text = L("Dialog.Hint.Delay", "(e.g. 30s, 1m, 1h)");
            DlgIdleHint.Text = L("Dialog.Hint.Idle", "(e.g. 5m, 10m, 30m)");
            DlgMonthlyDaysHint.Text = L("Dialog.Hint.MonthDays", "(comma separated, use 'Last' for last day)");
            EditTaskDailyRecurrence.Content = L("Dialog.RecurEvery", "Recur every");
            DlgDaysSuffix.Text = L("Dialog.DaysSuffix", "day(s)");
            DlgWeeklyRecur.Text = L("Dialog.RecurEvery", "Recur every");
            DlgWeeksOn.Text = L("Dialog.WeeksOn", "weeks on:");
            DlgMonthsLabel.Text = L("Dialog.Months", "Months:");
            MonthlyRadioDays.Content = L("Dialog.Days", "Days");
            MonthlyRadioOn.Content = L("Dialog.On", "On");
            DlgIdleWait.Text = L("Dialog.IdleWait", "Wait for the computer to be idle for:");
            WeeklyMon.Content = L("Dialog.Day.Mon", "Mon");
            WeeklyTue.Content = L("Dialog.Day.Tue", "Tue");
            WeeklyWed.Content = L("Dialog.Day.Wed", "Wed");
            WeeklyThu.Content = L("Dialog.Day.Thu", "Thu");
            WeeklyFri.Content = L("Dialog.Day.Fri", "Fri");
            WeeklySat.Content = L("Dialog.Day.Sat", "Sat");
            WeeklySun.Content = L("Dialog.Day.Sun", "Sun");
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
            DlgWeekFirst.Content = L("Dialog.Week.First", "First");
            DlgWeekSecond.Content = L("Dialog.Week.Second", "Second");
            DlgWeekThird.Content = L("Dialog.Week.Third", "Third");
            DlgWeekFourth.Content = L("Dialog.Week.Fourth", "Fourth");
            DlgWeekLast.Content = L("Dialog.Week.Last", "Last");
            DlgDayMon.Content = L("Dialog.Weekday.Mon", "Monday");
            DlgDayTue.Content = L("Dialog.Weekday.Tue", "Tuesday");
            DlgDayWed.Content = L("Dialog.Weekday.Wed", "Wednesday");
            DlgDayThu.Content = L("Dialog.Weekday.Thu", "Thursday");
            DlgDayFri.Content = L("Dialog.Weekday.Fri", "Friday");
            DlgDaySat.Content = L("Dialog.Weekday.Sat", "Saturday");
            DlgDaySun.Content = L("Dialog.Weekday.Sun", "Sunday");
            EditTaskSessionStateType.Header = L("Dialog.TriggerOn", "Trigger on");
            DlgSessLock.Content = L("Dialog.Sess.Lock", "Workstation Lock");
            DlgSessUnlock.Content = L("Dialog.Sess.Unlock", "Workstation Unlock");
            DlgSessRdpOn.Content = L("Dialog.Sess.RdpConnect", "Remote Desktop Connect");
            DlgSessRdpOff.Content = L("Dialog.Sess.RdpDisconnect", "Remote Desktop Disconnect");
            EditTaskExpires.Content = L("Dialog.Expire", "Expire task on:");
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
        }
    }
}
