namespace FluentTaskScheduler.Models.Enums
{
    /// <summary>
    /// Mirrors Microsoft.Win32.TaskScheduler.TaskState, plus AccessDenied for tasks
    /// discovered via the event log that can't actually be queried.
    /// Declaration order matches the original string values' alphabetical sort order
    /// (Access Denied, Disabled, Queued, Ready, Running, Unknown) so sorting the task
    /// list by status is unaffected by the switch away from raw strings.
    /// </summary>
    public enum TaskState
    {
        AccessDenied,
        Disabled,
        Queued,
        Ready,
        Running,
        Unknown
    }

    public static class TaskStateExtensions
    {
        public static string ToDisplayString(this TaskState state) => state switch
        {
            TaskState.AccessDenied => "Access Denied",
            _ => state.ToString()
        };
    }
}
