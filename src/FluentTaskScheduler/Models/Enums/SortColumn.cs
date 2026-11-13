namespace FluentTaskScheduler.Models.Enums
{
    /// <summary>Column the task list is currently sorted by. None means no active sort.</summary>
    public enum SortColumn
    {
        None,
        Name,
        Status,
        NextRun,
        LastRun
    }
}
