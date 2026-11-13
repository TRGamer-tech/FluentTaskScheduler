using System.Globalization;
using FluentTaskScheduler.Localization;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Says in a few words when a trigger fires, for lists of triggers.</summary>
public static class TriggerText
{
    private static readonly (DaysOfTheWeek Day, string Key)[] Weekdays =
    [
        (DaysOfTheWeek.Monday, "Dialog.Day.Mon"), (DaysOfTheWeek.Tuesday, "Dialog.Day.Tue"), (DaysOfTheWeek.Wednesday, "Dialog.Day.Wed"),
        (DaysOfTheWeek.Thursday, "Dialog.Day.Thu"), (DaysOfTheWeek.Friday, "Dialog.Day.Fri"), (DaysOfTheWeek.Saturday, "Dialog.Day.Sat"),
        (DaysOfTheWeek.Sunday, "Dialog.Day.Sun")
    ];

    private static readonly (WhichWeek Week, string Key)[] WeeksOfMonth =
    [
        (WhichWeek.FirstWeek, "Dialog.Week.First"), (WhichWeek.SecondWeek, "Dialog.Week.Second"), (WhichWeek.ThirdWeek, "Dialog.Week.Third"),
        (WhichWeek.FourthWeek, "Dialog.Week.Fourth"), (WhichWeek.LastWeek, "Dialog.Week.Last")
    ];

    public static string Describe(TriggerDraft trigger)
    {
        string type = Loc.Get(TypeKey(trigger.Type));
        string time = trigger.Start is { } start ? $" {Loc.Get("Trigger.At")} {start.ToString("t", CultureInfo.CurrentCulture)}" : "";

        return trigger.Type switch
        {
            TriggerType.Once => trigger.Start is { } when ? $"{type}: {when.ToString("g", CultureInfo.CurrentCulture)}" : type,
            TriggerType.Daily => trigger.DaysInterval > 1
                ? $"{type} ({Loc.Format("Editor.Trigger.EveryNDays", trigger.DaysInterval)}){time}"
                : $"{type}{time}",
            TriggerType.Weekly => $"{type} ({Names(Weekdays, d => trigger.DaysOfWeek.HasFlag(d))}){time}",
            TriggerType.Monthly when trigger.MonthlyByWeekday =>
                $"{type} ({Names(WeeksOfMonth, w => trigger.Weeks.HasFlag(w))}: {Names(Weekdays, d => trigger.DaysOfWeek.HasFlag(d))}){time}",
            TriggerType.Monthly => $"{type} ({DaysOfMonth(trigger)}){time}",
            TriggerType.Event when EventQuery.TryParse(trigger.EventSubscription, out string log, out string source, out int? id) =>
                $"{type} ({string.Join(", ", new[] { log, source, id?.ToString() }.Where(part => !string.IsNullOrEmpty(part)))})",
            TriggerType.SessionStateChange => Loc.Format("Trigger.SessionStateChange", Loc.Get(SessionChangeKey(trigger.SessionChange))),
            TriggerType.Unsupported when trigger.UnsupportedSummary is { Length: > 0 } summary => $"{type} ({summary})",
            _ => type
        };
    }

    /// <summary>Resource name for the name of a trigger type, as the editor's list of types shows it.</summary>
    public static string TypeKey(TriggerType type) => type switch
    {
        TriggerType.Once => "Dialog.Trigger.Once",
        TriggerType.Daily => "Dialog.Trigger.Daily",
        TriggerType.Weekly => "Dialog.Trigger.Weekly",
        TriggerType.Monthly => "Dialog.Trigger.Monthly",
        TriggerType.AtLogon => "Trigger.AtLogon",
        TriggerType.AtStartup => "Trigger.AtStartup",
        TriggerType.OnIdle => "Trigger.OnIdle",
        TriggerType.Event => "Dialog.Trigger.Event",
        TriggerType.SessionStateChange => "Dialog.Trigger.Session",
        _ => "Dialog.Trigger.__Unsupported"
    };

    /// <summary>Resource name for a session change such as lock or remote connect.</summary>
    public static string SessionChangeKey(TaskSessionStateChangeType change) => change switch
    {
        TaskSessionStateChangeType.SessionLock => "Dialog.Sess.Lock",
        TaskSessionStateChangeType.SessionUnlock => "Dialog.Sess.Unlock",
        TaskSessionStateChangeType.ConsoleConnect => "Dialog.Sess.ConsoleConnect",
        TaskSessionStateChangeType.ConsoleDisconnect => "Dialog.Sess.ConsoleDisconnect",
        TaskSessionStateChangeType.RemoteConnect => "Dialog.Sess.RemoteConnect",
        TaskSessionStateChangeType.RemoteDisconnect => "Dialog.Sess.RemoteDisconnect",
        _ => "Dialog.Trigger.__Unsupported"
    };

    private static string DaysOfMonth(TriggerDraft trigger)
    {
        var parts = TaskDraftReader.DaysOfMonthFromMask(trigger.DaysOfMonth).Select(day => day.ToString(CultureInfo.CurrentCulture)).ToList();
        if (trigger.LastDayOfMonth) parts.Add(Loc.Get("Dialog.Week.Last"));
        return string.Join(", ", parts);
    }

    private static string Names<T>((T Value, string Key)[] all, Func<T, bool> isSet) =>
        string.Join(", ", all.Where(item => isSet(item.Value)).Select(item => Loc.Get(item.Key)));
}
