using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Composes the focused Task Scheduler services behind the single <see cref="ITaskService"/> facade. Contains no logic of its own.</summary>
    public class TaskServiceWrapper : ITaskService
    {
        private readonly ITaskCrudService _crud;
        private readonly IFolderService _folders;
        private readonly ITaskXmlImportExportService _xml;
        private readonly ITaskEventLogService _events;

        public TaskServiceWrapper(ITaskCrudService crud, IFolderService folders, ITaskXmlImportExportService xml, ITaskEventLogService events)
        {
            _crud = crud; _folders = folders; _xml = xml; _events = events;
        }

        // Tasks
        public List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true) => _crud.GetAllTasks(folderPath, recursive);
        public ScheduledTaskModel? GetTaskDetails(string path) => _crud.GetTaskDetails(path);
        public bool TaskExists(string path) => _crud.TaskExists(path);
        public void EnableTask(string path) => _crud.EnableTask(path);
        public void DisableTask(string path) => _crud.DisableTask(path);
        public void SetTaskEnabled(string path, bool enabled) => _crud.SetTaskEnabled(path, enabled);
        public void RunTask(string path) => _crud.RunTask(path);
        public void RunTask(string path, string origin) => _crud.RunTask(path, origin);
        public void StopTask(string path) => _crud.StopTask(path);
        public void DeleteTask(string path) => _crud.DeleteTask(path);
        public void RegisterTask(string folderPath, ScheduledTaskModel model) => _crud.RegisterTask(folderPath, model);

        // Folders
        public TaskFolderModel GetFolderStructure() => _folders.GetFolderStructure();
        public void MoveTask(string sourcePath, string targetFolderPath) => _folders.MoveTask(sourcePath, targetFolderPath);
        public void MoveFolder(string sourceFolderPath, string targetParentFolderPath) => _folders.MoveFolder(sourceFolderPath, targetParentFolderPath);
        public void CreateFolder(string path) => _folders.CreateFolder(path);
        public void DeleteFolder(string path) => _folders.DeleteFolder(path);
        public void RenameFolder(string oldPath, string newName) => _folders.RenameFolder(oldPath, newName);

        // XML
        public void ExportTask(string taskPath, string outputPath) => _xml.ExportTask(taskPath, outputPath);
        public void RegisterTaskFromXml(string folderPath, string name, string xml) => _xml.RegisterTaskFromXml(folderPath, name, xml);

        // Event log
        public List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false) => _events.DiscoverTasksFromEventLog(forceRefresh);
        public void InvalidateDiscoveredCache() => _events.InvalidateDiscoveredCache();
        public List<TaskHistoryEntry> GetTaskHistory(string taskPath) => _events.GetTaskHistory(taskPath);
        public Dictionary<string, int> GetRunningTaskEnginePids() => _events.GetRunningTaskEnginePids();
        public List<TaskRunRecord> GetRecentRunRecords(TimeSpan window) => _events.GetRecentRunRecords(window);
        public List<SystemEventEntry> GetSystemEventsNear(DateTime centerTime, TimeSpan window) => _events.GetSystemEventsNear(centerTime, window);
    }
}
