using System.Collections.Generic;

namespace FluentTaskScheduler.Models
{
    /// <summary>
    /// Post-execution ("completion action") configuration for a task: which downstream tasks
    /// should be started once this task finishes, split by success / failure outcome.
    /// Persisted inside the task description metadata block so it round-trips through the
    /// native Task Scheduler XML without needing a side-car database.
    /// </summary>
    public class TaskPipeline
    {
        /// <summary>Master switch. When false the configured downstream tasks are ignored.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Full task paths started when the task finishes with exit code 0.</summary>
        public List<string> OnSuccessTasks { get; set; } = new();

        /// <summary>Full task paths started when the task finishes with a non-zero exit code.</summary>
        public List<string> OnFailureTasks { get; set; } = new();

        public bool HasAnyTargets => OnSuccessTasks.Count > 0 || OnFailureTasks.Count > 0;

        /// <summary>True when this pipeline should actually be evaluated at runtime.</summary>
        public bool IsActive => IsEnabled && HasAnyTargets;

        public TaskPipeline Clone() => new()
        {
            IsEnabled = IsEnabled,
            OnSuccessTasks = new List<string>(OnSuccessTasks),
            OnFailureTasks = new List<string>(OnFailureTasks)
        };
    }
}
