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
    /// <summary>Thrown when a run request is refused because global snooze is active.</summary>
    public class TaskSnoozedException : Exception
    {
        public string TaskPath { get; }

        public TaskSnoozedException(string taskPath)
            : base(string.Format(
                LocalizationService.GetString(
                    "Snooze.Error.RunBlocked",
                    "'{0}' was not started because Global Snooze is active."),
                System.IO.Path.GetFileName(taskPath)))
        {
            TaskPath = taskPath;
        }
    }

    public class TaskCrudService : ITaskCrudService
    {
        private readonly TaskSchedulerConnection _connection;
        private readonly ITaskEventLogService _events;
        public TaskCrudService(TaskSchedulerConnection connection, ITaskEventLogService events) { _connection = connection; _events = events; }

        public List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true)
        {
            var tasks = new List<ScheduledTaskModel>();
            using (var ts = _connection.Open())
            {
                var folder = ts.GetFolder(folderPath ?? "\\");
                if (folder != null)
                {
                    EnumFolderTasks(folder, tasks, recursive);
                }
            }

            // Merge discovered tasks from event log if they aren't already present
            var discovered = _events.DiscoverTasksFromEventLog();
            foreach (var d in discovered)
            {
                if (!tasks.Any(t => t.Path.Equals(d.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    // Filter by folder if requested
                    if (folderPath != null && folderPath != "\\" && !d.Path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                        
                    tasks.Add(d);
                }
            }

            return tasks;
        }

        public ScheduledTaskModel? GetTaskDetails(string path)
        {
            using (var ts = _connection.Open())
            {
                var task = ts.GetTask(path);
                if (task == null) return null;
                return TaskModelMapper.MapTaskToModel(task);
            }
        }

        private void EnumFolderTasks(TaskFolder folder, List<ScheduledTaskModel> tasks, bool recursive = true)
        {
            // Use AllTasks to include hidden tasks
            var sourceTasks = folder.AllTasks;
            
            foreach (var task in sourceTasks)
            {
                try
                {
                    // If not recursive, we only want tasks directly in this folder
                    if (!recursive)
                    {
                        var taskDir = System.IO.Path.GetDirectoryName(task.Path);
                        if (string.IsNullOrEmpty(taskDir)) taskDir = "\\";
                        if (!taskDir.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    tasks.Add(TaskModelMapper.MapTaskToModel(task));
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error("{Message}", $"Could not read task '{task.Name}': {ex.Message}");
                }
            }
        }

        public bool TaskExists(string path)
        {
            using var ts = _connection.Open();
            return ts.GetTask(path) != null;
        }

        public void EnableTask(string path) => SetTaskEnabled(path, true);
        public void DisableTask(string path) => SetTaskEnabled(path, false);

        public void SetTaskEnabled(string path, bool enabled)
        {
            try
            {
                using (var ts = _connection.Open())
                {
                    var task = ts.GetTask(path);
                    if (task != null) task.Enabled = enabled;
                }
            }
            catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
            {
                Serilog.Log.Error("{Message}", $"Access denied: Cannot {(enabled ? "enable" : "disable")} task '{path}'.");
                throw new UnauthorizedAccessException(
                    $"The user account under which you are performing this action does not have permission to {(enabled ? "enable" : "disable")} the task \"{System.IO.Path.GetFileName(path)}\".\n\n" +
                    "This task is protected and cannot be modified, even with administrator privileges.", ex);
            }
        }

        /// <summary>
        /// Raised when <see cref="RunTask"/> refuses to start a task because global snooze is active.
        /// </summary>
        public static event EventHandler<string>? RunSuppressedBySnooze;

        /// <summary>
        /// Starts a task immediately. Throws <see cref="TaskSnoozedException"/> instead of starting
        /// anything while global snooze is active.
        /// </summary>
        public void RunTask(string path) => RunTask(path, "Manual");

        /// <param name="origin">Where the request came from — recorded on the suppression log.</param>
        public void RunTask(string path, string origin)
        {
            if (SnoozeService.IsActive)
            {
                SnoozeService.RecordSuppressedRun(path, origin);
                NotificationService.ShowRunSuppressed(System.IO.Path.GetFileName(path));
                RunSuppressedBySnooze?.Invoke(this, path);
                throw new TaskSnoozedException(path);
            }

            try
            {
                using (var ts = _connection.Open())
                {
                    var task = ts.GetTask(path);
                    if (task != null)
                    {
                        task.RunEx(TaskRunFlags.IgnoreConstraints, 0, null);
                        NotificationService.ShowTaskStarted(task.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                var level = TaskSchedulerErrors.IsAccessDenied(ex) ? "Access denied" : "Error";
                Serilog.Log.Error("{Message}", $"{level}: Cannot run task '{path}': {ex.Message}");
                NotificationService.ShowTaskError(System.IO.Path.GetFileName(path), ex.Message);
                throw;
            }
        }

        public void StopTask(string path)
        {
            using (var ts = _connection.Open())
            {
                var task = ts.GetTask(path);
                task?.Stop();
            }
        }

        public void DeleteTask(string path)
        {
            using (var ts = _connection.Open())
            {
                var task = ts.GetTask(path);
                if (task != null) task.Folder.DeleteTask(task.Name);
            }
            _events.InvalidateDiscoveredCache();
        }

        public void RegisterTask(string folderPath, ScheduledTaskModel model)
        {
            using (var ts = _connection.Open())
            {
                TaskDefinition td = ts.NewTask();
                TaskDefinitionBuilder.ConfigureTaskDefinition(td, model);

                TaskFolder targetFolder = TaskSchedulerConnection.GetOrCreateFolder(ts, folderPath);

                string? userId = null;
                string? password = null;
                TaskLogonType logonType = TaskLogonType.InteractiveToken;

                if (model.RunAsSystem)
                {
                    userId = "SYSTEM";
                    logonType = TaskLogonType.ServiceAccount;
                }
                else if (!string.IsNullOrWhiteSpace(model.RunAsUser))
                {
                    userId = model.RunAsUser;
                    logonType = TaskLogonType.InteractiveToken;
                }

                try
                {
                    targetFolder.RegisterTaskDefinition(
                        model.Name,
                        td,
                        TaskCreation.CreateOrUpdate,
                        userId,
                        password,
                        logonType
                    );
                    Serilog.Log.Information("{Message}", $"Registered task '{model.Name}'.");
                }
                catch (Exception ex)
                {
                    if (TaskSchedulerErrors.IsAccessDenied(ex))
                    {
                        Serilog.Log.Warning("{Message}", $"Access denied registering task '{model.Name}' with elevated privileges; falling back to current user context.");
                        RegisterSafeTask(targetFolder, model, td);
                    }
                    else
                    {
                        Serilog.Log.Error("{Message}", $"Failed to register task '{model.Name}': {ex.Message}");
                        throw;
                    }
                }
            }
            _events.InvalidateDiscoveredCache();
        }

        private void RegisterSafeTask(TaskFolder targetFolder, ScheduledTaskModel model, TaskDefinition originalTd)
        {
            // Reuse the already-fully-configured definition instead of rebuilding one from scratch
            // and manually re-copying a hand-picked subset of its settings — that subset previously
            // dropped Hidden, WakeToRun, RunOnlyIfIdle, RestartCount/RestartInterval, and
            // NetworkSettings on every de-elevated fallback registration (see 3.8). Only the
            // principal/logon context actually needs to change for the de-elevated retry.
            originalTd.Principal.RunLevel = TaskRunLevel.LUA;
            originalTd.Principal.LogonType = TaskLogonType.InteractiveToken;
            originalTd.Principal.UserId = null;
            originalTd.Principal.GroupId = null;

            // Strip specific user contexts from triggers that require admin to target another user.
            foreach (var trig in originalTd.Triggers)
            {
                if (trig is SessionStateChangeTrigger sst) sst.UserId = null;
                if (trig is LogonTrigger lt) lt.UserId = null;
            }

            targetFolder.RegisterTaskDefinition(
                model.Name,
                originalTd,
                TaskCreation.CreateOrUpdate,
                null,
                null,
                TaskLogonType.InteractiveToken
            );

            Serilog.Log.Information("{Message}", $"Registered task '{model.Name}' via fallback (InteractiveToken).");
        }
    }
}
