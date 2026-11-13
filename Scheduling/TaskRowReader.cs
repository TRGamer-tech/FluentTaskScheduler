using System;
using FluentTaskScheduler.Models;
using Microsoft.Win32.TaskScheduler;
using AppTaskState = FluentTaskScheduler.Models.Enums.TaskState;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Scheduling
{
    /// <summary>Reads what the task list shows about a task.</summary>
    internal static class TaskRowReader
    {
        /// <summary>
        /// Never throws for a task that could be enumerated: a task whose definition cannot be read
        /// still gets a row, with <see cref="TaskRow.LoadError"/> saying why. Dropping it instead
        /// would make an existing task look as if it were missing.
        /// </summary>
        public static TaskRow Read(ScheduledTask task)
        {
            var row = new TaskRow(task.Path);
            try
            {
                row.State = ToAppState(task.State);
                row.IsEnabled = task.Enabled;
                row.LastRunTime = NullIfNever(task.LastRunTime);
                row.NextRunTime = NullIfNever(task.NextRunTime);

                var definition = task.Definition;
                var metadata = TaskMetadata.Split(definition.RegistrationInfo.Description);
                row.Description = metadata.Description;
                row.Category = metadata.Category;
                row.Tags = metadata.Tags;
                row.Pipeline = metadata.Pipeline;
                row.Author = definition.RegistrationInfo.Author ?? "";
                row.IsHidden = definition.Settings.Hidden;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Task {Path} is listed without details because its definition could not be read", task.Path);
                row.LoadError = ex.Message;
            }
            return row;
        }

        public static AppTaskState ToAppState(TaskState state) => state switch
        {
            TaskState.Disabled => AppTaskState.Disabled,
            TaskState.Queued => AppTaskState.Queued,
            TaskState.Ready => AppTaskState.Ready,
            TaskState.Running => AppTaskState.Running,
            _ => AppTaskState.Unknown
        };

        private static DateTime? NullIfNever(DateTime value) => value == DateTime.MinValue ? null : value;
    }
}
