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

namespace FluentTaskScheduler.Services
{
    public class TaskServiceWrapper
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
                    LogService.Error($"Could not read task '{task.Name}': {ex.Message}");
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
                State = task.State.ToString(),
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
                }
            }

            // Map Triggers
            if (def.Triggers != null)
            {
                foreach (var trigger in def.Triggers)
                {
                    model.TriggersList.Add(MapTriggerToModel(trigger));
                }
                // Update display string using wrapper descriptors
                model.Triggers = string.Join(", ", model.TriggersList.Select(t => t.Descriptor));
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

            model.MultipleInstancesPolicy = settings.MultipleInstances switch
            {
                TaskInstancesPolicy.Parallel => "Parallel",
                TaskInstancesPolicy.Queue => "Queue",
                TaskInstancesPolicy.StopExisting => "StopExisting",
                _ => "IgnoreNew"
            };

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
            if (model.TriggersList.Any(t => t.TriggerType == "SessionStateChange"))
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
                        foreach (var t in model.TriggersList.Where(t => t.TriggerType == "SessionStateChange"))
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
                LogService.Error($"Access denied: Cannot {(enabled ? "enable" : "disable")} task '{path}'.");
                throw new UnauthorizedAccessException(
                    $"The user account under which you are performing this action does not have permission to {(enabled ? "enable" : "disable")} the task \"{System.IO.Path.GetFileName(path)}\".\n\n" +
                    "This task is protected and cannot be modified, even with administrator privileges.", ex);
            }
        }

        public void RunTask(string path)
        {
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
                LogService.Error($"{level}: Cannot run task '{path}': {ex.Message}");
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
                    LogService.Info($"Registered task '{model.Name}'.");
                }
                catch (Exception ex)
                {
                    if (IsAccessDenied(ex))
                    {
                        LogService.Warn($"Access denied registering task '{model.Name}' with elevated privileges; falling back to current user context.");
                        RegisterSafeTask(ts, targetFolder, model, td);
                    }
                    else
                    {
                        LogService.Error($"Failed to register task '{model.Name}': {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private void RegisterSafeTask(TaskService ts, TaskFolder targetFolder, ScheduledTaskModel model, TaskDefinition originalTd)
        {
            // Create a fresh definition to avoid polluted privileges
            TaskDefinition safeTd = ts.NewTask();

            // Copy properties safely
            safeTd.RegistrationInfo.Description = originalTd.RegistrationInfo.Description;
            safeTd.RegistrationInfo.Author = originalTd.RegistrationInfo.Author;
            safeTd.Settings.Enabled = originalTd.Settings.Enabled;
            safeTd.Settings.Compatibility = TaskCompatibility.V2;
            
            // Map settings
            safeTd.Settings.MultipleInstances = originalTd.Settings.MultipleInstances;
            safeTd.Settings.DisallowStartIfOnBatteries = originalTd.Settings.DisallowStartIfOnBatteries;
            safeTd.Settings.StopIfGoingOnBatteries = originalTd.Settings.StopIfGoingOnBatteries;
            safeTd.Settings.AllowHardTerminate = originalTd.Settings.AllowHardTerminate;
            safeTd.Settings.StartWhenAvailable = originalTd.Settings.StartWhenAvailable;
            safeTd.Settings.RunOnlyIfNetworkAvailable = originalTd.Settings.RunOnlyIfNetworkAvailable;
            safeTd.Settings.IdleSettings.IdleDuration = originalTd.Settings.IdleSettings.IdleDuration;
            safeTd.Settings.IdleSettings.StopOnIdleEnd = originalTd.Settings.IdleSettings.StopOnIdleEnd;
            safeTd.Settings.ExecutionTimeLimit = originalTd.Settings.ExecutionTimeLimit;
            safeTd.Settings.Priority = originalTd.Settings.Priority;
            safeTd.Settings.DeleteExpiredTaskAfter = originalTd.Settings.DeleteExpiredTaskAfter;

            // Copy Triggers (strip specific user context that requires admin)
            foreach (var oldTrig in originalTd.Triggers)
            {
                var clone = (Trigger)oldTrig.Clone();
                if (clone is SessionStateChangeTrigger sst) sst.UserId = null;
                if (clone is LogonTrigger lt) lt.UserId = null;
                safeTd.Triggers.Add(clone);
            }

            // Copy Actions
            foreach (var oldAct in originalTd.Actions)
            {
                safeTd.Actions.Add((Microsoft.Win32.TaskScheduler.Action)oldAct.Clone());
            }

            // Force safe principal
            safeTd.Principal.RunLevel = TaskRunLevel.LUA;
            safeTd.Principal.LogonType = TaskLogonType.InteractiveToken;
            safeTd.Principal.UserId = null;
            safeTd.Principal.GroupId = null;

            targetFolder.RegisterTaskDefinition(
                model.Name,
                safeTd,
                TaskCreation.CreateOrUpdate,
                null, 
                null, 
                TaskLogonType.InteractiveToken
            );
            
            LogService.Info($"Registered task '{model.Name}' via fallback (InteractiveToken).");
        }

        private void ConfigureTaskDefinition(TaskDefinition td, ScheduledTaskModel model)
        {
            td.RegistrationInfo.Description = UpdateDescriptionWithMetadata(model.Description, model.Category, model.Tags.ToList());
            td.RegistrationInfo.Author = model.Author;
            td.Settings.Enabled = model.IsEnabled;
            td.Settings.Hidden = model.IsHidden;
            td.Principal.RunLevel = model.RunWithHighestPrivileges ? TaskRunLevel.Highest : TaskRunLevel.LUA;

            foreach (var triggerModel in model.TriggersList)
            {
                ConfigureTrigger(td, triggerModel, model);
            }

            if (model.Actions.Count > 0)
            {
                foreach (var act in model.Actions)
                {
                    if (!string.IsNullOrWhiteSpace(act.Command))
                    {
                        td.Actions.Add(new ExecAction(act.Command, act.Arguments, act.WorkingDirectory));
                    }
                }
            }
            else
            {
                td.Actions.Add(new ExecAction("notepad.exe"));
            }

            // Apply Settings
            td.Settings.RunOnlyIfIdle = model.OnlyIfIdle;
            if (!string.IsNullOrWhiteSpace(model.IdleDuration))
            {
                try { td.Settings.IdleSettings.IdleDuration = System.Xml.XmlConvert.ToTimeSpan(model.IdleDuration); } catch { }
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
                catch (Exception ex) { LogService.Warn($"Could not set NetworkSettings: {ex.Message}"); }
            }

            td.Settings.WakeToRun = model.WakeToRun;
            td.Settings.StartWhenAvailable = model.RunIfMissed;

            if (model.RestartOnFailure && !string.IsNullOrWhiteSpace(model.RestartInterval))
            {
                try
                {
                    td.Settings.RestartInterval = System.Xml.XmlConvert.ToTimeSpan(model.RestartInterval);
                    td.Settings.RestartCount = model.RestartCount;
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(model.StopIfRunsLongerThan))
            {
                try { td.Settings.ExecutionTimeLimit = System.Xml.XmlConvert.ToTimeSpan(model.StopIfRunsLongerThan); }
                catch { td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72); }
            }

            td.Settings.MultipleInstances = model.MultipleInstancesPolicy switch
            {
                "Parallel" => TaskInstancesPolicy.Parallel,
                "Queue" => TaskInstancesPolicy.Queue,
                "StopExisting" => TaskInstancesPolicy.StopExisting,
                _ => TaskInstancesPolicy.IgnoreNew
            };

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
            DateTime startTime = DateTime.Today.AddHours(9);
            if (!string.IsNullOrWhiteSpace(triggerModel.ScheduleInfo) && DateTime.TryParse(triggerModel.ScheduleInfo, out var parsedStart))
            {
                startTime = parsedStart;
            }

            Trigger t = triggerModel.TriggerType switch
            {
                "Daily" => new DailyTrigger { StartBoundary = startTime, DaysInterval = triggerModel.DailyInterval },
                "Weekly" => new WeeklyTrigger { StartBoundary = startTime, WeeksInterval = triggerModel.WeeklyInterval, DaysOfWeek = GetDaysOfWeek(triggerModel.WeeklyDays) },
                "Monthly" => CreateMonthlyTrigger(triggerModel, startTime),
                "AtLogon" => new LogonTrigger(),
                "AtStartup" => new BootTrigger(),
                "Once" => new TimeTrigger { StartBoundary = startTime },
                "One Time" => new TimeTrigger { StartBoundary = startTime },
                "Event" => CreateEventTrigger(triggerModel),
                "OnIdle" => new IdleTrigger(),
                "SessionStateChange" => CreateSessionTrigger(triggerModel, model),
                "OnLock" => CreateSessionTrigger(triggerModel, model),
                "OnUnlock" => CreateSessionTrigger(triggerModel, model),
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
                    if (!string.IsNullOrWhiteSpace(triggerModel.RandomDelay))
                    {
                        try { ((dynamic)t).RandomDelay = System.Xml.XmlConvert.ToTimeSpan(triggerModel.RandomDelay); } catch { }
                    }
                }
                catch { }
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
            string query = "*";

            if (!string.IsNullOrWhiteSpace(model.EventSource) || model.EventId.HasValue)
            {
                string conditions = "";
                if (!string.IsNullOrWhiteSpace(model.EventSource))
                    conditions += $"Provider[@Name='{model.EventSource}']";

                if (model.EventId.HasValue)
                {
                    if (conditions.Length > 0) conditions += " and ";
                    conditions += $"(EventID={model.EventId})";
                }
                query = $"*[System[{conditions}]]";
            }
            et.Subscription = $"<QueryList><Query Id=\"0\" Path=\"{log}\"><Select Path=\"{log}\">{query}</Select></Query></QueryList>";
            return et;
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

        public List<ScheduledTaskModel> DiscoverTasksFromEventLog(bool forceRefresh = false)
        {
            if (_discoveredCache != null && !forceRefresh) return _discoveredCache;

            var discoveredRaw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Event IDs for task activity: 100 (started), 102 (completed), 107 (triggered), 110 (registered)
                string query = "*[System[(EventID=100 or EventID=102 or EventID=107 or EventID=110)]]";
                EventLogQuery eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                EventLogReader logReader = new EventLogReader(eventsQuery);

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
                LogService.Error($"Could not discover tasks via event log: {ex.Message}");
            }

            var discoveredTasks = new List<ScheduledTaskModel>();
            foreach (var path in discoveredRaw)
            {
                bool added = false;
                try
                {
                    using (var ts = new TaskService())
                    {
                        var task = ts.GetTask(path);
                        if (task != null)
                        {
                            var model = MapTaskToModel(task);
                            model.IsFromEventLog = true;
                            discoveredTasks.Add(model);
                            added = true;
                        }
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
                        State = "Access Denied",
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
                string query = $"*[System/Provider[@Name='Microsoft-Windows-TaskScheduler'] and EventData[Data[@Name='TaskName']='{taskPath}']]";
                EventLogQuery eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                EventLogReader logReader = new EventLogReader(eventsQuery);

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
                            Message = record.FormatDescription() ?? record.LevelDisplayName ?? "",
                            EventId = record.Id,
                            ActivityId = record.ActivityId,
                            User = GetUserFromRecord(record),
                            TaskPath = taskPath,
                            TaskName = System.IO.Path.GetFileName(taskPath),
                            Level = record.LevelDisplayName ?? "Information",
                            Keywords = record.KeywordsDisplayNames != null ? string.Join(", ", record.KeywordsDisplayNames) : "None",
                            Computer = record.MachineName ?? "Local",
                            TaskCategory = record.TaskDisplayName ?? "None",
                            OpCode = record.OpcodeDisplayName ?? "Info"
                        });
                    }
                }
                history = history.OrderByDescending(h => h.Time).ToList();
            }
            catch (Exception ex)
            {
                LogService.Error($"Could not read task history: {ex.Message}");
            }
            return history;
        }

        private string GetEventResult(int eventId) => eventId switch
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

        private string GetEventExitCode(EventRecord record)
        {
            try
            {
                if (record.Properties != null && record.Properties.Count > 0)
                {
                    foreach (var prop in record.Properties)
                    {
                        if (prop.Value is int exitCode && exitCode != 0) return exitCode.ToString();
                    }
                }
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

        private TaskTriggerModel MapTriggerToModel(Trigger trigger)
        {
            var model = new TaskTriggerModel();
            
            // Basic Start/End
            if (trigger.StartBoundary != DateTime.MinValue)
                model.ScheduleInfo = trigger.StartBoundary.ToString("yyyy-MM-dd HH:mm:ss");
            if (trigger.EndBoundary != DateTime.MaxValue)
                model.ExpirationDate = trigger.EndBoundary;

            // Repetition
            if (trigger.Repetition.Interval != TimeSpan.Zero)
            {
                try { model.RepetitionInterval = System.Xml.XmlConvert.ToString(trigger.Repetition.Interval); } catch {}
                try { model.RepetitionDuration = System.Xml.XmlConvert.ToString(trigger.Repetition.Duration); } catch {}
                try { 
                    var dTrigger = (dynamic)trigger;
                    if (dTrigger.RandomDelay != TimeSpan.Zero)
                        model.RandomDelay = System.Xml.XmlConvert.ToString(dTrigger.RandomDelay);
                } catch {}
            }

            switch (trigger)
            {
                case DailyTrigger dt:
                    model.TriggerType = "Daily";
                    model.DailyInterval = dt.DaysInterval;
                    break;
                case WeeklyTrigger wt:
                    model.TriggerType = "Weekly";
                    model.WeeklyInterval = wt.WeeksInterval;
                    MapDaysOfWeek(wt.DaysOfWeek, model.WeeklyDays);
                    break;
                case MonthlyTrigger mt:
                    model.TriggerType = "Monthly";
                    model.MonthlyIsDayOfWeek = false;
                    MapMonths(mt.MonthsOfYear, model.MonthlyMonths);
                    model.MonthlyDays.AddRange(mt.DaysOfMonth);
                    if (mt.RunOnLastDayOfMonth) model.MonthlyDays.Add(32);
                    break;
                case MonthlyDOWTrigger mdt:
                    model.TriggerType = "Monthly";
                    model.MonthlyIsDayOfWeek = true;
                    MapMonths(mdt.MonthsOfYear, model.MonthlyMonths);
                    model.MonthlyWeek = mdt.WeeksOfMonth.ToString().Replace("Week","");
                    model.MonthlyDayOfWeek = mdt.DaysOfWeek.ToString();
                    break;
                case LogonTrigger: model.TriggerType = "AtLogon"; break;
                case BootTrigger: model.TriggerType = "AtStartup"; break;
                case IdleTrigger: model.TriggerType = "OnIdle"; break;
                case SessionStateChangeTrigger ssc:
                    model.TriggerType = "SessionStateChange";
                    model.SessionStateChangeType = ssc.StateChange.ToString().Replace("Session","");
                    break;
                case EventTrigger et:
                    model.TriggerType = "Event";
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
                case TimeTrigger: model.TriggerType = "Once"; break;
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
                    
                    // Clean description for UI
                    model.Description = model.Description.Remove(startIndex).Trim();
                }
            }
            catch { /* Ignore malformed metadata */ }
        }

        private string UpdateDescriptionWithMetadata(string description, string category, List<string> tags)
        {
            string cleanDescription = description;
            int startIndex = description.IndexOf(MetadataPrefix);
            if (startIndex != -1)
            {
                cleanDescription = description.Remove(startIndex).Trim();
            }

            if (string.IsNullOrEmpty(category) && (tags == null || tags.Count == 0))
            {
                return cleanDescription;
            }

            var metadata = new TaskMetadata { Category = category, Tags = tags };
            string json = JsonSerializer.Serialize(metadata);
            return $"{cleanDescription}\n\n{MetadataPrefix}{json}{MetadataSuffix}".Trim();
        }

        private class TaskMetadata
        {
            public string? Category { get; set; }
            public List<string>? Tags { get; set; }
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

        private bool IsAccessDenied(Exception ex)
        {
             return ex.HResult == -2147024891 || 
                    ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || 
                    ex.Message.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase);
        }

        public TaskFolderModel GetFolderStructure()
        {
            using (var ts = new TaskService())
            {
                var root = new TaskFolderModel { Name = "Task Scheduler Library", Path = "\\" };
                EnumFolders(ts.RootFolder, root);

                // Synthesize folders from discovered tasks
                // Pass true to force refresh if we're reloading folders
                var discovered = DiscoverTasksFromEventLog(true); 
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
                LogService.Info($"Moved task '{taskName}' to '{targetFolderPath}'.");
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
                CopyFolderContents(ts, sourceFolder, targetFolder);
            }

            // Perform deletion in a fresh context to ensure no handles are held
            DeleteFolder(sourceFolderPath);
            LogService.Info($"Moved folder '{sourceFolderPath}' to '{newPath}'.");
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
                CopyFolderContents(ts, oldFolder, newFolder);
                DeleteFolderRecursive(oldFolder);
                oldFolder.Parent?.DeleteFolder(oldFolder.Name);
            }
        }

        private void CopyFolderContents(TaskService ts, TaskFolder sourceFolder, TaskFolder targetFolder)
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
                        var td = ts.NewTask();
                        td.XmlText = task.Xml;
                        targetFolder.RegisterTaskDefinition(task.Name, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                    }
                    else
                    {
                        LogService.Error($"Failed to copy task '{task.Name}': {ex.Message}");
                    }
                }
            }

            foreach (var sub in sourceFolder.SubFolders)
            {
                var newSub = targetFolder.CreateFolder(sub.Name);
                CopyFolderContents(ts, sub, newSub);
            }
        }
    }
}
