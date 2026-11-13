using System.Globalization;
using System.Runtime.InteropServices;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Snoozing;

/// <summary>What the app says about a snooze, in the user's language.</summary>
public static class SnoozeText
{
    /// <summary>
    /// The status of the snooze of all tasks as the banner, the tray icon and the notification
    /// show it, e.g. "All tasks snoozed until 14:30".
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

    /// <summary>
    /// Why the snooze of all tasks did not start, for the failures of
    /// <see cref="ISnoozeService.SnoozeAllAsync"/>; null for any other exception.
    /// </summary>
    public static string? WhyNotSnoozed(Exception failure) => failure switch
    {
        IOException or UnauthorizedAccessException => Loc.Format("Snooze.Error.NotRecorded", failure.Message),
        COMException => Loc.Format("Snooze.Error.TaskScheduler", failure.Message),
        _ => null
    };

    /// <summary>The time alone for an end later today; date and time for an end on another day.</summary>
    private static string EndTime(DateTimeOffset until, TimeProvider time)
    {
        // In the time zone Windows uses now. .NET keeps the zone it read first until its cache is
        // cleared; on a laptop taken to another zone, the end would otherwise be shown hours off.
        TimeZoneInfo.ClearCachedData();

        DateTimeOffset local = TimeZoneInfo.ConvertTime(until, time.LocalTimeZone);
        bool today = local.Date == time.GetLocalNow().Date;
        return local.ToString(today ? "t" : "g", CultureInfo.CurrentCulture);
    }
}
