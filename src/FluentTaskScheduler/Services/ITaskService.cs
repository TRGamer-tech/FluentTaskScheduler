namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Facade over the focused Task Scheduler services. Extends the narrow
    /// <see cref="ITaskServiceWrapper"/> subset (task listing, enable/disable, run) that the
    /// snooze/pipeline services depend on and that unit tests fake. New code should prefer the
    /// focused interfaces (<see cref="ITaskCrudService"/>, <see cref="IFolderService"/>,
    /// <see cref="ITaskXmlImportExportService"/>, <see cref="ITaskEventLogService"/>).
    /// </summary>
    public interface ITaskService : ITaskCrudService, IFolderService, ITaskXmlImportExportService, ITaskEventLogService
    {
    }
}
