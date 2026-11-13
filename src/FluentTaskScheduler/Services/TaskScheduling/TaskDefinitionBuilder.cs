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
    /// <summary>Builds Task Scheduler definitions/triggers from app models (write direction).</summary>
    internal static class TaskDefinitionBuilder
    {
        internal static void ConfigureTaskDefinition(TaskDefinition td, ScheduledTaskModel model)
        {
            td.RegistrationInfo.Description = TaskModelMapper.UpdateDescriptionWithMetadata(model.Description, model.Category, model.Tags.ToList(), model.Pipeline);
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

        internal static void ConfigureTrigger(TaskDefinition td, TaskTriggerModel triggerModel, ScheduledTaskModel model)
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

        internal static Trigger CreateMonthlyTrigger(TaskTriggerModel model, DateTime startTime)
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

        internal static Trigger CreateEventTrigger(TaskTriggerModel model)
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

        internal static Trigger CreateSessionTrigger(TaskTriggerModel triggerModel, ScheduledTaskModel model)
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

        // Helpers
        internal static DaysOfTheWeek GetDaysOfWeek(List<string> days)
        {
            DaysOfTheWeek dow = 0;
            foreach(var d in days) if (Enum.TryParse(d, out DaysOfTheWeek v)) dow |= v;
            return dow == 0 ? DaysOfTheWeek.Monday : dow;
        }

        internal static MonthsOfTheYear GetMonths(List<string> months)
        {
             MonthsOfTheYear moy = 0;
             foreach(var m in months) if (Enum.TryParse(m, out MonthsOfTheYear v)) moy |= v;
             return moy == 0 ? MonthsOfTheYear.AllMonths : moy;
        }
        
        internal static DaysOfTheWeek GetDayOfWeek(string day) => Enum.TryParse(day, out DaysOfTheWeek v) ? v : DaysOfTheWeek.Monday;
        
        internal static WhichWeek GetWhichWeek(string week) => week switch {
            "First" => WhichWeek.FirstWeek,
            "Second" => WhichWeek.SecondWeek,
            "Third" => WhichWeek.ThirdWeek,
            "Fourth" => WhichWeek.FourthWeek,
            "Last" => WhichWeek.LastWeek,
            _ => WhichWeek.FirstWeek
        };
    }
}
