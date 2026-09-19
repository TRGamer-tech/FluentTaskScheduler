using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests.Fakes
{
    /// <summary>In-memory stand-in for <see cref="ITaskServiceWrapper"/> so service logic can be
    /// unit tested without a real Task Scheduler.</summary>
    public class FakeTaskServiceWrapper : ITaskServiceWrapper
    {
        public List<ScheduledTaskModel> Tasks { get; } = new();

        /// <summary>Paths passed to SetTaskEnabled, in call order, with the requested state.</summary>
        public List<(string Path, bool Enabled)> EnableCalls { get; } = new();

        /// <summary>Paths that should throw when SetTaskEnabled is called on them (simulates protected/system tasks).</summary>
        public HashSet<string> ProtectedPaths { get; } = new();

        public List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true) => new(Tasks);

        public bool TaskExists(string path) => Tasks.Exists(t => t.Path == path);

        public void EnableTask(string path) => SetTaskEnabled(path, true);
        public void DisableTask(string path) => SetTaskEnabled(path, false);

        public virtual void SetTaskEnabled(string path, bool enabled)
        {
            if (ProtectedPaths.Contains(path))
                throw new System.UnauthorizedAccessException($"'{path}' is protected.");

            EnableCalls.Add((path, enabled));
            var task = Tasks.Find(t => t.Path == path);
            if (task != null) task.IsEnabled = enabled;
        }

        public void RunTask(string path) => RunTask(path, "Manual");
        public void RunTask(string path, string origin) { }
    }
}
