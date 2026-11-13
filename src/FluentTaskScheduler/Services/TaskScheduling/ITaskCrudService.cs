using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Read, run, enable/disable, register and delete individual scheduled tasks.</summary>
    public interface ITaskCrudService : ITaskServiceWrapper
    {
        ScheduledTaskModel? GetTaskDetails(string path);
        void StopTask(string path);
        void DeleteTask(string path);
        void RegisterTask(string folderPath, ScheduledTaskModel model);
    }
}
