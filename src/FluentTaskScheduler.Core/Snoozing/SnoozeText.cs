using System.Globalization;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Snoozing;

/// <summary>What the app says about a snooze, in the user's language.</summary>
public static class SnoozeText
{
    /// <summary>
    /// The status of the snooze of all tasks as the banner, the tray icon and the notification
    /// show it, e.g. "Global Snooze Active (Ends at 14:30)".
    /// </summary>
    public static string AllTasks(SnoozeWindow window, TimeProvider time) =>
        window.Until is { } until
            ? Loc.Format("Snooze.Status.EndsAt", EndTime(until, time))
            : Loc.Get("Snooze.Status.UntilReboot");

    /// <summary>The status of a task that is snoozed on its own, e.g. "Snoozed until 14:30".</summary>
    public static string OneTask(SnoozeWindow window, TimeProvider time) =>
        window.Until is { } until
            ? Loc.Format("Task.Snooze.Status", EndTime(until, time))
            : Loc.Get("Task.Snooze.UntilRestart");

    /// <summary>The time alone for an end later today; date and time for an end on another day.</summary>
    private static string EndTime(DateTimeOffset until, TimeProvider time)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(until, time.LocalTimeZone);
        bool today = local.Date == time.GetLocalNow().Date;
        return local.ToString(today ? "t" : "g", CultureInfo.CurrentCulture);
    }
}
