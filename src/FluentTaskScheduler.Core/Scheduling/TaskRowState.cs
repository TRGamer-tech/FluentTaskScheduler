namespace FluentTaskScheduler.Scheduling;

/// <summary>The state of a task as the task list shows it.</summary>
public enum TaskRowState
{
    Unknown,
    Ready,
    Running,
    Queued,
    Disabled,

    /// <summary>The task exists, but this user is not allowed to read it.</summary>
    AccessDenied
}
