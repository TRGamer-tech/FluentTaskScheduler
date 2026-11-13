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

    private static readonly (MonthsOfTheYear Month, string Key)[] Months =
    [
        (MonthsOfTheYear.January, "Dialog.Month.Jan"), (MonthsOfTheYear.February, "Dialog.Month.Feb"), (MonthsOfTheYear.March, "Dialog.Month.Mar"),
        (MonthsOfTheYear.April, "Dialog.Month.Apr"), (MonthsOfTheYear.May, "Dialog.Month.May"), (MonthsOfTheYear.June, "Dialog.Month.Jun"),
        (MonthsOfTheYear.July, "Dialog.Month.Jul"), (MonthsOfTheYear.August, "Dialog.Month.Aug"), (MonthsOfTheYear.September, "Dialog.Month.Sep"),
        (MonthsOfTheYear.October, "Dialog.Month.Oct"), (MonthsOfTheYear.November, "Dialog.Month.Nov"), (MonthsOfTheYear.December, "Dialog.Month.Dec")
    ];

    // Put together with format texts rather than with spaces and punctuation in code: languages
    // differ in where the time goes, whether a word comes before it, and which brackets and
    // separators they use.
    public static string Describe(TriggerDraft trigger)
    {
        string type = Loc.Get(TypeKey(trigger.Type));
        string With(string detail) => Loc.Format("Text.DetailFormat", type, detail);

        return trigger.Type switch
        {
            TriggerType.Once => trigger.Start is { } when ? Loc.Format("Trigger.PartFormat", type, when.ToString("g", CultureInfo.CurrentCulture)) : type,
            TriggerType.Daily => AtTime(trigger, trigger.DaysInterval > 1 ? With(Loc.Format("Editor.Trigger.EveryNDays", trigger.DaysInterval)) : type),
            TriggerType.Weekly => AtTime(trigger, With(EveryNWeeks(trigger, Names(Weekdays, d => trigger.DaysOfWeek.HasFlag(d))))),
            TriggerType.Monthly when trigger.MonthlyByWeekday => AtTime(trigger, With(InMonths(trigger,
                Loc.Format("Trigger.PartFormat", Names(WeeksOfMonth, w => trigger.Weeks.HasFlag(w)), Names(Weekdays, d => trigger.DaysOfWeek.HasFlag(d)))))),
            TriggerType.Monthly => AtTime(trigger, With(InMonths(trigger, DaysOfMonth(trigger)))),
            TriggerType.Event when EventQuery.TryParse(trigger.EventSubscription, out string log, out string source, out int? id) =>
                With(Loc.List(new[] { log, source, id?.ToString() }.OfType<string>().Where(part => part.Length > 0))),
            TriggerType.SessionStateChange => Loc.Format("Trigger.SessionStateChange", Loc.Get(SessionChangeKey(trigger.SessionChange))),
            TriggerType.Unsupported when trigger.UnsupportedSummary is { Length: > 0 } summary => With(summary),
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

    /// <summary>A calendar trigger's summary with the time of day it fires.</summary>
    private static string AtTime(TriggerDraft trigger, string summary) =>
        trigger.Start is { } start ? Loc.Format("Trigger.AtTimeFormat", summary, start.ToString("t", CultureInfo.CurrentCulture)) : summary;

    private static string EveryNWeeks(TriggerDraft trigger, string days) =>
        trigger.WeeksInterval > 1 ? Loc.Format("Trigger.PartFormat", Loc.Format("Editor.Trigger.EveryNWeeks", trigger.WeeksInterval), days) : days;

    /// <summary>The months a monthly trigger is limited to. Every month goes without saying, and none at all is an error the editor reports.</summary>
    private static string InMonths(TriggerDraft trigger, string days) =>
        trigger.Months is MonthsOfTheYear.AllMonths or 0 ? days : Loc.Format("Trigger.InMonthsFormat", days, Names(Months, m => trigger.Months.HasFlag(m)));

    private static string DaysOfMonth(TriggerDraft trigger)
    {
        var parts = TaskDraftReader.DaysOfMonthFromMask(trigger.DaysOfMonth).Select(day => Loc.Format("Trigger.DayOfMonthFormat", day)).ToList();
        if (trigger.LastDayOfMonth) parts.Add(Loc.Get("Trigger.LastDayOfMonth"));
        return Loc.List(parts);
    }

    private static string Names<T>((T Value, string Key)[] all, Func<T, bool> isSet) =>
        Loc.List(all.Where(item => isSet(item.Value)).Select(item => Loc.Get(item.Key)));
}
