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
    /// <summary>Task Scheduler event-log access: history, run records, system events, and discovery of tasks that cannot be enumerated directly.</summary>
    public class TaskEventLogService : ITaskEventLogService
    {
        private readonly TaskSchedulerConnection _connection;
        public TaskEventLogService(TaskSchedulerConnection connection) { _connection = connection; }

        private List<ScheduledTaskModel>? _discoveredCache;

        /// <summary>Drops the event-log task-discovery cache so the next GetFolderStructure() call
        /// re-scans instead of reusing a snapshot that predates a task being added/removed.</summary>
        public void InvalidateDiscoveredCache() => _discoveredCache = null;

        public List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false)
        {
            if (_discoveredCache != null && !forceRefresh) return _discoveredCache;

            var discoveredRaw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Event IDs for task activity: 100 (started), 102 (completed), 107 (triggered), 110 (registered)
                string query = "*[System[(EventID=100 or EventID=102 or EventID=107 or EventID=110)]]";
                EventLogQuery eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                using EventLogReader logReader = new EventLogReader(eventsQuery);

                EventRecord record;
                // Limit to last 2000 events to ensure older infrequent tasks are caught
                int count = 0;
                while (count < 2000 && (record = logReader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        try
                        {
                            // In TaskScheduler Operational log, Data[0] (or similar) usually contains the TaskName
                            // We look through all properties for anything starting with \ (task path)
                            // This is language-independent.
                            foreach (var prop in record.Properties)
                            {
                                if (prop.Value is string s && s.StartsWith("\\") && s.Length > 1)
                                {
                                    discoveredRaw.Add(s.Trim());
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    count++;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("{Message}", $"Could not discover tasks via event log: {ex.Message}");
            }

            var discoveredTasks = new List<ScheduledTaskModel>();
            // One TaskService for the whole pass — opening a fresh COM connection per discovered
            // path here was the single biggest cost of a folder-tree refresh (see 3.5).
            using var lookupTs = _connection.Open();
            foreach (var path in discoveredRaw)
            {
                bool added = false;
                try
                {
                    var task = lookupTs.GetTask(path);
                    if (task != null)
                    {
                        var model = TaskModelMapper.MapTaskToModel(task);
                        model.IsFromEventLog = true;
                        discoveredTasks.Add(model);
                        added = true;
                    }
                }
                catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
                {
                    // Handled below if !added
                }
                catch { }

                if (!added)
                {
                    // Definitive Fallback: If we saw it in the logs, it exists. 
                    // Create a placeholder even if TaskService fails to find/load it.
                    discoveredTasks.Add(new ScheduledTaskModel
                    {
                        Name = System.IO.Path.GetFileName(path),
                        Path = path,
                        IsFromEventLog = true,
                        IsReadOnlyFallback = true,
                        State = AppTaskState.AccessDenied,
                        Description = "This task was discovered via event logs but is highly protected. Access is restricted for non-administrator users."
                    });
                }
            }
            _discoveredCache = discoveredTasks;
            return discoveredTasks;
        }

        public List<TaskHistoryEntry> GetTaskHistory(string taskPath)
        {
            var history = new List<TaskHistoryEntry>();
            try
            {
                string query = $"*[System/Provider[@Name='Microsoft-Windows-TaskScheduler'] and EventData[Data[@Name='TaskName']={TaskDefinitionBuilder.ToXPathLiteral(taskPath)}]]";
                EventLogQuery eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                using EventLogReader logReader = new EventLogReader(eventsQuery);

                EventRecord record;
                while ((record = logReader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        history.Add(new TaskHistoryEntry
                        {
                            Time = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown",
                            Result = GetEventResult(record.Id),
                            ExitCode = GetEventExitCode(record),
                            Message = BuildEventMessage(record),
                            EventId = record.Id,
                            ActivityId = record.ActivityId,
                            User = GetUserFromRecord(record),
                            TaskPath = taskPath,
                            TaskName = System.IO.Path.GetFileName(taskPath),
                            Level = GetLevelName(record),
                            Keywords = record.KeywordsDisplayNames != null ? string.Join(", ", record.KeywordsDisplayNames) : "None",
                            Computer = record.MachineName ?? "Local",
                            TaskCategory = GetEventResult(record.Id),
                            OpCode = GetLevelName(record)
                        });
                    }
                }
                history = history.OrderByDescending(h => h.Time).ToList();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("{Message}", $"Could not read task history: {ex.Message}");
            }
            return history;
        }

        /// <summary>
        /// Returns the actual Task Scheduler engine PID for each currently-running task, keyed by
        /// task path. Used instead of matching processes by image name — a name match (e.g.
        /// "powershell.exe") can hit any unrelated process on the machine, not the one this task
        /// actually started (see item 2.10).
        /// </summary>
        public Dictionary<string, int> GetRunningTaskEnginePids()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var ts = _connection.Open();
                foreach (RunningTask rt in ts.GetRunningTasks(true))
                {
                    try { result[rt.Path] = (int)rt.EnginePID; }
                    catch { /* task may have just finished; PID no longer available */ }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not enumerate running task engine PIDs: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Reads every task start/completion record from the Task Scheduler operational log within
        /// <paramref name="window"/>, in one pass. Parses the raw event XML instead of calling
        /// <c>FormatDescription()</c> per record, which keeps a full 7-day read responsive.
        /// </summary>
        public List<TaskRunRecord> GetRecentRunRecords(TimeSpan window)
        {
            var records = new List<TaskRunRecord>();
            long ms = (long)Math.Max(window.TotalMilliseconds, 60_000);

            try
            {
                // Note: EventLogQuery takes raw XPath here, so "<=" must NOT be XML-escaped —
                // "&lt;=" makes the Event Log service reject the query as invalid.
                string query =
                    "*[System[(EventID=100 or EventID=102 or EventID=103 or EventID=201 or EventID=203) " +
                    $"and TimeCreated[timediff(@SystemTime) <= {ms}]]]";

                var eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                using EventLogReader logReader = new EventLogReader(eventsQuery);

                EventRecord? record;
                while ((record = logReader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        var parsed = ParseRunRecord(record);
                        if (parsed != null) records.Add(parsed);
                    }
                }
            }
            catch (EventLogNotFoundException ex)
            {
                Serilog.Log.Error(ex, "{Message}", "The Task Scheduler operational log is not available; dashboard analytics will be empty.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Access denied reading the Task Scheduler operational log for dashboard analytics.");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Failed to read recent task run records.");
            }

            return records;
        }

        /// <summary>Maps one operational-log event onto a <see cref="TaskRunRecord"/>, or null if it carries no task name.</summary>
        private TaskRunRecord? ParseRunRecord(EventRecord record)
        {
            try
            {
                var data = ReadEventData(record);
                if (!data.TryGetValue("TaskName", out var taskName) || string.IsNullOrWhiteSpace(taskName))
                    return null;

                long? resultCode = null;
                if (data.TryGetValue("ResultCode", out var rc) && long.TryParse(rc, out long parsedRc))
                    resultCode = parsedRc;

                // Events 100/102 name the field "InstanceId"; event 201 names it "TaskInstanceId".
                string instanceId = data.TryGetValue("InstanceId", out var iid) ? iid
                                  : data.TryGetValue("TaskInstanceId", out var tiid) ? tiid
                                  : "";

                return new TaskRunRecord
                {
                    TaskPath = taskName,
                    TaskName = System.IO.Path.GetFileName(taskName),
                    Time = record.TimeCreated ?? DateTime.Now,
                    EventId = record.Id,
                    InstanceId = instanceId,
                    ResultCode = resultCode
                };
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Skipping unreadable Task Scheduler event record: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Pulls the <c>&lt;EventData&gt;/&lt;Data Name="..."&gt;</c> pairs out of an event.
        /// Name-based lookup keeps this stable across schema/locale differences, unlike positional
        /// <c>record.Properties</c> access.
        /// </summary>
        internal static Dictionary<string, string> ReadEventData(EventRecord record)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(record.ToXml());
                foreach (var element in doc.Descendants())
                {
                    if (!string.Equals(element.Name.LocalName, "Data", StringComparison.Ordinal)) continue;
                    var nameAttr = element.Attribute("Name");
                    if (nameAttr == null) continue;
                    result[nameAttr.Value] = element.Value;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not parse event XML for record {record.Id}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Severity name derived from the numeric level rather than <c>LevelDisplayName</c>, which
        /// Windows renders in the OS display language (e.g. "Informationen" on a German install).
        /// </summary>
        private string GetLevelName(EventRecord record)
        {
            byte? level = record.Level;
            return level switch
            {
                1 => LocalizationService.GetString("EventLevel.Critical", "Critical"),
                2 => LocalizationService.GetString("EventLevel.Error", "Error"),
                3 => LocalizationService.GetString("EventLevel.Warning", "Warning"),
                4 => LocalizationService.GetString("EventLevel.Information", "Information"),
                5 => LocalizationService.GetString("EventLevel.Verbose", "Verbose"),
                _ => LocalizationService.GetString("EventLevel.Information", "Information")
            };
        }

        /// <summary>
        /// Builds a description from the event's own data fields for the events we understand, so
        /// the history reads in the app's language. Falls back to the Windows-rendered description
        /// (OS language) only for events we have no template for.
        /// </summary>
        private string BuildEventMessage(EventRecord record)
        {
            try
            {
                var data = ReadEventData(record);
                data.TryGetValue("TaskName", out var taskName);
                data.TryGetValue("ActionName", out var actionName);
                data.TryGetValue("UserContext", out var userContext);
                data.TryGetValue("ResultCode", out var resultCode);

                string name = taskName ?? "";
                string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

                switch (record.Id)
                {
                    case 100:
                        return string.Format(L("EventMsg.100", "Task Scheduler started an instance of task \"{0}\" for user \"{1}\"."), name, userContext ?? "");
                    case 102:
                        return string.Format(L("EventMsg.102", "Task Scheduler successfully finished task \"{0}\"."), name);
                    case 103:
                        return string.Format(L("EventMsg.103", "Task Scheduler failed to start an instance of task \"{0}\" (error {1})."), name, FormatCode(resultCode));
                    case 106:
                        return string.Format(L("EventMsg.106", "User \"{0}\" registered task \"{1}\"."), userContext ?? "", name);
                    case 107:
                        return string.Format(L("EventMsg.107", "Task Scheduler launched task \"{0}\" from a time trigger."), name);
                    case 110:
                        return string.Format(L("EventMsg.110", "Task Scheduler launched task \"{0}\" for user \"{1}\"."), name, userContext ?? "");
                    case 111:
                        return string.Format(L("EventMsg.111", "Task Scheduler terminated task \"{0}\" because it exceeded its configured time limit."), name);
                    case 129:
                        return string.Format(L("EventMsg.129", "Task Scheduler launched action \"{0}\" of task \"{1}\"."), actionName ?? "", name);
                    case 200:
                        return string.Format(L("EventMsg.200", "Task Scheduler launched action \"{0}\" of task \"{1}\"."), actionName ?? "", name);
                    case 201:
                        return string.Format(L("EventMsg.201", "Task Scheduler completed action \"{0}\" of task \"{1}\" with return code {2}."), actionName ?? "", name, FormatCode(resultCode));
                    case 203:
                        return string.Format(L("EventMsg.203", "Task Scheduler failed to launch action \"{0}\" of task \"{1}\" (error {2})."), actionName ?? "", name, FormatCode(resultCode));
                    case 322:
                        return string.Format(L("EventMsg.322", "Task Scheduler did not launch task \"{0}\" because an instance is already running."), name);
                    case 108:
                        return string.Format(L("EventMsg.108", "Task Scheduler failed to start task \"{0}\" (error {1})."), name, FormatCode(resultCode));
                    case 118:
                        return string.Format(L("EventMsg.118", "Task Scheduler launched task \"{0}\" from a boot trigger."), name);
                    case 119:
                        return string.Format(L("EventMsg.119", "Task Scheduler launched task \"{0}\" from a logon trigger."), name);
                    case 140:
                        return string.Format(L("EventMsg.140", "User \"{0}\" updated the definition of task \"{1}\"."), userContext ?? "", name);
                    case 141:
                        return string.Format(L("EventMsg.141", "User \"{0}\" deleted task \"{1}\"."), userContext ?? "", name);
                    case 142:
                        return string.Format(L("EventMsg.142", "User \"{0}\" disabled task \"{1}\"."), userContext ?? "", name);
                    case 153:
                        return string.Format(L("EventMsg.153", "Task Scheduler did not start task \"{0}\" because the schedule was missed. Enable \"Run task as soon as possible after a scheduled start is missed\" to catch up."), name);
                    case 202:
                        return string.Format(L("EventMsg.202", "Action \"{0}\" of task \"{1}\" failed with return code {2}."), actionName ?? "", name, FormatCode(resultCode));
                    case 329:
                        return string.Format(L("EventMsg.329", "Task Scheduler stopped task \"{0}\" because it exceeded its configured time limit."), name);
                    case 331:
                        return string.Format(L("EventMsg.331", "Task Scheduler stopped task \"{0}\" because the computer switched to battery power."), name);
                    case 332:
                        return string.Format(L("EventMsg.332", "Task Scheduler did not launch task \"{0}\" because the required conditions (idle, network or power) were not met."), name);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not build a localized message for event {record.Id}: {ex.Message}");
            }

            // Unknown event: Windows renders this in the OS display language.
            try { return record.FormatDescription() ?? ""; }
            catch { return ""; }
        }

        /// <summary>Renders a raw result code the way Task Scheduler does: 0, otherwise hex.</summary>
        private static string FormatCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "-";
            if (!long.TryParse(raw, out long value)) return raw;
            return value == 0 ? "0" : "0x" + ((uint)value).ToString("X8");
        }

        /// <summary>Looks up logon/logoff/shutdown/reboot/sleep events from the Windows "System" log
        /// within +/- <paramref name="window"/> of <paramref name="centerTime"/>, so a task run can be
        /// correlated with "was the machine restarted or the user logged off around this time?".</summary>
        public List<SystemEventEntry> GetSystemEventsNear(DateTime centerTime, TimeSpan window)
        {
            var events = new List<SystemEventEntry>();
            try
            {
                DateTime start = centerTime - window;
                DateTime end = centerTime + window;
                string startIso = start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                string endIso = end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // 6005/6006/6008: EventLog service started/stopped/unexpected shutdown
                // 1074: restart/shutdown initiated by a user or process (User32)
                // 41: system rebooted without a clean shutdown (Kernel-Power)
                // 42/107: entering sleep / resumed from sleep (Kernel-Power)
                // 7001/7002: user logon/logoff (Winlogon)
                string query = $"*[System[TimeCreated[@SystemTime>='{startIso}'] and TimeCreated[@SystemTime<='{endIso}'] " +
                                "and (EventID=6005 or EventID=6006 or EventID=6008 or EventID=1074 or EventID=41 or EventID=42 or EventID=107 or EventID=7001 or EventID=7002)]]";

                var eventsQuery = new EventLogQuery("System", PathType.LogName, query);
                using var logReader = new EventLogReader(eventsQuery);

                EventRecord record;
                while ((record = logReader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        events.Add(new SystemEventEntry
                        {
                            Time = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown",
                            EventId = record.Id,
                            EventType = GetSystemEventType(record.Id),
                            Description = GetSystemEventDescription(record.Id)
                        });
                    }
                }
                events = events.OrderBy(ev => ev.Time).ToList();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not read nearby system events: {ex.Message}");
            }
            return events;
        }

        private string GetSystemEventType(int eventId) => eventId switch
        {
            6005 => "System Startup",
            6006 => "Clean Shutdown",
            6008 => "Unexpected Shutdown",
            1074 => "Restart/Shutdown Requested",
            41 => "Unclean Reboot",
            42 => "Sleep",
            107 => "Resume From Sleep",
            7001 => "User Logon",
            7002 => "User Logoff",
            _ => $"Event {eventId}"
        };

        private string GetSystemEventDescription(int eventId) => eventId switch
        {
            6005 => "The Event Log service started (system booted up).",
            6006 => "The Event Log service stopped (clean system shutdown).",
            6008 => "The previous system shutdown was unexpected.",
            1074 => "A restart or shutdown was requested by a user or process.",
            41 => "The system rebooted without a clean shutdown (e.g. power loss or crash).",
            42 => "The system entered sleep/standby.",
            107 => "The system resumed from sleep/standby.",
            7001 => "A user logged on to the system.",
            7002 => "A user logged off the system.",
            _ => "System event."
        };


        private string GetEventResult(int eventId) => eventId switch
        {
            100 => LocalizationService.GetString("EventResult.100", "Task Started"),
            102 => LocalizationService.GetString("EventResult.102", "Task Completed"),
            103 => LocalizationService.GetString("EventResult.103", "Task Failed"),
            106 => LocalizationService.GetString("EventResult.106", "Task Registered"),
            108 => LocalizationService.GetString("EventResult.108", "Start Failed"),
            118 => LocalizationService.GetString("EventResult.118", "Boot Trigger"),
            119 => LocalizationService.GetString("EventResult.119", "Logon Trigger"),
            140 => LocalizationService.GetString("EventResult.140", "Task Updated"),
            141 => LocalizationService.GetString("EventResult.141", "Task Deleted"),
            142 => LocalizationService.GetString("EventResult.142", "Task Disabled"),
            153 => LocalizationService.GetString("EventResult.153", "Schedule Missed"),
            202 => LocalizationService.GetString("EventResult.202", "Action Failed"),
            329 => LocalizationService.GetString("EventResult.329", "Stopped (time limit)"),
            331 => LocalizationService.GetString("EventResult.331", "Stopped (on battery)"),
            332 => LocalizationService.GetString("EventResult.332", "Skipped (conditions not met)"),
            107 => LocalizationService.GetString("EventResult.107", "Task Triggered"),
            110 => LocalizationService.GetString("EventResult.110", "Task Launched"),
            111 => LocalizationService.GetString("EventResult.111", "Task Terminated"),
            129 => LocalizationService.GetString("EventResult.129", "Action Started"),
            200 => LocalizationService.GetString("EventResult.200", "Action Started"),
            201 => LocalizationService.GetString("EventResult.201", "Action Completed"),
            203 => LocalizationService.GetString("EventResult.203", "Action Launch Failed"),
            322 => LocalizationService.GetString("EventResult.322", "Launch Skipped (already running)"),
            _ => string.Format(LocalizationService.GetString("EventResult.Unknown", "Event {0}"), eventId)
        };

        /// <summary>
        /// Reads the event's own "ResultCode" data field by name rather than walking
        /// <see cref="EventRecord.Properties"/> and guessing — the old code returned the first
        /// non-zero int property, which could be any field of the event (PID, instance id, etc.),
        /// not necessarily the exit code (see item 2.11).
        /// </summary>
        private string GetEventExitCode(EventRecord record)
        {
            try
            {
                var data = ReadEventData(record);
                if (data.TryGetValue("ResultCode", out var rc) && long.TryParse(rc, out long value))
                    return value == 0 ? "0" : "0x" + ((uint)value).ToString("X8");
                return "0";
            }
            catch { return "-"; }
        }

        private string GetUserFromRecord(EventRecord record)
        {
            try
            {
                var userId = record.UserId;
                if (userId == null) return "";
                return userId.Translate(typeof(NTAccount)).ToString();
            }
            catch { return record.UserId?.ToString() ?? ""; }
        }
    }
}
