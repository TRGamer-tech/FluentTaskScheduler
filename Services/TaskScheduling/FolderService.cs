using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentTaskScheduler.Models;
using Microsoft.Win32.TaskScheduler;
using System.Collections.ObjectModel;
using AppTaskState = FluentTaskScheduler.Models.Enums.TaskState;
using AppTriggerType = FluentTaskScheduler.Models.Enums.TriggerType;

namespace FluentTaskScheduler.Services
{
    public class FolderService : IFolderService
    {
        private readonly TaskSchedulerConnection _connection;
        private readonly ITaskEventLogService _events;
        public FolderService(TaskSchedulerConnection connection, ITaskEventLogService events) { _connection = connection; _events = events; }

        public TaskFolderModel GetFolderStructure()
        {
            using (var ts = _connection.Open())
            {
                var root = new TaskFolderModel { Name = "Task Scheduler Library", Path = "\\" };
                EnumFolders(ts.RootFolder, root);

                // Synthesize folders from discovered tasks. This used to force a full event-log
                // re-scan (up to 2000 events) plus a fresh TaskService per discovered path on every
                // single folder-tree refresh; it now reuses the cache and is invalidated explicitly
                // by RegisterTask/DeleteTask instead (see 3.5).
                var discovered = _events.DiscoverTasksFromEventLog();
                foreach (var task in discovered)
                {
                    SynthesizeFoldersInTree(root, task.Path);
                }

                return root;
            }
        }

        private void SynthesizeFoldersInTree(TaskFolderModel root, string taskPath)
        {
            string? dirPath = System.IO.Path.GetDirectoryName(taskPath);
            if (string.IsNullOrEmpty(dirPath) || dirPath == "\\") return;

            // Split path into parts, skipping first empty (from root \)
            var parts = dirPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            string currentPath = "";

            foreach (var part in parts)
            {
                currentPath += "\\" + part;
                var child = current.SubFolders.FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new TaskFolderModel { Name = part, Path = currentPath };
                    current.SubFolders.Add(child);
                }
                current = child;
            }
        }

        private void EnumFolders(TaskFolder folder, TaskFolderModel model)
        {
            foreach (var subFolder in folder.SubFolders)
            {
                var subModel = new TaskFolderModel { Name = subFolder.Name, Path = subFolder.Path };
                model.SubFolders.Add(subModel);
                EnumFolders(subFolder, subModel);
            }
        }

        public void MoveTask(string sourcePath, string targetFolderPath)
        {
            using (var ts = _connection.Open())
            {
                var task = ts.GetTask(sourcePath);
                if (task == null) throw new Exception($"Task '{sourcePath}' not found.");

                var targetFolder = TaskSchedulerConnection.GetOrCreateFolder(ts, targetFolderPath);

                // Guard: already in the target folder
                string currentFolder = System.IO.Path.GetDirectoryName(sourcePath) ?? "\\";
                if (currentFolder.Equals(targetFolderPath, StringComparison.OrdinalIgnoreCase)) return;

                // Guard: name collision in target
                string taskName = task.Name;
                if (targetFolder.Tasks.Any(t => t.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception($"A task named '{taskName}' already exists in the target folder.");

                try
                {
                    targetFolder.RegisterTaskDefinition(taskName, task.Definition, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                }
                catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
                {
                    // Fall back to a safe (non-elevated) copy
                    var safeTd = ts.NewTask();
                    safeTd.XmlText = task.Xml;
                    safeTd.Principal.RunLevel = TaskRunLevel.LUA;
                    safeTd.Principal.LogonType = TaskLogonType.InteractiveToken;
                    safeTd.Principal.UserId = null;
                    targetFolder.RegisterTaskDefinition(taskName, safeTd, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                }

                task.Folder.DeleteTask(taskName);
                Serilog.Log.Information("{Message}", $"Moved task '{taskName}' to '{targetFolderPath}'.");
            }
        }

        public void MoveFolder(string sourceFolderPath, string targetParentFolderPath)
        {
            if (string.IsNullOrWhiteSpace(sourceFolderPath) || sourceFolderPath == "\\")
                throw new Exception("Cannot move the root folder.");

            // Guard: already in the target folder (no-op move)
            string currentParent = System.IO.Path.GetDirectoryName(sourceFolderPath) ?? "\\";
            if (targetParentFolderPath.TrimEnd('\\').Equals(currentParent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return;

            // Guard: moving into own descendant
            if (targetParentFolderPath.Equals(sourceFolderPath, StringComparison.OrdinalIgnoreCase) || 
                targetParentFolderPath.StartsWith(sourceFolderPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Cannot move a folder into one of its own sub-folders.");

            string newPath;
            using (var ts = _connection.Open())
            {
                var sourceFolder = ts.GetFolder(sourceFolderPath);
                if (sourceFolder == null) throw new Exception($"Source folder '{sourceFolderPath}' not found.");

                string folderName = sourceFolder.Name;
                newPath = targetParentFolderPath == "\\"
                    ? "\\" + folderName
                    : targetParentFolderPath.TrimEnd('\\') + "\\" + folderName;

                // Guard: name collision
                try
                {
                    if (ts.GetFolder(newPath) != null)
                        throw new Exception($"A folder named '{folderName}' already exists in the target.");
                }
                catch (System.IO.FileNotFoundException) { }

                var targetFolder = TaskSchedulerConnection.GetOrCreateFolder(ts, newPath);
                try
                {
                    CopyFolderContents(ts, sourceFolder, targetFolder);
                }
                catch
                {
                    // Copying only some tasks left a half-populated folder at the destination —
                    // clean it up so a failed move doesn't leave orphaned duplicates behind (3.10).
                    TryDeleteFolderQuietly(ts, newPath);
                    throw;
                }
            }

            // Perform deletion in a fresh context to ensure no handles are held
            DeleteFolder(sourceFolderPath);
            Serilog.Log.Information("{Message}", $"Moved folder '{sourceFolderPath}' to '{newPath}'.");
        }

        public void CreateFolder(string path)
        {
             using (var ts = _connection.Open())
             {
                 TaskSchedulerConnection.GetOrCreateFolder(ts, path);
             }
        }

        public void DeleteFolder(string path)
        {
            using (var ts = _connection.Open())
            {
                var folder = ts.GetFolder(path);
                if (folder != null && folder.Path != "\\")
                {
                   DeleteFolderRecursive(folder);
                   folder.Parent?.DeleteFolder(folder.Name);
                }
            }
        }

        private void DeleteFolderRecursive(TaskFolder folder)
        {
            // Copy to list to avoid collection modification exceptions
            var tasks = folder.Tasks.ToList();
            foreach (var task in tasks)
            {
                folder.DeleteTask(task.Name);
            }

            var subFolders = folder.SubFolders.ToList();
            foreach (var sub in subFolders)
            {
                DeleteFolderRecursive(sub);
                folder.DeleteFolder(sub.Name);
            }
        }

        public void RenameFolder(string oldPath, string newName)
        {
            using (var ts = _connection.Open())
            {
                var oldFolder = ts.GetFolder(oldPath);
                if (oldFolder == null || oldFolder.Path == "\\")
                    throw new Exception("Cannot rename the system root folder.");

                string parentPath = oldFolder.Parent?.Path ?? "\\";
                string newPath = parentPath == "\\" ? "\\" + newName : parentPath + "\\" + newName;

                try { if (ts.GetFolder(newPath) != null) throw new Exception($"A folder named '{newName}' already exists."); } catch (System.IO.FileNotFoundException) { }

                var newFolder = TaskSchedulerConnection.GetOrCreateFolder(ts, newPath);
                try
                {
                    CopyFolderContents(ts, oldFolder, newFolder);
                }
                catch
                {
                    // Same partial-copy cleanup as MoveFolder (3.10).
                    TryDeleteFolderQuietly(ts, newPath);
                    throw;
                }
                DeleteFolderRecursive(oldFolder);
                oldFolder.Parent?.DeleteFolder(oldFolder.Name);
            }
        }

        /// <summary>Best-effort cleanup of a half-populated target folder after a failed copy (3.10).</summary>
        private void TryDeleteFolderQuietly(TaskService ts, string path)
        {
            try
            {
                var folder = ts.GetFolder(path);
                if (folder != null && folder.Path != "\\")
                {
                    DeleteFolderRecursive(folder);
                    folder.Parent?.DeleteFolder(folder.Name);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not clean up the partially-copied target folder '{path}' after a failed move/rename: {ex.Message}");
            }
        }

        private void CopyFolderContents(TaskService ts, TaskFolder sourceFolder, TaskFolder targetFolder)
        {
            var failedTasks = new List<string>();
            CopyFolderContents(ts, sourceFolder, targetFolder, failedTasks);
            if (failedTasks.Count > 0)
            {
                throw new Exception(
                    $"Failed to copy {failedTasks.Count} task(s): {string.Join(", ", failedTasks)}. " +
                    "The source folder was left untouched so no tasks were lost.");
            }
        }

        private void CopyFolderContents(TaskService ts, TaskFolder sourceFolder, TaskFolder targetFolder, List<string> failedTasks)
        {
            foreach (var task in sourceFolder.Tasks)
            {
                try
                {
                    targetFolder.RegisterTaskDefinition(task.Name, task.Definition);
                }
                catch (Exception ex)
                {
                    if (TaskSchedulerErrors.IsAccessDenied(ex))
                    {
                        try
                        {
                            var td = ts.NewTask();
                            td.XmlText = task.Xml;
                            targetFolder.RegisterTaskDefinition(task.Name, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                        }
                        catch (Exception fallbackEx)
                        {
                            Serilog.Log.Error("{Message}", $"Failed to copy task '{task.Name}': {fallbackEx.Message}");
                            failedTasks.Add(task.Name);
                        }
                    }
                    else
                    {
                        Serilog.Log.Error("{Message}", $"Failed to copy task '{task.Name}': {ex.Message}");
                        failedTasks.Add(task.Name);
                    }
                }
            }

            foreach (var sub in sourceFolder.SubFolders)
            {
                var newSub = targetFolder.CreateFolder(sub.Name);
                CopyFolderContents(ts, sub, newSub, failedTasks);
            }
        }
    }
}
