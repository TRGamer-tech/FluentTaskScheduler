namespace FluentTaskScheduler.History;

public enum TaskChangeKind
{
    Registered,

    /// <summary>The task's definition was changed, or the task was enabled or disabled.</summary>
    Updated,

    Deleted
}

/// <summary>A task was registered, changed or deleted, by this app or by anything else on the computer.</summary>
public sealed record TaskChange(string TaskPath, TaskChangeKind Kind, DateTimeOffset Time);

/// <summary>
/// What Task Scheduler does, reported while the app runs. There is one stream for the whole app.
/// </summary>
/// <remarks>
/// <para>Handlers are called one at a time on a background thread, never on the UI thread. A
/// handler that throws is logged and affects neither the other handlers nor the stream.</para>
/// <para>The notifications come from Task Scheduler's history log and lag behind it: a start is
/// reported about a second after the fact, an end about two. While that log is switched off
/// nothing is reported; the stream notices on its own when it is switched on.</para>
/// </remarks>
public interface ITaskEventStream
{
    /// <summary>A run launched its first action. Not raised for a task that could not be started.</summary>
    event EventHandler<TaskRun>? RunStarted;

    /// <summary>
    /// A run is over, raised once per run with its final <see cref="TaskRun.Status"/>. This
    /// includes a task that could not be started, and a run that began before the app did.
    /// </summary>
    event EventHandler<TaskRun>? RunCompleted;

    /// <summary>A task was registered, changed or deleted. Registering a task reports <see cref="TaskChangeKind.Registered"/> followed by <see cref="TaskChangeKind.Updated"/>, as Task Scheduler logs both.</summary>
    event EventHandler<TaskChange>? TaskChanged;
}
