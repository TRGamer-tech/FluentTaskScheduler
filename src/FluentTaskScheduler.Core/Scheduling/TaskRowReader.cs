using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Reads what the task list shows about a task.</summary>
internal static class TaskRowReader
{
    /// <summary>
    /// Never throws for a task that could be enumerated: a task whose definition cannot be read
    /// still gets a row, with <see cref="TaskRow.LoadError"/> saying why. Dropping it instead
    /// would make an existing task look as if it were missing.
    /// </summary>
    public static TaskRow Read(ScheduledTask task)
    {
        var row = new TaskRow(task.Path);
        try
        {
            row.State = ToRowState(task.State);
            row.IsEnabled = task.Enabled;
            row.LastRunTime = NullIfNever(task.LastRunTime);
            // Task Scheduler names no next run for a disabled task, and the library then works one
            // out from the triggers: a moment at which the task is not going to run.
            row.NextRunTime = task.Enabled ? NullIfNever(task.NextRunTime) : null;

            var definition = task.Definition;
            var metadata = TaskMetadata.Split(definition.RegistrationInfo.Description);
            row.Description = metadata.Description;
            row.Category = metadata.Category;
            row.Tags = metadata.Tags;
            row.CompletionActions = metadata.CompletionActions;
            row.Author = definition.RegistrationInfo.Author ?? "";
            row.IsHidden = definition.Settings.Hidden;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Task {Path} is listed without details because its definition could not be read", task.Path);
            row.LoadError = ex.Message;
        }
        return row;
    }

    public static TaskRowState ToRowState(TaskState state) => state switch
    {
        TaskState.Disabled => TaskRowState.Disabled,
        TaskState.Queued => TaskRowState.Queued,
        TaskState.Ready => TaskRowState.Ready,
        TaskState.Running => TaskRowState.Running,
        _ => TaskRowState.Unknown
    };

    // Task Scheduler gives a task that never ran this moment as its last run, and a task that is
    // not going to run again no moment at all.
    private static readonly DateTime NeverRan = new(1999, 11, 30);

    private static DateTime? NullIfNever(DateTime value) => value == DateTime.MinValue || value == NeverRan ? null : value;
}
