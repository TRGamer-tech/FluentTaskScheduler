using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Snoozing;

/// <summary>
/// What is snoozed right now, as an immutable snapshot. <see cref="ISnoozeService.State"/> holds
/// the current one; every change replaces it and raises <see cref="ISnoozeService.Changed"/>.
/// </summary>
public sealed record SnoozeState
{
    /// <summary>The snooze of all tasks; null while there is none.</summary>
    public GlobalSnooze? Global { get; init; }

    /// <summary>The tasks that are snoozed on their own.</summary>
    public ValueList<TaskSnooze> Tasks { get; init; } = [];

    /// <summary>Runs this app refused because of a snooze, newest first.</summary>
    public ValueList<SuppressedRun> SuppressedRuns { get; init; } = [];

    /// <summary>
    /// Tasks whose snooze is over but which are still disabled, because re-enabling them failed.
    /// They are tried again at the next start and by <see cref="ISnoozeService.RetryRestoresAsync"/>.
    /// </summary>
    public ValueList<PendingRestore> PendingRestores { get; init; } = [];

    /// <summary>
    /// Set when stored snooze state could not be read at startup: where that file is kept now.
    /// Tasks an earlier session disabled may then still be disabled without being listed.
    /// </summary>
    public string? UnreadableStateFile { get; init; }

    /// <summary>
    /// True while tasks are still being disabled or re-enabled for what this snapshot says. A
    /// task list read now may be out of date by the time it turns false.
    /// </summary>
    public bool IsBusy { get; init; }

    /// <summary>True while this app must not start the task: all tasks are snoozed, or this one is.</summary>
    public bool BlocksRun(string taskPath) => Global is not null || WindowOf(taskPath) is not null;

    /// <summary>The window of the task's own snooze; null when it has none. Paths compare without regard to case.</summary>
    public SnoozeWindow? WindowOf(string taskPath)
    {
        foreach (TaskSnooze task in Tasks)
        {
            if (string.Equals(task.TaskPath, taskPath, StringComparison.OrdinalIgnoreCase)) return task.Window;
        }
        return null;
    }
}

/// <summary>How long a snooze lasts.</summary>
/// <param name="Until">When it ends; null for a snooze that lasts until the user signs out or Windows restarts.</param>
public sealed record SnoozeWindow(DateTimeOffset? Until)
{
    public static SnoozeWindow UntilRestart { get; } = new(Until: null);
}

/// <summary>The snooze of all tasks: while it lasts, this app starts no task.</summary>
/// <param name="Window">How long it lasts.</param>
/// <param name="SuspendsTriggers">Whether it also disabled the tasks in Task Scheduler, so that their triggers do not fire either.</param>
/// <param name="SuspendedTasks">Paths of the tasks it keeps disabled.</param>
public sealed record GlobalSnooze(SnoozeWindow Window, bool SuspendsTriggers, ValueList<string> SuspendedTasks);

/// <summary>The snooze of one task. The task is disabled for as long as it lasts.</summary>
public sealed record TaskSnooze(string TaskPath, SnoozeWindow Window);

/// <summary>A run this app refused because of a snooze.</summary>
public sealed record SuppressedRun(DateTimeOffset Time, string TaskPath, RunOrigin Origin);

/// <summary>A task this app disabled and could not re-enable when its snooze ended.</summary>
/// <param name="Detail">The error as Task Scheduler worded it; for the log and for details, not for a headline.</param>
public sealed record PendingRestore(string TaskPath, RestoreFailure Reason, string Detail);

/// <summary>Why a task could not be re-enabled. Member names are the stored format.</summary>
public enum RestoreFailure
{
    /// <summary>Windows refused. Typical for a task that was snoozed while the app ran as administrator and is to be re-enabled without.</summary>
    AccessDenied,

    /// <summary>Task Scheduler answered with another error.</summary>
    Error
}
