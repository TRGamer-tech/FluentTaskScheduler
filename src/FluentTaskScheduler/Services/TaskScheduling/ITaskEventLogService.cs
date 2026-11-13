using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Task Scheduler event-log access: history, run records, system events, task discovery.</summary>
    public interface ITaskEventLogService
    {
        List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false);
        void InvalidateDiscoveredCache();
        List<TaskHistoryEntry> GetTaskHistory(string taskPath);
        Dictionary<string, int> GetRunningTaskEnginePids();
        List<TaskRunRecord> GetRecentRunRecords(TimeSpan window);
        List<SystemEventEntry> GetSystemEventsNear(DateTime centerTime, TimeSpan window);
    }
}
