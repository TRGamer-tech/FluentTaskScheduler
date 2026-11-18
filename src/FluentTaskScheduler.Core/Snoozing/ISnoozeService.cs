using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;

namespace FluentTaskScheduler.Snoozing;

/// <summary>What became of a request to snooze one task.</summary>
public enum SnoozeTaskOutcome
{
    Snoozed,

    /// <summary>
    /// Not snoozed: the task is disabled, and not by this app. Snoozing it would mean enabling it
    /// afterwards, which nobody asked for.
    /// </summary>
    AlreadyDisabled
}

/// <summary>
/// Snoozes all tasks at once, or one task.
/// <para>
/// While all tasks are snoozed this app starts none (see <see cref="ISnoozeGate"/>). If
/// <see cref="AppSettings.SnoozeSuspendsScheduledTasks"/> is on, every enabled task is also
/// disabled in Task Scheduler, so that Windows does not start it either. A task snoozed on its
/// own is always disabled. What a snooze disabled is re-enabled when the snooze ends, and
/// nothing else is.
/// </para>
/// <para>
/// The commands may be called from any thread. They run one at a time and talk to Task
/// Scheduler on a background thread; the task they return completes when that work is done.
/// A task that cannot be re-enabled does not fail a command: it is listed in
/// <see cref="SnoozeState.PendingRestores"/>.
/// </para>
/// </summary>
public interface ISnoozeService
{
    SnoozeState State { get; }

    /// <summary>
    /// Raised after <see cref="State"/> changed, on whatever thread made the change. A change that
    /// disables or re-enables tasks is reported twice: when it is decided, with
    /// <see cref="SnoozeState.IsBusy"/> set, and again when the tasks are done.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Snoozes all tasks until <paramref name="end"/>. If they are snoozed already, this replaces
    /// that snooze: its end, and what it covers according to the settings as they are now. A task
    /// that Windows refuses to disable is left as it is.
    /// </summary>
    /// <exception cref="IOException">The snooze could not be recorded on disk; nothing was changed.</exception>
    /// <exception cref="UnauthorizedAccessException">As for <see cref="IOException"/>.</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">Task Scheduler could not list the tasks to disable; nothing was changed.</exception>
    Task SnoozeAllAsync(SnoozeEnd end);

    /// <summary>Ends the snooze of all tasks now. A task that is also snoozed on its own stays snoozed.</summary>
    Task ResumeAllAsync();

    /// <summary>
    /// Disables one task until <paramref name="end"/>. For a task that is snoozed already this
    /// changes when its snooze ends.
    /// </summary>
    /// <exception cref="TaskNotFoundException">There is no such task.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused to disable the task; it is not snoozed.</exception>
    /// <exception cref="IOException">The snooze could not be recorded on disk; nothing was changed.</exception>
    /// <exception cref="UnauthorizedAccessException">As for <see cref="IOException"/>.</exception>
    /// <exception cref="System.Runtime.InteropServices.COMException">Task Scheduler answered with another error; the task is not snoozed.</exception>
    Task<SnoozeTaskOutcome> SnoozeTaskAsync(string taskPath, SnoozeEnd end);

    /// <summary>Ends the task's own snooze now. The task stays disabled while a snooze of all tasks still covers it.</summary>
    Task ResumeTaskAsync(string taskPath);

    /// <summary>
    /// Tells the service that a task was renamed or moved to another folder. Its snooze follows
    /// it, so that it is re-enabled where it is now; a snooze knows a task only by its path.
    /// Call it after every rename or move. For a task that is not snoozed it does nothing.
    /// </summary>
    Task TaskMovedAsync(string fromPath, string toPath);

    /// <summary>As <see cref="TaskMovedAsync"/>, for all tasks in a folder that was renamed or moved, at any depth.</summary>
    Task FolderMovedAsync(string fromPath, string toPath);

    /// <summary>Tries again to re-enable the tasks in <see cref="SnoozeState.PendingRestores"/>.</summary>
    Task RetryRestoresAsync();
}
