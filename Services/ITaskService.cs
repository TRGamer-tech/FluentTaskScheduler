using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Windows Task Scheduler operations (create/read/update/delete tasks and folders, history, import/export).</summary>
    public interface ITaskService
    {
        List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true);
        ScheduledTaskModel? GetTaskDetails(string path);
        void EnableTask(string path);
        void DisableTask(string path);
        void SetTaskEnabled(string path, bool enabled);
        void RunTask(string path);
        void StopTask(string path);
        void DeleteTask(string path);
        void RegisterTask(string folderPath, ScheduledTaskModel model);
        void ExportTask(string taskPath, string outputPath);
        List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false);
        List<TaskHistoryEntry> GetTaskHistory(string taskPath);
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
