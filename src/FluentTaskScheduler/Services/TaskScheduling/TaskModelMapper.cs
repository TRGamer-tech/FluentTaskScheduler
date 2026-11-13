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
    /// <summary>Converts Task Scheduler COM objects into app models (read direction).</summary>
    internal static class TaskModelMapper
    {
        internal static ScheduledTaskModel MapTaskToModel(Microsoft.Win32.TaskScheduler.Task task)
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

        internal static AppTaskState MapTaskState(Microsoft.Win32.TaskScheduler.TaskState state) => state switch
        {
            Microsoft.Win32.TaskScheduler.TaskState.Disabled => AppTaskState.Disabled,
            Microsoft.Win32.TaskScheduler.TaskState.Queued => AppTaskState.Queued,
            Microsoft.Win32.TaskScheduler.TaskState.Ready => AppTaskState.Ready,
            Microsoft.Win32.TaskScheduler.TaskState.Running => AppTaskState.Running,
            _ => AppTaskState.Unknown
        };

        internal static void MapSettingsToModel(TaskSettings settings, ScheduledTaskModel model)
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

        internal static void FixSessionStateTriggers(Microsoft.Win32.TaskScheduler.Task task, ScheduledTaskModel model)
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

        /// <summary>Sentinel TriggerType used when a trigger's real type has no model representation
        /// (e.g. RegistrationTrigger, custom XML triggers) — must never be saved back to Task Scheduler.</summary>

        internal static TaskTriggerModel MapTriggerToModel(Trigger trigger)
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

        internal static void MapDaysOfWeek(DaysOfTheWeek dow, List<string> target)
        {
            if ((dow & DaysOfTheWeek.Monday) != 0) target.Add("Monday");
            if ((dow & DaysOfTheWeek.Tuesday) != 0) target.Add("Tuesday");
            if ((dow & DaysOfTheWeek.Wednesday) != 0) target.Add("Wednesday");
            if ((dow & DaysOfTheWeek.Thursday) != 0) target.Add("Thursday");
            if ((dow & DaysOfTheWeek.Friday) != 0) target.Add("Friday");
            if ((dow & DaysOfTheWeek.Saturday) != 0) target.Add("Saturday");
            if ((dow & DaysOfTheWeek.Sunday) != 0) target.Add("Sunday");
        }

        internal static void MapMonths(MonthsOfTheYear moy, List<string> target)
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

        internal const string MetadataPrefix = "<!-- FTS_META:";
        internal const string MetadataSuffix = " -->";

        internal static void ParseMetadata(ScheduledTaskModel model)
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

        internal static string UpdateDescriptionWithMetadata(string description, string category, List<string> tags, TaskPipeline? pipeline)
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

        internal class TaskMetadata
        {
            public string? Category { get; set; }
            public List<string>? Tags { get; set; }
            public TaskPipeline? Pipeline { get; set; }
        }
    }
}
