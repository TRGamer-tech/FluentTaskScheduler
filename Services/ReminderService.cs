using System;
using System.Collections.Generic;
using System.Threading;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Background service that fires toast reminders for tasks that are about to run.
    /// Checks every minute; notifies once per (task, scheduled-run-time) pair.
    /// </summary>
    public static class ReminderService
    {
        private static Timer? _timer;
        private static readonly HashSet<string> _notifiedKeys = new();
        private static readonly object _lock = new();

        public static void Start()
        {
            Stop();
            _timer = new Timer(CheckUpcomingTasks, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            LogService.Info("ReminderService started.");
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private static void CheckUpcomingTasks(object? state)
        {
            if (!SettingsService.ShowNotifications || !SettingsService.EnableUpcomingReminders)
                return;

            try
            {
                var taskService = new TaskServiceWrapper();
                var tasks = taskService.GetAllTasks(recursive: true);
                var now = DateTime.Now;
                int leadMinutes = SettingsService.ReminderLeadMinutes;

                // Prevent unbounded growth across long sessions
                lock (_lock)
                {
                    if (_notifiedKeys.Count > 1000)
                        _notifiedKeys.Clear();
                }

                foreach (var task in tasks)
                {
                    if (!task.IsEnabled || !task.NextRunTime.HasValue)
                        continue;

                    var nextRun = task.NextRunTime.Value;
                    var timeUntil = nextRun - now;

                    // Only notify if within the lead window and still in the future
                    if (timeUntil.TotalMinutes < 0 || timeUntil.TotalMinutes > leadMinutes)
                        continue;

                    // Key is unique per task + scheduled run time — prevents duplicate toasts
                    string key = $"{task.Path}|{nextRun:O}";
                    bool alreadyNotified;
                    lock (_lock)
                    {
                        alreadyNotified = !_notifiedKeys.Add(key);
                    }

                    if (!alreadyNotified)
                    {
                        int minutes = (int)Math.Ceiling(timeUntil.TotalMinutes);
                        NotificationService.ShowUpcomingTask(task.Name, minutes);
                        LogService.Info($"Reminder sent for task '{task.Name}' (runs in ~{minutes} min).");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("ReminderService check failed", ex);
            }
        }
    }
}
