namespace FluentTaskScheduler.History;

/// <summary>
/// What Task Scheduler recorded about the runs of tasks. All of it comes from one Windows event
/// log, which is switched off unless someone switched it on: check <see cref="GetLogState"/>
/// before telling the user that a task has no history.
/// </summary>
/// <remarks>The queries read the log on the calling thread and can take a moment; call them from a worker thread.</remarks>
public interface ITaskHistory
{
    /// <summary>Whether history is being recorded. Cheap, and never throws.</summary>
    LogState GetLogState();

    /// <summary>Switches recording on. Needs administrator rights; without them the result says so.</summary>
    EnableLogResult EnableLog();

    /// <summary>
    /// The events of one task, newest first. Make runs of them with <see cref="TaskRun.FromEvents"/>.
    /// </summary>
    /// <param name="taskPath">The task's path exactly as Task Scheduler reports it; the log is matched case-sensitively.</param>
    /// <param name="maxCount">At most this many events are returned: the newest ones.</param>
    /// <param name="from">Leave out events before this moment.</param>
    /// <param name="to">Leave out events after this moment.</param>
    /// <exception cref="HistoryUnavailableException">The log cannot be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellation"/> was signalled.</exception>
    IReadOnlyList<TaskEvent> GetEvents(string taskPath, int maxCount, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default);

    /// <summary>
    /// The runs of all tasks that have an event at or after <paramref name="since"/>, newest first.
    /// A run that began earlier is included without its start.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The log cannot be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellation"/> was signalled.</exception>
    IReadOnlyList<TaskRun> GetRuns(DateTimeOffset since, CancellationToken cancellation = default);

    /// <summary>
    /// Startups, shutdowns, sleep and logons within <paramref name="margin"/> before and after a
    /// moment, oldest first: what might explain a run that went wrong then.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The Windows System log cannot be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellation"/> was signalled.</exception>
    IReadOnlyList<SystemEvent> GetSystemEvents(DateTimeOffset around, TimeSpan margin, CancellationToken cancellation = default);
}
