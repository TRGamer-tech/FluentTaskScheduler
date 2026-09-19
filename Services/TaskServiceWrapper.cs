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

    /// <summary>
    /// The subset of <see cref="TaskServiceWrapper"/> that other services depend on, extracted so
    /// SnoozeService/TaskPipelineService logic can be unit tested against a fake implementation
    /// without a real Task Scheduler.
    /// </summary>
    public interface ITaskServiceWrapper
    {
        List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true);
        bool TaskExists(string path);
        void EnableTask(string path);
        void DisableTask(string path);
        void SetTaskEnabled(string path, bool enabled);
        void RunTask(string path);
        void RunTask(string path, string origin);
    }

    public class TaskServiceWrapper : ITaskService
    {
        public List<ScheduledTaskModel> GetAllTasks(string? folderPath = null, bool recursive = true)
        {
            var tasks = new List<ScheduledTaskModel>();
            using (var ts = new TaskService())
            {
                var folder = ts.GetFolder(folderPath ?? "\\");
                if (folder != null)
                {
                    EnumFolderTasks(folder, tasks, recursive);
                }
            }

            // Merge discovered tasks from event log if they aren't already present
            var discovered = DiscoverTasksFromEventLog();
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
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                if (task == null) return null;
                return MapTaskToModel(task);
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

                    tasks.Add(MapTaskToModel(task));
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error("{Message}", $"Could not read task '{task.Name}': {ex.Message}");
                }
            }
        }

        private ScheduledTaskModel MapTaskToModel(Microsoft.Win32.TaskScheduler.Task task)
        {
            var def = task.Definition;
            var model = new ScheduledTaskModel
            {
                Name = task.Name,
                Path = task.Path,
                State = MapTaskState(task.State),
                IsEnabled = task.Enabled,
                LastRunTime = task.LastRunTime == DateTime.MinValue ? null : (DateTime?)task.LastRunTime,
                NextRunTime = task.NextRunTime == DateTime.MinValue ? null : (DateTime?)task.NextRunTime,
                Author = def.RegistrationInfo.Author ?? "",
                Description = def.RegistrationInfo.Description ?? "",
                IsHidden = def.Settings.Hidden,
                RunWithHighestPrivileges = def.Principal.RunLevel == TaskRunLevel.Highest,
                Triggers = def.Triggers != null
                    ? string.Join(", ", def.Triggers.Cast<Trigger>().Select(t => t.ToString()))
                    : ""
            };

            // Parse Metadata from Description
            ParseMetadata(model);

            // Map Actions
            var unsupported = new List<string>();
            if (def.Actions != null)
            {
                foreach (var action in def.Actions)
                {
                    if (action is ExecAction execAction)
                    {
                        model.Actions.Add(new TaskActionModel
                        {
                            Command = execAction.Path,
                            Arguments = execAction.Arguments,
                            WorkingDirectory = execAction.WorkingDirectory
                        });
                    }
                    else
                    {
                        // Email/COM/show-message actions have no model representation. Round-tripping
                        // this task through the editor would silently drop them, so it's flagged as
                        // unsupported instead (see HasUnsupportedElements).
                        unsupported.Add($"{action.ActionType} action");
                    }
                }
            }

            // Map Triggers
            if (def.Triggers != null)
            {
                foreach (var trigger in def.Triggers)
                {
                    var mapped = MapTriggerToModel(trigger);
                    if (mapped.TriggerType == AppTriggerType.Unsupported)
                    {
                        unsupported.Add($"{trigger.GetType().Name} trigger");
                    }
                    model.TriggersList.Add(mapped);
                }
                // Update display string using wrapper descriptors
                model.Triggers = string.Join(", ", model.TriggersList.Select(t => t.Descriptor));
            }

            if (unsupported.Count > 0)
            {
                model.HasUnsupportedElements = true;
                model.UnsupportedElementsDescription = string.Join(", ", unsupported);
            }

            // Map Settings
            MapSettingsToModel(def.Settings, model);

            // Verify SessionStateChange triggers (Workaround for library deserialization issue)
            FixSessionStateTriggers(task, model);

            // Map User Context
            if (def.Principal != null)
            {
                if (def.Principal.LogonType == TaskLogonType.ServiceAccount && def.Principal.UserId == "SYSTEM")
                {
                    model.RunAsSystem = true;
                }
                else if (!string.IsNullOrWhiteSpace(def.Principal.UserId) && def.Principal.UserId != "SYSTEM")
                {
                    model.RunAsUser = def.Principal.UserId;
                }
            }

            return model;
        }

        private static AppTaskState MapTaskState(Microsoft.Win32.TaskScheduler.TaskState state) => state switch
        {
            Microsoft.Win32.TaskScheduler.TaskState.Disabled => AppTaskState.Disabled,
            Microsoft.Win32.TaskScheduler.TaskState.Queued => AppTaskState.Queued,
            Microsoft.Win32.TaskScheduler.TaskState.Ready => AppTaskState.Ready,
            Microsoft.Win32.TaskScheduler.TaskState.Running => AppTaskState.Running,
            _ => AppTaskState.Unknown
        };

        private void MapSettingsToModel(TaskSettings settings, ScheduledTaskModel model)
        {
            if (settings == null) return;

            model.OnlyIfIdle = settings.RunOnlyIfIdle;
            try { model.IdleDuration = System.Xml.XmlConvert.ToString(settings.IdleSettings.IdleDuration); } catch { }
            model.StopOnIdleEnd = settings.IdleSettings.StopOnIdleEnd;

            model.OnlyIfAC = settings.DisallowStartIfOnBatteries;
            model.DisallowStartOnBatteries = settings.DisallowStartIfOnBatteries;
            model.StopOnBattery = settings.StopIfGoingOnBatteries;

            model.OnlyIfNetwork = settings.RunOnlyIfNetworkAvailable;
            try
            {
                if (settings.NetworkSettings != null && settings.NetworkSettings.Id != Guid.Empty)
                {
                    model.NetworkId = settings.NetworkSettings.Id.ToString();
                    try { model.NetworkName = settings.NetworkSettings.Name ?? ""; } catch { }
                }
            }
            catch { }

            model.WakeToRun = settings.WakeToRun;
            model.RunIfMissed = settings.StartWhenAvailable;
            model.RestartOnFailure = settings.RestartCount > 0;
            model.RestartCount = settings.RestartCount;

            model.MultipleInstancesPolicy = settings.MultipleInstances;

            model.TaskPriority = (int)settings.Priority;
            model.DeleteExpiredTaskAfter = settings.DeleteExpiredTaskAfter != TimeSpan.Zero;
            model.AllowHardTerminate = settings.AllowHardTerminate;

            if (settings.ExecutionTimeLimit != TimeSpan.Zero)
            {
                try { model.StopIfRunsLongerThan = System.Xml.XmlConvert.ToString(settings.ExecutionTimeLimit); } catch { }
            }

            if (settings.RestartInterval != TimeSpan.Zero)
            {
                try { model.RestartInterval = System.Xml.XmlConvert.ToString(settings.RestartInterval); } catch { }
            }
        }

        private void FixSessionStateTriggers(Microsoft.Win32.TaskScheduler.Task task, ScheduledTaskModel model)
        {
            if (model.TriggersList.Any(t => t.TriggerType == AppTriggerType.SessionStateChange))
            {
                try
                {
                    // The library sometimes defaults to "Lock" if it can't parse the state.
                    // We check the raw XML for truth logic.
                    string rawXml = task.Xml;
                    var match = Regex.Match(rawXml, "<StateChange>(.*)</StateChange>");
                    if (match.Success)
                    {
                        string state = match.Groups[1].Value.Trim();
                        foreach (var t in model.TriggersList.Where(t => t.TriggerType == AppTriggerType.SessionStateChange))
                        {
                            t.SessionStateChangeType = state switch
                            {
                                "SessionLock" => "Lock",
                                "SessionUnlock" => "Unlock",
                                "RemoteConnect" => "RemoteConnect",
                                "RemoteDisconnect" => "RemoteDisconnect",
                                "ConsoleConnect" => "Unlock",
                                "ConsoleDisconnect" => "Lock",
                                _ => "Lock"
                            };
                        }
                    }
                }
                catch { }
            }
        }

        public bool TaskExists(string path)
        {
            using var ts = new TaskService();
            return ts.GetTask(path) != null;
        }

        public void EnableTask(string path) => SetTaskEnabled(path, true);
        public void DisableTask(string path) => SetTaskEnabled(path, false);

        public void SetTaskEnabled(string path, bool enabled)
        {
            try
            {
                using (var ts = new TaskService())
                {
                    var task = ts.GetTask(path);
                    if (task != null) task.Enabled = enabled;
                }
            }
            catch (Exception ex) when (IsAccessDenied(ex))
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
                using (var ts = new TaskService())
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
                var level = IsAccessDenied(ex) ? "Access denied" : "Error";
                Serilog.Log.Error("{Message}", $"{level}: Cannot run task '{path}': {ex.Message}");
                NotificationService.ShowTaskError(System.IO.Path.GetFileName(path), ex.Message);
                throw;
            }
        }

        public void StopTask(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                task?.Stop();
            }
        }

        public void DeleteTask(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                if (task != null) task.Folder.DeleteTask(task.Name);
            }
            InvalidateDiscoveredCache();
        }

        public void RegisterTask(string folderPath, ScheduledTaskModel model)
        {
            using (var ts = new TaskService())
            {
                TaskDefinition td = ts.NewTask();
                ConfigureTaskDefinition(td, model);

                TaskFolder targetFolder = GetOrCreateFolder(ts, folderPath);

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
                    if (IsAccessDenied(ex))
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
            InvalidateDiscoveredCache();
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

        private void ConfigureTaskDefinition(TaskDefinition td, ScheduledTaskModel model)
        {
            td.RegistrationInfo.Description = UpdateDescriptionWithMetadata(model.Description, model.Category, model.Tags.ToList(), model.Pipeline);
            td.RegistrationInfo.Author = model.Author;
            td.Settings.Enabled = model.IsEnabled;
            td.Settings.Hidden = model.IsHidden;
            td.Principal.RunLevel = model.RunWithHighestPrivileges ? TaskRunLevel.Highest : TaskRunLevel.LUA;

            foreach (var triggerModel in model.TriggersList)
            {
                ConfigureTrigger(td, triggerModel, model);
            }

            foreach (var act in model.Actions)
            {
                if (!string.IsNullOrWhiteSpace(act.Command))
                {
                    td.Actions.Add(new ExecAction(act.Command, act.Arguments, act.WorkingDirectory));
                }
            }

            if (td.Actions.Count == 0)
            {
                // A task with no actions does nothing when it runs — refuse to save it instead of
                // silently substituting a notepad.exe placeholder the user never asked for.
                throw new InvalidOperationException("This task has no actions. Add at least one action before saving.");
            }

            // Apply Settings
            td.Settings.RunOnlyIfIdle = model.OnlyIfIdle;
            if (!string.IsNullOrWhiteSpace(model.IdleDuration))
            {
                // Accepts both ISO-8601 ("PT10M") and the shorthand ("10m") the placeholder text
                // advertises — previously only the strict ISO form parsed, so shorthand input was
                // silently discarded (item 2.3).
                if (DurationUtil.TryParseFlexibleDuration(model.IdleDuration, out var idleDuration))
                    td.Settings.IdleSettings.IdleDuration = idleDuration;
                else
                    Serilog.Log.Warning("{Message}", $"Invalid IdleDuration '{model.IdleDuration}' for task '{model.Name}'; leaving the previous idle duration in place.");
            }
            td.Settings.IdleSettings.StopOnIdleEnd = model.StopOnIdleEnd;
            td.Settings.DisallowStartIfOnBatteries = model.DisallowStartOnBatteries || model.OnlyIfAC;
            td.Settings.StopIfGoingOnBatteries = model.StopOnBattery;

            bool hasNetworkId = !string.IsNullOrWhiteSpace(model.NetworkId);
            td.Settings.RunOnlyIfNetworkAvailable = model.OnlyIfNetwork || hasNetworkId;
            if (hasNetworkId)
            {
                try 
                { 
                    td.Settings.NetworkSettings.Id = Guid.Parse(model.NetworkId);
                    if (!string.IsNullOrWhiteSpace(model.NetworkName) && model.NetworkName != "Any network")
                        td.Settings.NetworkSettings.Name = model.NetworkName;
                } 
                catch (Exception ex) { Serilog.Log.Warning("{Message}", $"Could not set NetworkSettings: {ex.Message}"); }
            }

            td.Settings.WakeToRun = model.WakeToRun;
            td.Settings.StartWhenAvailable = model.RunIfMissed;

            if (model.RestartOnFailure)
            {
                TimeSpan restartInterval;
                if (string.IsNullOrWhiteSpace(model.RestartInterval))
                {
                    restartInterval = TimeSpan.FromMinutes(1);
                }
                else if (!DurationUtil.TryParseFlexibleDuration(model.RestartInterval, out restartInterval))
                {
                    Serilog.Log.Warning("{Message}", $"Invalid RestartInterval '{model.RestartInterval}' for task '{model.Name}'; defaulting to 1 minute instead of dropping the restart-on-failure policy.");
                    restartInterval = TimeSpan.FromMinutes(1);
                }
                td.Settings.RestartInterval = restartInterval;
                td.Settings.RestartCount = model.RestartCount;
            }

            if (!string.IsNullOrWhiteSpace(model.StopIfRunsLongerThan))
            {
                try { td.Settings.ExecutionTimeLimit = System.Xml.XmlConvert.ToTimeSpan(model.StopIfRunsLongerThan); }
                catch { td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72); }
            }
            else
            {
                // Explicitly unlimited when the user unchecked "Stop task if runs longer than" -
                // otherwise the task definition's own built-in default (72h) would silently apply.
                td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            }

            td.Settings.MultipleInstances = model.MultipleInstancesPolicy;

            td.Settings.Priority = model.TaskPriority switch
            {
                0 => System.Diagnostics.ProcessPriorityClass.RealTime,
                1 => System.Diagnostics.ProcessPriorityClass.High,
                2 => System.Diagnostics.ProcessPriorityClass.AboveNormal,
                3 => System.Diagnostics.ProcessPriorityClass.AboveNormal,
                4 => System.Diagnostics.ProcessPriorityClass.Normal,
                5 => System.Diagnostics.ProcessPriorityClass.Normal,
                6 => System.Diagnostics.ProcessPriorityClass.Normal,
                7 => System.Diagnostics.ProcessPriorityClass.BelowNormal,
                8 => System.Diagnostics.ProcessPriorityClass.BelowNormal,
                9 => System.Diagnostics.ProcessPriorityClass.Idle,
                10 => System.Diagnostics.ProcessPriorityClass.Idle,
                _ => System.Diagnostics.ProcessPriorityClass.Normal
            };

            td.Settings.DeleteExpiredTaskAfter = model.DeleteExpiredTaskAfter ? TimeSpan.FromDays(30) : TimeSpan.Zero;
            td.Settings.AllowHardTerminate = model.AllowHardTerminate;
        }

        private void ConfigureTrigger(TaskDefinition td, TaskTriggerModel triggerModel, ScheduledTaskModel model)
        {
            if (triggerModel.TriggerType == AppTriggerType.Unsupported)
            {
                // Should never be reachable — the editor refuses to open tasks containing one of
                // these (see ScheduledTaskModel.HasUnsupportedElements) — but guard against saving
                // over one anyway rather than silently turning it into a daily 9 AM trigger.
                throw new InvalidOperationException("This trigger type is not supported by the editor and cannot be saved.");
            }

            DateTime startTime = DateTime.Today.AddHours(9);
            if (!string.IsNullOrWhiteSpace(triggerModel.ScheduleInfo))
            {
                var parsedStart = DurationUtil.TryParseScheduleInfo(triggerModel.ScheduleInfo);
                if (parsedStart.HasValue)
                {
                    startTime = parsedStart.Value;
                }
                else
                {
                    throw new FormatException($"Could not parse trigger start time '{triggerModel.ScheduleInfo}'. Expected format: {DurationUtil.ScheduleInfoFormat}.");
                }
            }

            Trigger t = triggerModel.TriggerType switch
            {
                AppTriggerType.Daily => new DailyTrigger { StartBoundary = startTime, DaysInterval = triggerModel.DailyInterval },
                AppTriggerType.Weekly => new WeeklyTrigger { StartBoundary = startTime, WeeksInterval = triggerModel.WeeklyInterval, DaysOfWeek = GetDaysOfWeek(triggerModel.WeeklyDays) },
                AppTriggerType.Monthly => CreateMonthlyTrigger(triggerModel, startTime),
                AppTriggerType.AtLogon => new LogonTrigger(),
                AppTriggerType.AtStartup => new BootTrigger(),
                AppTriggerType.Once => new TimeTrigger { StartBoundary = startTime },
                AppTriggerType.Event => CreateEventTrigger(triggerModel),
                AppTriggerType.OnIdle => new IdleTrigger(),
                AppTriggerType.SessionStateChange => CreateSessionTrigger(triggerModel, model),
                _ => new DailyTrigger { StartBoundary = startTime }
            };

            if (triggerModel.ExpirationDate.HasValue)
            {
                t.EndBoundary = triggerModel.ExpirationDate.Value;
            }

            if (!string.IsNullOrWhiteSpace(triggerModel.RepetitionInterval))
            {
                try
                {
                    t.Repetition.Interval = System.Xml.XmlConvert.ToTimeSpan(triggerModel.RepetitionInterval);
                    if (!string.IsNullOrWhiteSpace(triggerModel.RepetitionDuration))
                    {
                        t.Repetition.Duration = System.Xml.XmlConvert.ToTimeSpan(triggerModel.RepetitionDuration);
                    }
                }
                catch { }
            }

            // RandomDelay applies independently of repetition — must not be nested inside the
            // RepetitionInterval check above, or a delay configured without repetition is dropped.
            if (!string.IsNullOrWhiteSpace(triggerModel.RandomDelay))
            {
                try { ((dynamic)t).RandomDelay = System.Xml.XmlConvert.ToTimeSpan(triggerModel.RandomDelay); } catch { }
            }

            td.Triggers.Add(t);
        }

        private Trigger CreateMonthlyTrigger(TaskTriggerModel model, DateTime startTime)
        {
            if (model.MonthlyIsDayOfWeek)
            {
                return new MonthlyDOWTrigger
                {
                    StartBoundary = startTime,
                    MonthsOfYear = GetMonths(model.MonthlyMonths),
                    DaysOfWeek = GetDayOfWeek(model.MonthlyDayOfWeek),
                    WeeksOfMonth = GetWhichWeek(model.MonthlyWeek)
                };
            }
            return new MonthlyTrigger
            {
                StartBoundary = startTime,
                MonthsOfYear = GetMonths(model.MonthlyMonths),
                DaysOfMonth = model.MonthlyDays.Where(d => d <= 31).ToArray(),
                RunOnLastDayOfMonth = model.MonthlyDays.Contains(32)
            };
        }

        private Trigger CreateEventTrigger(TaskTriggerModel model)
        {
            var et = new EventTrigger();
            string log = string.IsNullOrWhiteSpace(model.EventLog) ? "Application" : model.EventLog;
            if (log.Any(c => char.IsControl(c)))
                throw new ArgumentException("Event log name contains invalid control characters.", nameof(model.EventLog));
            if (!string.IsNullOrWhiteSpace(model.EventSource) && model.EventSource.Any(c => char.IsControl(c)))
                throw new ArgumentException("Event source contains invalid control characters.", nameof(model.EventSource));
            string query = "*";

            if (!string.IsNullOrWhiteSpace(model.EventSource) || model.EventId.HasValue)
            {
                string conditions = "";
                if (!string.IsNullOrWhiteSpace(model.EventSource))
                    conditions += $"Provider[@Name={ToXPathLiteral(model.EventSource)}]";

                if (model.EventId.HasValue)
                {
                    if (conditions.Length > 0) conditions += " and ";
                    conditions += $"(EventID={model.EventId})";
                }
                query = $"*[System[{conditions}]]";
            }

            // `query` is an XPath expression that itself becomes XML element content below, so it
            // needs XML-escaping on top of the XPath-literal escaping already applied above.
            string logXml = System.Security.SecurityElement.Escape(log);
            string queryXml = System.Security.SecurityElement.Escape(query);
            et.Subscription = $"<QueryList><Query Id=\"0\" Path=\"{logXml}\"><Select Path=\"{logXml}\">{queryXml}</Select></Query></QueryList>";
            return et;
        }

        /// <summary>
        /// Encodes a string as a safe XPath 1.0 string literal. XPath 1.0 has no escape sequence for
        /// quote characters inside a literal, so a value containing both ' and " has to be split
        /// across a concat() call.
        /// </summary>
        internal static string ToXPathLiteral(string value)
        {
            value ??= "";
            if (!value.Contains('\''))
                return $"'{value}'";
            if (!value.Contains('"'))
                return $"\"{value}\"";

            var parts = value.Split('\'');
            var sb = new System.Text.StringBuilder("concat(");
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(", \"'\", ");
                sb.Append('\'').Append(parts[i]).Append('\'');
            }
            sb.Append(')');
            return sb.ToString();
        }

        private Trigger CreateSessionTrigger(TaskTriggerModel triggerModel, ScheduledTaskModel model)
        {
            var sscTrigger = new SessionStateChangeTrigger();
            sscTrigger.StateChange = triggerModel.SessionStateChangeType switch
            {
                "Lock" => TaskSessionStateChangeType.SessionLock,
                "Unlock" => TaskSessionStateChangeType.SessionUnlock,
                "RemoteConnect" => TaskSessionStateChangeType.RemoteConnect,
                "RemoteDisconnect" => TaskSessionStateChangeType.RemoteDisconnect,
                _ => TaskSessionStateChangeType.SessionUnlock
            };

            if (string.IsNullOrEmpty(model.RunAsUser) && !model.RunAsSystem)
            {
                sscTrigger.UserId = WindowsIdentity.GetCurrent().Name;
            }
            else if (!string.IsNullOrEmpty(model.RunAsUser))
            {
                sscTrigger.UserId = model.RunAsUser;
            }
            return sscTrigger;
        }

        private TaskFolder GetOrCreateFolder(TaskService ts, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "\\") return ts.RootFolder;

            try
            {
                var folder = ts.GetFolder(folderPath);
                if (folder != null) return folder;
            }
            catch (System.IO.FileNotFoundException) { }
            catch (Exception ex) { throw new Exception($"Failed to check folder '{folderPath}': {ex.Message}"); }

            int lastSlash = folderPath.LastIndexOf('\\');
            string parentPath = lastSlash > 0 ? folderPath.Substring(0, lastSlash) : "\\";
            string folderName = folderPath.Substring(lastSlash + 1);

            TaskFolder parentFolder = GetOrCreateFolder(ts, parentPath);
            try
            {
                return parentFolder.CreateFolder(folderName);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create folder '{folderName}' in '{parentPath}': {ex.Message}");
            }
        }

        public void ExportTask(string taskPath, string outputPath)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(taskPath);
                if (task != null)
                {
                    task.Definition.XmlText = task.Definition.XmlText; // Ensure XML is generated
                    System.IO.File.WriteAllText(outputPath, task.Definition.XmlText, new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));
                }
                else
                {
                    throw new Exception($"Task '{taskPath}' not found.");
                }
            }
        }

        private List<ScheduledTaskModel>? _discoveredCache;

        /// <summary>Drops the event-log task-discovery cache so the next GetFolderStructure() call
        /// re-scans instead of reusing a snapshot that predates a task being added/removed.</summary>
        internal void InvalidateDiscoveredCache() => _discoveredCache = null;

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
            using var lookupTs = new TaskService();
            foreach (var path in discoveredRaw)
            {
                bool added = false;
                try
                {
                    var task = lookupTs.GetTask(path);
                    if (task != null)
                    {
                        var model = MapTaskToModel(task);
                        model.IsFromEventLog = true;
                        discoveredTasks.Add(model);
                        added = true;
                    }
                }
                catch (Exception ex) when (IsAccessDenied(ex))
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
                string query = $"*[System/Provider[@Name='Microsoft-Windows-TaskScheduler'] and EventData[Data[@Name='TaskName']={ToXPathLiteral(taskPath)}]]";
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
                using var ts = new TaskService();
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

        /// <summary>Sentinel TriggerType used when a trigger's real type has no model representation
        /// (e.g. RegistrationTrigger, custom XML triggers) — must never be saved back to Task Scheduler.</summary>

        private TaskTriggerModel MapTriggerToModel(Trigger trigger)
        {
            var model = new TaskTriggerModel();
            
            // Basic Start/End
            if (trigger.StartBoundary != DateTime.MinValue)
                model.ScheduleInfo = DurationUtil.FormatScheduleInfo(trigger.StartBoundary);
            if (trigger.EndBoundary != DateTime.MaxValue)
                model.ExpirationDate = trigger.EndBoundary;

            // Repetition
            if (trigger.Repetition.Interval != TimeSpan.Zero)
            {
                try { model.RepetitionInterval = System.Xml.XmlConvert.ToString(trigger.Repetition.Interval); } catch {}
                try { model.RepetitionDuration = System.Xml.XmlConvert.ToString(trigger.Repetition.Duration); } catch {}
            }

            // RandomDelay is a property of most trigger types independent of repetition — read it
            // unconditionally so a delay configured without repetition isn't silently dropped.
            try
            {
                var dTrigger = (dynamic)trigger;
                if (dTrigger.RandomDelay is TimeSpan rd && rd != TimeSpan.Zero)
                    model.RandomDelay = System.Xml.XmlConvert.ToString(rd);
            }
            catch { }

            switch (trigger)
            {
                case DailyTrigger dt:
                    model.TriggerType = AppTriggerType.Daily;
                    model.DailyInterval = dt.DaysInterval;
                    break;
                case WeeklyTrigger wt:
                    model.TriggerType = AppTriggerType.Weekly;
                    model.WeeklyInterval = wt.WeeksInterval;
                    MapDaysOfWeek(wt.DaysOfWeek, model.WeeklyDays);
                    break;
                case MonthlyTrigger mt:
                    model.TriggerType = AppTriggerType.Monthly;
                    model.MonthlyIsDayOfWeek = false;
                    MapMonths(mt.MonthsOfYear, model.MonthlyMonths);
                    model.MonthlyDays.AddRange(mt.DaysOfMonth);
                    if (mt.RunOnLastDayOfMonth) model.MonthlyDays.Add(32);
                    break;
                case MonthlyDOWTrigger mdt:
                    model.TriggerType = AppTriggerType.Monthly;
                    model.MonthlyIsDayOfWeek = true;
                    MapMonths(mdt.MonthsOfYear, model.MonthlyMonths);
                    model.MonthlyWeek = mdt.WeeksOfMonth.ToString().Replace("Week","");
                    model.MonthlyDayOfWeek = mdt.DaysOfWeek.ToString();
                    break;
                case LogonTrigger: model.TriggerType = AppTriggerType.AtLogon; break;
                case BootTrigger: model.TriggerType = AppTriggerType.AtStartup; break;
                case IdleTrigger: model.TriggerType = AppTriggerType.OnIdle; break;
                case SessionStateChangeTrigger ssc:
                    model.TriggerType = AppTriggerType.SessionStateChange;
                    model.SessionStateChangeType = ssc.StateChange.ToString().Replace("Session","");
                    break;
                case EventTrigger et:
                    model.TriggerType = AppTriggerType.Event;
                    // Simplistic parsing maintained for backward compatibility
                    try {
                         var sub = et.Subscription;
                         if (sub.Contains("Path=")) model.EventLog = Regex.Match(sub, "Path=\"([^\"]+)\"").Groups[1].Value;
                         if (sub.Contains("Provider[@Name=")) model.EventSource = Regex.Match(sub, "Provider\\[@Name='([^']+)'\\]").Groups[1].Value;
                         if (sub.Contains("EventID="))
                         {
                             if (int.TryParse(Regex.Match(sub, "EventID=(\\d+)").Groups[1].Value, out int id))
                                model.EventId = id;
                         }
                    } catch {}
                    break;
                case TimeTrigger: model.TriggerType = AppTriggerType.Once; break;
                default:
                    // RegistrationTrigger, custom/derived triggers, etc: no model representation.
                    // Leaving TriggerType at its "Daily" default would silently convert this into a
                    // daily 9 AM trigger on save, so mark it unsupported instead.
                    model.TriggerType = AppTriggerType.Unsupported;
                    break;
            }
            return model;
        }

        private void MapDaysOfWeek(DaysOfTheWeek dow, List<string> target)
        {
            if ((dow & DaysOfTheWeek.Monday) != 0) target.Add("Monday");
            if ((dow & DaysOfTheWeek.Tuesday) != 0) target.Add("Tuesday");
            if ((dow & DaysOfTheWeek.Wednesday) != 0) target.Add("Wednesday");
            if ((dow & DaysOfTheWeek.Thursday) != 0) target.Add("Thursday");
            if ((dow & DaysOfTheWeek.Friday) != 0) target.Add("Friday");
            if ((dow & DaysOfTheWeek.Saturday) != 0) target.Add("Saturday");
            if ((dow & DaysOfTheWeek.Sunday) != 0) target.Add("Sunday");
        }

        private void MapMonths(MonthsOfTheYear moy, List<string> target)
        {
            if (moy == MonthsOfTheYear.AllMonths) 
            {
                // Assuming AllMonths means all, but logic in old code was explicit check. 
                // Let's stick to explicit checks to match old logic exactly.
            }
            foreach(MonthsOfTheYear m in Enum.GetValues(typeof(MonthsOfTheYear)))
            {
                 if (m != MonthsOfTheYear.AllMonths && (moy & m) != 0) target.Add(m.ToString());
            }
        }

        private const string MetadataPrefix = "<!-- FTS_META:";
        private const string MetadataSuffix = " -->";

        private void ParseMetadata(ScheduledTaskModel model)
        {
            if (string.IsNullOrEmpty(model.Description)) return;

            int startIndex = model.Description.IndexOf(MetadataPrefix);
            if (startIndex == -1) return;

            int endIndex = model.Description.IndexOf(MetadataSuffix, startIndex);
            if (endIndex == -1) return;

            string json = model.Description.Substring(startIndex + MetadataPrefix.Length, endIndex - (startIndex + MetadataPrefix.Length));
            try
            {
                var metadata = JsonSerializer.Deserialize<TaskMetadata>(json);
                if (metadata != null)
                {
                    model.Category = metadata.Category ?? "";
                    model.Tags = new ObservableCollection<string>(metadata.Tags ?? new List<string>());
                    model.Pipeline = metadata.Pipeline ?? new TaskPipeline();

                    // Clean description for UI
                    model.Description = model.Description.Remove(startIndex).Trim();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Ignoring malformed FTS metadata on task '{model.Path}': {ex.Message}");
            }
        }

        private string UpdateDescriptionWithMetadata(string description, string category, List<string> tags, TaskPipeline? pipeline)
        {
            string cleanDescription = description;
            int startIndex = description.IndexOf(MetadataPrefix);
            if (startIndex != -1)
            {
                cleanDescription = description.Remove(startIndex).Trim();
            }

            bool hasPipeline = pipeline != null && (pipeline.IsEnabled || pipeline.HasAnyTargets);
            if (string.IsNullOrEmpty(category) && (tags == null || tags.Count == 0) && !hasPipeline)
            {
                return cleanDescription;
            }

            var metadata = new TaskMetadata
            {
                Category = category,
                Tags = tags,
                Pipeline = hasPipeline ? pipeline : null
            };
            string json = JsonSerializer.Serialize(metadata);
            return $"{cleanDescription}\n\n{MetadataPrefix}{json}{MetadataSuffix}".Trim();
        }

        private class TaskMetadata
        {
            public string? Category { get; set; }
            public List<string>? Tags { get; set; }
            public TaskPipeline? Pipeline { get; set; }
        }

        // Helpers
        private DaysOfTheWeek GetDaysOfWeek(List<string> days)
        {
            DaysOfTheWeek dow = 0;
            foreach(var d in days) if (Enum.TryParse(d, out DaysOfTheWeek v)) dow |= v;
            return dow == 0 ? DaysOfTheWeek.Monday : dow;
        }

        private MonthsOfTheYear GetMonths(List<string> months)
        {
             MonthsOfTheYear moy = 0;
             foreach(var m in months) if (Enum.TryParse(m, out MonthsOfTheYear v)) moy |= v;
             return moy == 0 ? MonthsOfTheYear.AllMonths : moy;
        }
        
        private DaysOfTheWeek GetDayOfWeek(string day) => Enum.TryParse(day, out DaysOfTheWeek v) ? v : DaysOfTheWeek.Monday;
        
        private WhichWeek GetWhichWeek(string week) => week switch {
            "First" => WhichWeek.FirstWeek,
            "Second" => WhichWeek.SecondWeek,
            "Third" => WhichWeek.ThirdWeek,
            "Fourth" => WhichWeek.FourthWeek,
            "Last" => WhichWeek.LastWeek,
            _ => WhichWeek.FirstWeek
        };

        /// <summary>Win32 E_ACCESSDENIED (0x80070005), which is what Task Scheduler's COM layer
        /// raises for a protected/system task regardless of the OS display language.</summary>
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);

        /// <summary>
        /// Checks the HRESULT (walking inner exceptions too) instead of matching the exception
        /// message text — the old code only recognized English and German "access denied" strings,
        /// so the check silently failed on any other OS display language (see 3.9).
        /// </summary>
        internal static bool IsAccessDenied(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e.HResult == E_ACCESSDENIED) return true;
                if (e is System.Runtime.InteropServices.COMException com && com.ErrorCode == E_ACCESSDENIED) return true;
            }
            return false;
        }

        public TaskFolderModel GetFolderStructure()
        {
            using (var ts = new TaskService())
            {
                var root = new TaskFolderModel { Name = "Task Scheduler Library", Path = "\\" };
                EnumFolders(ts.RootFolder, root);

                // Synthesize folders from discovered tasks. This used to force a full event-log
                // re-scan (up to 2000 events) plus a fresh TaskService per discovered path on every
                // single folder-tree refresh; it now reuses the cache and is invalidated explicitly
                // by RegisterTask/DeleteTask instead (see 3.5).
                var discovered = DiscoverTasksFromEventLog();
                foreach (var task in discovered)
                {
                    SynthesizeFoldersInTree(root, task.Path);
                }

                return root;
            }
        }

        private void SynthesizeFoldersInTree(TaskFolderModel root, string taskPath)
        {
            string? dirPath = System.IO.Path.GetDirectoryName(taskPath);
            if (string.IsNullOrEmpty(dirPath) || dirPath == "\\") return;

            // Split path into parts, skipping first empty (from root \)
            var parts = dirPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            string currentPath = "";

            foreach (var part in parts)
            {
                currentPath += "\\" + part;
                var child = current.SubFolders.FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new TaskFolderModel { Name = part, Path = currentPath };
                    current.SubFolders.Add(child);
                }
                current = child;
            }
        }

        private void EnumFolders(TaskFolder folder, TaskFolderModel model)
        {
            foreach (var subFolder in folder.SubFolders)
            {
                var subModel = new TaskFolderModel { Name = subFolder.Name, Path = subFolder.Path };
                model.SubFolders.Add(subModel);
                EnumFolders(subFolder, subModel);
            }
        }

        public void MoveTask(string sourcePath, string targetFolderPath)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(sourcePath);
                if (task == null) throw new Exception($"Task '{sourcePath}' not found.");

                var targetFolder = GetOrCreateFolder(ts, targetFolderPath);

                // Guard: already in the target folder
                string currentFolder = System.IO.Path.GetDirectoryName(sourcePath) ?? "\\";
                if (currentFolder.Equals(targetFolderPath, StringComparison.OrdinalIgnoreCase)) return;

                // Guard: name collision in target
                string taskName = task.Name;
                if (targetFolder.Tasks.Any(t => t.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception($"A task named '{taskName}' already exists in the target folder.");

                try
                {
                    targetFolder.RegisterTaskDefinition(taskName, task.Definition, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                }
                catch (Exception ex) when (IsAccessDenied(ex))
                {
                    // Fall back to a safe (non-elevated) copy
                    var safeTd = ts.NewTask();
                    safeTd.XmlText = task.Xml;
                    safeTd.Principal.RunLevel = TaskRunLevel.LUA;
                    safeTd.Principal.LogonType = TaskLogonType.InteractiveToken;
                    safeTd.Principal.UserId = null;
                    targetFolder.RegisterTaskDefinition(taskName, safeTd, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                }

                task.Folder.DeleteTask(taskName);
                Serilog.Log.Information("{Message}", $"Moved task '{taskName}' to '{targetFolderPath}'.");
            }
        }

        public void MoveFolder(string sourceFolderPath, string targetParentFolderPath)
        {
            if (string.IsNullOrWhiteSpace(sourceFolderPath) || sourceFolderPath == "\\")
                throw new Exception("Cannot move the root folder.");

            // Guard: already in the target folder (no-op move)
            string currentParent = System.IO.Path.GetDirectoryName(sourceFolderPath) ?? "\\";
            if (targetParentFolderPath.TrimEnd('\\').Equals(currentParent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return;

            // Guard: moving into own descendant
            if (targetParentFolderPath.Equals(sourceFolderPath, StringComparison.OrdinalIgnoreCase) || 
                targetParentFolderPath.StartsWith(sourceFolderPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Cannot move a folder into one of its own sub-folders.");

            string newPath;
            using (var ts = new TaskService())
            {
                var sourceFolder = ts.GetFolder(sourceFolderPath);
                if (sourceFolder == null) throw new Exception($"Source folder '{sourceFolderPath}' not found.");

                string folderName = sourceFolder.Name;
                newPath = targetParentFolderPath == "\\"
                    ? "\\" + folderName
                    : targetParentFolderPath.TrimEnd('\\') + "\\" + folderName;

                // Guard: name collision
                try
                {
                    if (ts.GetFolder(newPath) != null)
                        throw new Exception($"A folder named '{folderName}' already exists in the target.");
                }
                catch (System.IO.FileNotFoundException) { }

                var targetFolder = GetOrCreateFolder(ts, newPath);
                try
                {
                    CopyFolderContents(ts, sourceFolder, targetFolder);
                }
                catch
                {
                    // Copying only some tasks left a half-populated folder at the destination —
                    // clean it up so a failed move doesn't leave orphaned duplicates behind (3.10).
                    TryDeleteFolderQuietly(ts, newPath);
                    throw;
                }
            }

            // Perform deletion in a fresh context to ensure no handles are held
            DeleteFolder(sourceFolderPath);
            Serilog.Log.Information("{Message}", $"Moved folder '{sourceFolderPath}' to '{newPath}'.");
        }

        public void CreateFolder(string path)
        {
             using (var ts = new TaskService())
             {
                 GetOrCreateFolder(ts, path);
             }
        }

        public void DeleteFolder(string path)
        {
            using (var ts = new TaskService())
            {
                var folder = ts.GetFolder(path);
                if (folder != null && folder.Path != "\\")
                {
                   DeleteFolderRecursive(folder);
                   folder.Parent?.DeleteFolder(folder.Name);
                }
            }
        }

        private void DeleteFolderRecursive(TaskFolder folder)
        {
            // Copy to list to avoid collection modification exceptions
            var tasks = folder.Tasks.ToList();
            foreach (var task in tasks)
            {
                folder.DeleteTask(task.Name);
            }

            var subFolders = folder.SubFolders.ToList();
            foreach (var sub in subFolders)
            {
                DeleteFolderRecursive(sub);
                folder.DeleteFolder(sub.Name);
            }
        }

        public void RegisterTaskFromXml(string folderPath, string name, string xml)
        {
            using (var ts = new TaskService())
            {
                var td = ts.NewTask();
                td.XmlText = xml;
                var folder = GetOrCreateFolder(ts, folderPath);
                folder.RegisterTaskDefinition(name, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
            }
        }

        public void RenameFolder(string oldPath, string newName)
        {
            using (var ts = new TaskService())
            {
                var oldFolder = ts.GetFolder(oldPath);
                if (oldFolder == null || oldFolder.Path == "\\")
                    throw new Exception("Cannot rename the system root folder.");

                string parentPath = oldFolder.Parent?.Path ?? "\\";
                string newPath = parentPath == "\\" ? "\\" + newName : parentPath + "\\" + newName;

                try { if (ts.GetFolder(newPath) != null) throw new Exception($"A folder named '{newName}' already exists."); } catch (System.IO.FileNotFoundException) { }

                var newFolder = GetOrCreateFolder(ts, newPath);
                try
                {
                    CopyFolderContents(ts, oldFolder, newFolder);
                }
                catch
                {
                    // Same partial-copy cleanup as MoveFolder (3.10).
                    TryDeleteFolderQuietly(ts, newPath);
                    throw;
                }
                DeleteFolderRecursive(oldFolder);
                oldFolder.Parent?.DeleteFolder(oldFolder.Name);
            }
        }

        /// <summary>Best-effort cleanup of a half-populated target folder after a failed copy (3.10).</summary>
        private void TryDeleteFolderQuietly(TaskService ts, string path)
        {
            try
            {
                var folder = ts.GetFolder(path);
                if (folder != null && folder.Path != "\\")
                {
                    DeleteFolderRecursive(folder);
                    folder.Parent?.DeleteFolder(folder.Name);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not clean up the partially-copied target folder '{path}' after a failed move/rename: {ex.Message}");
            }
        }

        private void CopyFolderContents(TaskService ts, TaskFolder sourceFolder, TaskFolder targetFolder)
        {
            var failedTasks = new List<string>();
            CopyFolderContents(ts, sourceFolder, targetFolder, failedTasks);
            if (failedTasks.Count > 0)
            {
                throw new Exception(
                    $"Failed to copy {failedTasks.Count} task(s): {string.Join(", ", failedTasks)}. " +
                    "The source folder was left untouched so no tasks were lost.");
            }
        }

        private void CopyFolderContents(TaskService ts, TaskFolder sourceFolder, TaskFolder targetFolder, List<string> failedTasks)
        {
            foreach (var task in sourceFolder.Tasks)
            {
                try
                {
                    targetFolder.RegisterTaskDefinition(task.Name, task.Definition);
                }
                catch (Exception ex)
                {
                    if (IsAccessDenied(ex))
                    {
                        try
                        {
                            var td = ts.NewTask();
                            td.XmlText = task.Xml;
                            targetFolder.RegisterTaskDefinition(task.Name, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                        }
                        catch (Exception fallbackEx)
                        {
                            Serilog.Log.Error("{Message}", $"Failed to copy task '{task.Name}': {fallbackEx.Message}");
                            failedTasks.Add(task.Name);
                        }
                    }
                    else
                    {
                        Serilog.Log.Error("{Message}", $"Failed to copy task '{task.Name}': {ex.Message}");
                        failedTasks.Add(task.Name);
                    }
                }
            }

            foreach (var sub in sourceFolder.SubFolders)
            {
                var newSub = targetFolder.CreateFolder(sub.Name);
                CopyFolderContents(ts, sub, newSub, failedTasks);
            }
        }
    }
}
