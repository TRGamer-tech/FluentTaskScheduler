using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.ViewModels
{
    /// <summary>Multi-select (batch) actions on the task list. Contains no UI code.</summary>
    public partial class MainViewModel
    {
        /// <summary>False when any selected task is disabled: running or stopping such a batch makes no sense.</summary>
        [ObservableProperty]
        private bool _canBatchRunStop = true;

        /// <summary>Recomputes <see cref="CanBatchRunStop"/> for the current multi-selection. Single selections are ignored.</summary>
        public void UpdateBatchSelection(IReadOnlyCollection<ScheduledTaskModel> selected)
        {
            if (selected.Count <= 1) return;
            CanBatchRunStop = !selected.Any(t => !t.IsEnabled);
        }

        /// <summary>Stops every task; a task that fails to stop is skipped.</summary>
        public void BatchStop(IEnumerable<ScheduledTaskModel> tasks)
        {
            foreach (var task in tasks)
            {
                try
                {
                    _taskService.StopTask(task.Path);
                    task.State = Models.Enums.TaskState.Ready;
                    task.IsRunning = false;
                }
                catch { }
            }
        }

        /// <summary>
        /// Enables or disables every task that isn't already in that state. Returns the names of
        /// tasks Windows refused to change because they are protected.
        /// </summary>
        public List<string> BatchSetEnabled(IEnumerable<ScheduledTaskModel> tasks, bool enabled)
        {
            var denied = new List<string>();
            foreach (var task in tasks)
            {
                try
                {
                    if (task.IsEnabled == enabled) continue;
                    _taskService.SetTaskEnabled(task.Path, enabled);
                    task.IsEnabled = enabled;
                }
                catch (UnauthorizedAccessException) { denied.Add(task.Name); }
                catch { }
            }
            return denied;
        }

        /// <summary>Deletes every task, skipping any that fail, then reloads the list.</summary>
        public void BatchDelete(IEnumerable<ScheduledTaskModel> tasks)
        {
            foreach (var t in tasks) try { _taskService.DeleteTask(t.Path); } catch { }
            _ = LoadTasksAsync();
        }
    }
}
