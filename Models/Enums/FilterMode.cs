namespace FluentTaskScheduler.Models.Enums
{
    /// <summary>
    /// What the task list's footer/global filter is currently restricting on.
    /// Folder means the filter is a specific folder path (tracked separately),
    /// not one of the fixed status keywords.
    /// </summary>
    public enum FilterMode
    {
        Folder,
        All,
        Running,
        Enabled,
        Disabled
    }
}
