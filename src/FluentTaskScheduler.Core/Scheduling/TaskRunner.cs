using FluentTaskScheduler.Notifications;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Why a task is being started.</summary>
public enum RunOrigin
{
    /// <summary>The user asked for it, in the app or from the command line.</summary>
    Manual,
    /// <summary>A completion action of another task.</summary>
    CompletionAction
}

/// <summary>What became of a request to start a task.</summary>
public enum RunOutcome
{
    Started,
    /// <summary>Not started because tasks are snoozed.</summary>
    Suppressed
}

/// <summary>Tells whether tasks may be started right now, and keeps track of the runs that were refused.</summary>
public interface ISnoozeGate
{
    /// <summary>True while starting <paramref name="taskPath"/> from this app is not allowed.</summary>
    bool BlocksRun(string taskPath);

    void RecordSuppressedRun(string taskPath, RunOrigin origin);
}

/// <summary>Starts tasks on request.</summary>
public interface ITaskRunner
{
    /// <exception cref="TaskNotFoundException">There is no such task.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused to start it.</exception>
    RunOutcome Run(string taskPath, RunOrigin origin = RunOrigin.Manual);
}

/// <summary>
/// The one place a run request from the app goes through: it honours the snooze, starts the
/// task and tells the user what happened. The store itself only talks to Task Scheduler.
/// </summary>
public sealed class TaskRunner : ITaskRunner
{
    private readonly ITaskStore _store;
    private readonly ISnoozeGate _snooze;
    private readonly INotifier _notifier;

    public TaskRunner(ITaskStore store, ISnoozeGate snooze, INotifier notifier)
    {
        _store = store;
        _snooze = snooze;
        _notifier = notifier;
    }

    public RunOutcome Run(string taskPath, RunOrigin origin = RunOrigin.Manual)
    {
        string name = taskPath[(taskPath.LastIndexOf('\\') + 1)..];

        if (_snooze.BlocksRun(taskPath))
        {
            _snooze.RecordSuppressedRun(taskPath, origin);
            _notifier.RunSuppressed(name);
            return RunOutcome.Suppressed;
        }

        try
        {
            _store.Run(taskPath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Task {Path} could not be started ({Origin})", taskPath, origin);
            _notifier.TaskCouldNotStart(name, ex.Message);
            throw;
        }

        // A completion action announces itself; a second "started" notification would only repeat it.
        if (origin == RunOrigin.Manual) _notifier.TaskStarted(name);
        return RunOutcome.Started;
    }
}
