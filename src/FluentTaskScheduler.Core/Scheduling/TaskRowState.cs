namespace FluentTaskScheduler.Scheduling;

/// <summary>The state of a task as the task list shows it.</summary>
public enum TaskRowState
{
    Unknown,
    Ready,
    Running,
    Queued,
    Disabled
}
