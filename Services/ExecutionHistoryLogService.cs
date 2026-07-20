using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text.Json;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Optionally mirrors Task Scheduler execution events into a durable, app-owned JSON-lines log
    /// (independent of the Windows "Microsoft-Windows-TaskScheduler/Operational" event log, which has
    /// limited retention and may be disabled). Only active while the app is running and the
    /// corresponding setting is enabled.
    /// </summary>
    public static class ExecutionHistoryLogService
    {
        public static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "ExecutionHistory");

        private static readonly object _lock = new();
        private static EventLogWatcher? _watcher;

        public static bool IsRunning => _watcher != null;

        public static void Start()
        {
            Stop();
            if (!SettingsService.EnableExecutionHistoryLog) return;

            try
            {
                var query = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName);
                _watcher = new EventLogWatcher(query);
                _watcher.EventRecordWritten += OnEventRecordWritten;
                _watcher.Enabled = true;
                LogService.Info("ExecutionHistoryLogService started.");
            }
            catch (Exception ex)
            {
                LogService.Warn($"Could not start execution history log watcher: {ex.Message}");
                _watcher = null;
            }
        }

        public static void Stop()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.Enabled = false;
                _watcher.EventRecordWritten -= OnEventRecordWritten;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }

        private static void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
        {
            if (!SettingsService.EnableExecutionHistoryLog) return;
            var record = e.EventRecord;
            if (record == null) return;

            try
            {
                using (record)
                {
                    string taskName = "";
                    foreach (var prop in record.Properties)
                    {
                        if (prop.Value is string s && s.StartsWith("\\") && s.Length > 1)
                        {
                            taskName = s.Trim();
                            break;
                        }
                    }

                    var line = JsonSerializer.Serialize(new
                    {
                        Time = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        EventId = record.Id,
                        Result = GetEventResult(record.Id),
                        TaskName = taskName,
                        Level = record.LevelDisplayName ?? "Information",
                        Message = record.FormatDescription() ?? ""
                    });

                    Append(line);
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to record execution history event: {ex.Message}");
            }
        }

        private static string GetEventResult(int eventId) => eventId switch
        {
            100 => "Task Started",
            102 => "Task Completed",
            103 => "Task Failed",
            107 => "Task Triggered",
            110 => "Task Registered",
            129 => "Action Started",
            201 => "Action Completed",
            _ => $"Event {eventId}"
        };

        private static void Append(string jsonLine)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogFolder))
                        Directory.CreateDirectory(LogFolder);

                    string path = Path.Combine(LogFolder, $"history-{DateTime.Now:yyyy-MM}.jsonl");
                    File.AppendAllText(path, jsonLine + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void OpenLogFolder()
        {
            try
            {
                if (!Directory.Exists(LogFolder))
                    Directory.CreateDirectory(LogFolder);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LogFolder,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
