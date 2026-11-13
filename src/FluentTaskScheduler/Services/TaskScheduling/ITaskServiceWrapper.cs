using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{

    /// <summary>
    /// The subset of <see cref="TaskServiceWrapper"/> that other services depend on, extracted so
    /// SnoozeService/TaskPipelineService logic can be unit tested against a fake implementation
    /// without a real Task Scheduler.
    /// </summary>
    public interface ITaskServiceWrapper
    {
        List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true);
        bool TaskExists(string path);
        void EnableTask(string path);
        void DisableTask(string path);
        void SetTaskEnabled(string path, bool enabled);
        void RunTask(string path);
        void RunTask(string path, string origin);
    }
}
