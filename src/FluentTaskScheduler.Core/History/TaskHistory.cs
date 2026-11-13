using Serilog;

namespace FluentTaskScheduler.History;

/// <inheritdoc cref="ITaskHistory"/>
public sealed class TaskHistory : ITaskHistory
{
    /// <summary>
    /// The most events a question about all tasks reads. Windows keeps 10 MB of history unless
    /// told otherwise, some 15 000 events, so this only matters where someone made the log far
    /// larger. When it is reached, the oldest runs of the period are left out.
    /// </summary>
    private const int DefaultEventLimit = 50_000;

    private readonly IEventLog _log;
    private readonly int _eventLimit;

    public TaskHistory(IEventLog log)
        : this(log, DefaultEventLimit)
    {
    }

    internal TaskHistory(IEventLog log, int eventLimit)
    {
        _log = log;
        _eventLimit = eventLimit;
    }

    public LogState GetLogState() => _log.GetState(TaskSchedulerLog.Channel);

    public EnableLogResult EnableLog() => _log.Enable(TaskSchedulerLog.Channel);

    public IReadOnlyList<TaskEvent> GetEvents(string taskPath, int maxCount, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
    {
        string query = TaskSchedulerLog.EventsOf(taskPath, from, to);
        return Read(TaskSchedulerLog.Channel, query, newestFirst: true, TaskEvent.From, maxCount, cancellation);
    }

    public IReadOnlyList<TaskRun> GetRuns(DateTimeOffset since, CancellationToken cancellation = default)
    {
        string query = TaskSchedulerLog.RunEventsSince(since);
        var events = Read(TaskSchedulerLog.Channel, query, newestFirst: true, TaskEvent.From, _eventLimit, cancellation);
        if (events.Count == _eventLimit)
            Log.Warning("The task history since {Since} holds more than {Limit} events; the oldest runs of that period are left out", since, _eventLimit);
        return TaskRun.FromEvents(events);
    }

    public IReadOnlyList<SystemEvent> GetSystemEvents(DateTimeOffset around, TimeSpan margin, CancellationToken cancellation = default)
    {
        string query = SystemEvent.Query(around - margin, around + margin);
        var events = Read(SystemEvent.Log, query, newestFirst: false, SystemEvent.From, int.MaxValue, cancellation);

        // The System log is not written in the order things happened: its sources stamp their
        // own events, and a shutdown is recorded ahead of the request that caused it.
        return [.. events.OrderBy(e => e.Time)];
    }

    private List<T> Read<T>(string log, string query, bool newestFirst, Func<LogRecord, T?> interpret, int maxCount, CancellationToken cancellation)
        where T : class
    {
        var items = new List<T>();
        if (maxCount <= 0) return items;

        cancellation.ThrowIfCancellationRequested();
        foreach (var record in _log.Read(log, query, newestFirst))
        {
            cancellation.ThrowIfCancellationRequested();
            if (interpret(record) is not { } item) continue;
            items.Add(item);
            if (items.Count == maxCount) break;
        }
        return items;
    }
}
