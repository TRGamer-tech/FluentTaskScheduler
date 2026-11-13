namespace FluentTaskScheduler.History;

/// <summary>What became of one run of a task.</summary>
public enum RunStatus
{
    /// <summary>The log shows the run starting and, so far, not ending.</summary>
    Running,

    /// <summary>Every action ran and returned 0.</summary>
    Succeeded,

    /// <summary>The run got going, but an action returned something other than 0, or a later action could not be launched.</summary>
    Failed,

    /// <summary>Nothing ran: the task's account could not be logged on, or its action could not be launched.</summary>
    CouldNotStart,

    /// <summary>Task Scheduler ended the run early: a user asked for it, a new instance replaced it, the computer went on battery power or stopped being idle.</summary>
    Stopped,

    /// <summary>Task Scheduler ended the run because it exceeded the task's time limit.</summary>
    TimedOut
}

/// <summary>
/// One run of a task, put together from the events Task Scheduler logged for it. A start request
/// that Task Scheduler turned down or queued is not a run.
/// </summary>
public sealed record TaskRun
{
    /// <summary>Full path of the task, e.g. \Backups\Nightly.</summary>
    public required string TaskPath { get; init; }

    public required RunStatus Status { get; init; }

    /// <summary>Task Scheduler's ID of the run. Null for a task that could not be started for a reason other than its action: that failure is logged without one.</summary>
    public Guid? InstanceId { get; init; }

    /// <summary>When the first action was launched. Null if nothing ran, or if the start lies before what was read from the log.</summary>
    public DateTimeOffset? Started { get; init; }

    /// <summary>Null while the run is going on.</summary>
    public DateTimeOffset? Ended { get; init; }

    /// <summary>
    /// 0 for a successful run; for a failed one the exit code of the first action that did not
    /// return 0, or the Windows error that kept an action from being launched; for a task that
    /// could not start, that Windows error. Null for a run that is going on, was stopped or timed
    /// out: the code Task Scheduler logs for a process it ended itself says nothing about the task.
    /// </summary>
    public uint? ResultCode { get; init; }

    /// <summary>The account the task ran as.</summary>
    public string User { get; init; } = "";

    /// <summary>The events the run was put together from, oldest first.</summary>
    public ValueList<TaskEvent> Events { get; init; } = [];

    public bool IsFinished => Status != RunStatus.Running;

    public TimeSpan? Duration => Ended - Started;

    /// <summary>
    /// Puts the runs together that a set of events describes, newest first. The events may be in
    /// any order and belong to any number of tasks; events that are not part of a run are ignored.
    /// </summary>
    public static IReadOnlyList<TaskRun> FromEvents(IEnumerable<TaskEvent> events)
    {
        var tracker = new RunTracker(capacity: int.MaxValue);
        foreach (var e in events.OrderBy(e => e.Time).ThenBy(e => e.RecordId)) tracker.Add(e, e.Time);
        return tracker.Runs().OrderByDescending(run => run.Started ?? run.Ended ?? run.Events[0].Time).ToList();
    }
}
