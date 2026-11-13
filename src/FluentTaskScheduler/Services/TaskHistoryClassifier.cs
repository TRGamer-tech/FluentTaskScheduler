using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Classifies a <see cref="TaskHistoryEntry"/> as a success, a failure, or neutral, purely from
    /// its raw Task Scheduler <see cref="TaskHistoryEntry.EventId"/> (and, for event 201, its exit
    /// code) — never from <see cref="TaskHistoryEntry.Result"/>, which is a localized display string
    /// and therefore unsafe to compare against English literals. See ToDo.md item 1.5.
    /// </summary>
    public static class TaskHistoryClassifier
    {
        /// <summary>102 = Task Completed. 201 = Action Completed, but only when its exit code is 0.</summary>
        public static bool IsSuccess(TaskHistoryEntry entry) =>
            entry.EventId == 102 || (entry.EventId == 201 && entry.ExitCode == "0");

        /// <summary>
        /// 103 = Task Failed, 111 = Task Terminated, 202 = Action Failed, 203 = Action Launch Failed,
        /// 329 = Stopped (time limit), 331 = Stopped (on battery). 201 also counts as a failure when
        /// its exit code is non-zero.
        /// </summary>
        public static bool IsFailure(TaskHistoryEntry entry) =>
            entry.EventId is 103 or 111 or 202 or 203 or 329 or 331 ||
            (entry.EventId == 201 && entry.ExitCode != "0");
    }
}
