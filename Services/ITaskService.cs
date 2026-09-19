using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Windows Task Scheduler operations (create/read/update/delete tasks and folders, history,
    /// analytics, import/export). Extends the narrow <see cref="ITaskServiceWrapper"/> subset
    /// (task listing, enable/disable, run) that the snooze/pipeline services depend on and that
    /// unit tests fake.
    /// </summary>
    public interface ITaskService : ITaskServiceWrapper
    {
        ScheduledTaskModel? GetTaskDetails(string path);
        void StopTask(string path);
        void DeleteTask(string path);
        void RegisterTask(string folderPath, ScheduledTaskModel model);
        void ExportTask(string taskPath, string outputPath);
        List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false);
        List<TaskHistoryEntry> GetTaskHistory(string taskPath);
        Dictionary<string, int> GetRunningTaskEnginePids();
        List<TaskRunRecord> GetRecentRunRecords(TimeSpan window);
        List<SystemEventEntry> GetSystemEventsNear(DateTime centerTime, TimeSpan window);
        TaskFolderModel GetFolderStructure();
        void MoveTask(string sourcePath, string targetFolderPath);
        void MoveFolder(string sourceFolderPath, string targetParentFolderPath);
        void CreateFolder(string path);
        void DeleteFolder(string path);
        void RegisterTaskFromXml(string folderPath, string name, string xml);
        void RenameFolder(string oldPath, string newName);
    }
}
