using System.Collections.Frozen;
using System.Globalization;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.History;

/// <summary>Where Task Scheduler keeps its history, what the events in it mean and how the app asks for them.</summary>
internal static class TaskSchedulerLog
{
    /// <summary>The log behind "All Tasks History". Windows ships it switched off.</summary>
    public const string Channel = "Microsoft-Windows-TaskScheduler/Operational";

    public const string Provider = "Microsoft-Windows-TaskScheduler";

    // Taken from what the provider declares and from the event sequences real runs leave behind.
    // 101 is the failed start that is seen in practice; 103 is its declared variant that names an instance.
    private static readonly FrozenDictionary<int, TaskEventKind> Kinds = new Dictionary<int, TaskEventKind>
    {
        [100] = TaskEventKind.RunStarted,
        [101] = TaskEventKind.StartFailed,
        [102] = TaskEventKind.RunCompleted,
        [103] = TaskEventKind.StartFailed,
        [106] = TaskEventKind.Registered,
        [111] = TaskEventKind.Terminated,
        [140] = TaskEventKind.Updated,
        [141] = TaskEventKind.Deleted,
        [142] = TaskEventKind.Updated,
        [200] = TaskEventKind.ActionStarted,
        [201] = TaskEventKind.ActionCompleted,
        [202] = TaskEventKind.ActionFailed,
        [203] = TaskEventKind.ActionFailed,
        [323] = TaskEventKind.Stopping,
        [327] = TaskEventKind.Stopping,
        [328] = TaskEventKind.Stopping,
        [329] = TaskEventKind.TimedOut,
        [330] = TaskEventKind.Stopping
    }.ToFrozenDictionary();

    public static TaskEventKind KindOf(int eventId) => Kinds.GetValueOrDefault(eventId);

    /// <summary>Selects every event that names the task, optionally within a period.</summary>
    public static string EventsOf(string taskPath, DateTimeOffset? from, DateTimeOffset? to)
    {
        string named = $"EventData[Data[@Name='TaskName']={EventQuery.ToXPathLiteral(taskPath)}]";
        return Period(from, to) is { } period ? $"*[System[{period}] and {named}]" : $"*[{named}]";
    }

    /// <summary>Selects, for all tasks, the events runs are made of from a moment on.</summary>
    public static string RunEventsSince(DateTimeOffset since) =>
        $"*[System[({Ids(kind => kind is not (TaskEventKind.Registered or TaskEventKind.Updated or TaskEventKind.Deleted))}) and {Period(since, null)}]]";

    /// <summary>Selects everything the app reacts to while it runs: what runs are made of, and tasks being registered, changed and deleted.</summary>
    public static string LiveEvents { get; } = $"*[System[({Ids(_ => true)})]]";

    private static string Ids(Func<TaskEventKind, bool> wanted) =>
        string.Join(" or ", Kinds.Where(entry => wanted(entry.Value)).Select(entry => entry.Key).Order().Select(id => $"EventID={id.ToString(CultureInfo.InvariantCulture)}"));

    private static string? Period(DateTimeOffset? from, DateTimeOffset? to)
    {
        var bounds = new List<string>(2);
        if (from is { } start) bounds.Add($"@SystemTime>='{Format(start)}'");
        if (to is { } end) bounds.Add($"@SystemTime<='{Format(end)}'");
        return bounds.Count == 0 ? null : $"TimeCreated[{string.Join(" and ", bounds)}]";
    }

    /// <summary>The form the event log compares times in: UTC, to the millisecond.</summary>
    internal static string Format(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
