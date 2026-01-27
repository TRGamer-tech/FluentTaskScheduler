using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32.TaskScheduler;
using FluentTaskScheduler.Models;

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
            return tasks;
        }

        public ScheduledTaskModel? GetTaskDetails(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                if (task == null) return null;

                var def = task.Definition;
                var defTriggers = def.Triggers;
                var defActions = def.Actions;
                var defSettings = def.Settings;

                var model = new ScheduledTaskModel
                {
                    Name = task.Name,
                    Path = task.Path,
                    State = task.State.ToString(),
                    IsEnabled = task.Enabled,
                    LastRunTime = task.LastRunTime == DateTime.MinValue ? null : (DateTime?)task.LastRunTime,
                    NextRunTime = task.NextRunTime == DateTime.MinValue ? null : (DateTime?)task.NextRunTime,
                    
                    // Detailed properties
                    Author = def.RegistrationInfo.Author ?? "",
                    Description = def.RegistrationInfo.Description ?? "",
                    RunWithHighestPrivileges = def.Principal.RunLevel == TaskRunLevel.Highest,
                    Triggers = (defTriggers != null) 
                        ? string.Join(", ", defTriggers.Cast<Trigger>().Select(t => t.ToString())) 
                        : ""
                };

                // Map Actions
                // Map Actions
                if (defActions != null)
                {
                    foreach (var action in defActions)
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

                // Map Triggers and Repetition
                if (defTriggers != null)
                {
                    foreach (var trigger in defTriggers)
                    {
                        var triggerModel = MapTriggerToModel(trigger);
                        model.TriggersList.Add(triggerModel);
                    }
                    
                    // Update the display string
                    model.Triggers = string.Join(", ", model.TriggersList.Select(t => t.Descriptor));
                }

                // Map Settings & Conditions
                if (defSettings != null)
                {
                    model.OnlyIfIdle = defSettings.RunOnlyIfIdle;
                    model.OnlyIfAC = defSettings.DisallowStartIfOnBatteries;
                    model.OnlyIfNetwork = defSettings.RunOnlyIfNetworkAvailable;
                    model.WakeToRun = defSettings.WakeToRun;
                    model.StopOnBattery = defSettings.StopIfGoingOnBatteries;
                    model.RunIfMissed = defSettings.StartWhenAvailable;
                    model.RestartOnFailure = defSettings.RestartCount > 0;
                    model.RestartCount = defSettings.RestartCount;
                    
                    if (defSettings.ExecutionTimeLimit != TimeSpan.Zero)
                    {
                            try { model.StopIfRunsLongerThan = System.Xml.XmlConvert.ToString(defSettings.ExecutionTimeLimit); } catch {}
                    }
                    
                    if (defSettings.RestartInterval != TimeSpan.Zero)
                    {
                            try { model.RestartInterval = System.Xml.XmlConvert.ToString(defSettings.RestartInterval); } catch {}
                    }
                }
                
                return model;
            }
        }

        private void EnumFolderTasks(TaskFolder folder, List<ScheduledTaskModel> tasks, bool recursive = true)
        {
            foreach (var task in folder.Tasks)
            {
                try
                {
                    // Cache Definition to avoid repeated COM calls (EXTREMELY EXPENSIVE)
                    var def = task.Definition;
                    
                    // Cache Triggers and Actions collections once
                    var defTriggers = def.Triggers;
                    var defActions = def.Actions;
                    var defSettings = def.Settings;

                    var model = new ScheduledTaskModel
                    {
                        Name = task.Name,
                        Path = task.Path,
                        State = task.State.ToString(),
                        Author = def.RegistrationInfo.Author ?? "",
                        Description = def.RegistrationInfo.Description ?? "",
                        LastRunTime = task.LastRunTime == DateTime.MinValue ? null : (DateTime?)task.LastRunTime,
                        NextRunTime = task.NextRunTime == DateTime.MinValue ? null : (DateTime?)task.NextRunTime,
                        IsEnabled = task.Enabled,
                        RunWithHighestPrivileges = def.Principal.RunLevel == TaskRunLevel.Highest,
                        Triggers = (defTriggers != null) 
                            ? string.Join(", ", defTriggers.Cast<Trigger>().Select(t => t.ToString())) 
                            : ""
                    };

                    // Map Actions
                    // Map Actions
                    if (defActions != null)
                    {
                        foreach (var action in defActions)
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

                    // Map Triggers and Repetition
                    if (defTriggers != null)
                    {
                        foreach (var trigger in defTriggers)
                        {
                            var triggerModel = MapTriggerToModel(trigger);
                            model.TriggersList.Add(triggerModel);
                        }
                        
                        // Update display string
                        model.Triggers = (defTriggers != null) 
                            ? string.Join(", ", defTriggers.Cast<Trigger>().Select(t => t.ToString())) 
                            : "";
                    }

                    // Map Settings & Conditions
                    if (defSettings != null)
                    {
                        model.OnlyIfIdle = defSettings.RunOnlyIfIdle;
                        model.OnlyIfAC = defSettings.DisallowStartIfOnBatteries;
                        model.OnlyIfNetwork = defSettings.RunOnlyIfNetworkAvailable;
                        model.WakeToRun = defSettings.WakeToRun;
                        model.StopOnBattery = defSettings.StopIfGoingOnBatteries;
                        model.RunIfMissed = defSettings.StartWhenAvailable;
                        model.RestartOnFailure = defSettings.RestartCount > 0;
                        model.RestartCount = defSettings.RestartCount;
                        
                        if (defSettings.ExecutionTimeLimit != TimeSpan.Zero)
                        {
                             try { model.StopIfRunsLongerThan = System.Xml.XmlConvert.ToString(defSettings.ExecutionTimeLimit); } catch {}
                        }
                        
                        if (defSettings.RestartInterval != TimeSpan.Zero)
                        {
                             try { model.RestartInterval = System.Xml.XmlConvert.ToString(defSettings.RestartInterval); } catch {}
                        }
                    }

                    tasks.Add(model);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading task {task.Name}: {ex.Message}");
                    // Continue to next task, don't crash
                }
            }

            if (recursive)
            {
                foreach (var subFolder in folder.SubFolders)
                {
                    try
                    {
                        EnumFolderTasks(subFolder, tasks, true);
                    }
                    catch {}
                }
            }
        }

        public void EnableTask(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                if (task != null)
                {
                    task.Enabled = true;
                }
            }
        }

        public void DisableTask(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                if (task != null)
                {
                    task.Enabled = false;
                }
            }
        }

        public void RunTask(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                task?.Run();
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
                if (task != null)
                {
                    task.Folder.DeleteTask(task.Name);
                }
            }
        }

        public void RegisterTask(ScheduledTaskModel model)
        {
            using (var ts = new TaskService())
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = model.Description;
                td.RegistrationInfo.Author = model.Author;
                td.Settings.Enabled = model.IsEnabled;
                
                // Set privilege level
                td.Principal.RunLevel = model.RunWithHighestPrivileges 
                    ? TaskRunLevel.Highest 
                    : TaskRunLevel.LUA;

                foreach (var triggerModel in model.TriggersList)
                {
                    DateTime startTime = DateTime.Today.AddHours(9); // default 9am
                    if (!string.IsNullOrWhiteSpace(triggerModel.ScheduleInfo) && DateTime.TryParse(triggerModel.ScheduleInfo, out var parsedStart))
                    {
                        startTime = parsedStart;
                    }

                    Trigger t;
                    switch (triggerModel.TriggerType)
                    {
                        case "Daily":
                            t = new DailyTrigger 
                            { 
                                StartBoundary = startTime,
                                DaysInterval = triggerModel.DailyInterval
                            };
                            break;
                        case "Weekly":
                            t = new WeeklyTrigger 
                            { 
                                StartBoundary = startTime,
                                WeeksInterval = triggerModel.WeeklyInterval,
                                DaysOfWeek = GetDaysOfWeek(triggerModel.WeeklyDays)
                            };
                            break;
                        case "Monthly":
                            if (triggerModel.MonthlyIsDayOfWeek)
                            {
                                t = new MonthlyDOWTrigger 
                                { 
                                    StartBoundary = startTime,
                                    MonthsOfYear = GetMonths(triggerModel.MonthlyMonths),
                                    DaysOfWeek = GetDayOfWeek(triggerModel.MonthlyDayOfWeek),
                                    WeeksOfMonth = GetWhichWeek(triggerModel.MonthlyWeek)
                                };
                            }
                            else
                            {
                                var mt = new MonthlyTrigger 
                                { 
                                    StartBoundary = startTime,
                                    MonthsOfYear = GetMonths(triggerModel.MonthlyMonths),
                                    DaysOfMonth = triggerModel.MonthlyDays.Where(d => d <= 31).ToArray()
                                };
                                if (triggerModel.MonthlyDays.Contains(32)) mt.RunOnLastDayOfMonth = true;
                                t = mt;
                            }
                            break;
                        case "AtLogon":
                            t = new LogonTrigger();
                            break;
                        case "AtStartup":
                            t = new BootTrigger();
                            break;
                        case "One Time": 
                        case "Once":
                            t = new TimeTrigger { StartBoundary = startTime };
                            break;
                        case "Event":
                            var et = new EventTrigger();
                            string log = string.IsNullOrWhiteSpace(triggerModel.EventLog) ? "Application" : triggerModel.EventLog;
                            string query = "*";
                            
                            if (!string.IsNullOrWhiteSpace(triggerModel.EventSource) || triggerModel.EventId.HasValue)
                            {
                                string conditions = "";
                                if (!string.IsNullOrWhiteSpace(triggerModel.EventSource))
                                    conditions += $"Provider[@Name='{triggerModel.EventSource}']";
                                    
                                if (triggerModel.EventId.HasValue)
                                {
                                    if (conditions.Length > 0) conditions += " and ";
                                    conditions += $"(EventID={triggerModel.EventId})";
                                }
                                
                                query = $"*[System[{conditions}]]";
                            }
                            
                            et.Subscription = $"<QueryList><Query Id=\"0\" Path=\"{log}\"><Select Path=\"{log}\">{query}</Select></Query></QueryList>";
                            t = et;
                            break;
                        default:
                            t = new DailyTrigger { StartBoundary = startTime };
                            break;
                    }

                    // Set Expiration
                    if (triggerModel.ExpirationDate.HasValue)
                    {
                        t.EndBoundary = triggerModel.ExpirationDate.Value;
                    }

                    // Apply repetition
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
                                try { ((dynamic)t).RandomDelay = System.Xml.XmlConvert.ToTimeSpan(triggerModel.RandomDelay); } catch {}
                            }
                        }
                        catch {}
                    }

                    td.Triggers.Add(t);
                }

                // Action handling with arguments support
                // Action handling with arguments support
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

                // Apply conditions
                td.Settings.RunOnlyIfIdle = model.OnlyIfIdle;
                td.Settings.DisallowStartIfOnBatteries = model.OnlyIfAC;
                td.Settings.StopIfGoingOnBatteries = model.StopOnBattery; // Use the new property
                td.Settings.RunOnlyIfNetworkAvailable = model.OnlyIfNetwork;
                td.Settings.WakeToRun = model.WakeToRun;

                // Apply settings
                if (!string.IsNullOrWhiteSpace(model.StopIfRunsLongerThan))
                {
                    try
                    {
                        td.Settings.ExecutionTimeLimit = System.Xml.XmlConvert.ToTimeSpan(model.StopIfRunsLongerThan);
                    }
                    catch
                    {
                        // Invalid format, use default
                        td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(72);
                    }
                }
                
                td.Settings.StartWhenAvailable = model.RunIfMissed;

                if (model.RestartOnFailure && !string.IsNullOrWhiteSpace(model.RestartInterval))
                {
                    try
                    {
                        td.Settings.RestartInterval = System.Xml.XmlConvert.ToTimeSpan(model.RestartInterval);
                        td.Settings.RestartCount = model.RestartCount;
                    }
                    catch
                    {
                        // Invalid format, skip restart settings
                    }
                }

                // Register with proper flags to ensure task is visible and executable
                ts.RootFolder.RegisterTaskDefinition(
                    model.Name, 
                    td,
                    TaskCreation.CreateOrUpdate,
                    null, // Use current user
                    null, // No password needed for current user
                    TaskLogonType.InteractiveToken
                );
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
                    System.IO.File.WriteAllText(outputPath, task.Definition.XmlText);
                }
                else
                {
                    throw new System.Exception($"Task '{taskPath}' not found.");
                }
            }
        }

        public List<TaskHistoryEntry> GetTaskHistory(string taskPath)
        {
            var history = new List<TaskHistoryEntry>();
            
            try
            {
                // Query the Task Scheduler Event Log
                string query = $"*[System/Provider[@Name='Microsoft-Windows-TaskScheduler'] and EventData[Data[@Name='TaskName']='{taskPath}']]";
                
                EventLogQuery eventsQuery = new EventLogQuery("Microsoft-Windows-TaskScheduler/Operational", PathType.LogName, query);
                EventLogReader logReader = new EventLogReader(eventsQuery);
                
                EventRecord record;
                while ((record = logReader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        var entry = new TaskHistoryEntry
                        {
                            Time = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown",
                            Result = GetEventResult(record.Id),
                            ExitCode = GetEventExitCode(record),
                            Message = record.FormatDescription() ?? record.LevelDisplayName ?? ""
                        };
                        history.Add(entry);
                    }
                }
                
                // Sort by time descending (newest first)
                history = history.OrderByDescending(h => h.Time).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading task history: {ex.Message}");
                // Return empty list on error
            }
            
            return history;
        }

        private string GetEventResult(int eventId)
        {
            return eventId switch
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
        }

        private string GetEventExitCode(EventRecord record)
        {
            try
            {
                // Look for exit code in event properties
                if (record.Properties != null && record.Properties.Count > 0)
                {
                    foreach (var prop in record.Properties)
                    {
                        if (prop.Value is int exitCode && exitCode != 0)
                            return exitCode.ToString();
                    }
                }
                return "0";
            }
            catch
            {
                return "-";
            }
        }

        private void MapMonths(MonthsOfTheYear moy, List<string> months)
        {
            if ((moy & MonthsOfTheYear.January) != 0) months.Add("January");
            if ((moy & MonthsOfTheYear.February) != 0) months.Add("February");
            if ((moy & MonthsOfTheYear.March) != 0) months.Add("March");
            if ((moy & MonthsOfTheYear.April) != 0) months.Add("April");
            if ((moy & MonthsOfTheYear.May) != 0) months.Add("May");
            if ((moy & MonthsOfTheYear.June) != 0) months.Add("June");
            if ((moy & MonthsOfTheYear.July) != 0) months.Add("July");
            if ((moy & MonthsOfTheYear.August) != 0) months.Add("August");
            if ((moy & MonthsOfTheYear.September) != 0) months.Add("September");
            if ((moy & MonthsOfTheYear.October) != 0) months.Add("October");
            if ((moy & MonthsOfTheYear.November) != 0) months.Add("November");
            if ((moy & MonthsOfTheYear.December) != 0) months.Add("December");
        }
        
        private DaysOfTheWeek GetDaysOfWeek(List<string> days)
        {
            DaysOfTheWeek dow = 0;
            if (days.Contains("Monday")) dow |= DaysOfTheWeek.Monday;
            if (days.Contains("Tuesday")) dow |= DaysOfTheWeek.Tuesday;
            if (days.Contains("Wednesday")) dow |= DaysOfTheWeek.Wednesday;
            if (days.Contains("Thursday")) dow |= DaysOfTheWeek.Thursday;
            if (days.Contains("Friday")) dow |= DaysOfTheWeek.Friday;
            if (days.Contains("Saturday")) dow |= DaysOfTheWeek.Saturday;
            if (days.Contains("Sunday")) dow |= DaysOfTheWeek.Sunday;
            return dow == 0 ? DaysOfTheWeek.Monday : dow; // Default to Monday if none selected
        }
        
        private MonthsOfTheYear GetMonths(List<string> months)
        {
            MonthsOfTheYear moy = 0;
            if (months.Contains("January")) moy |= MonthsOfTheYear.January;
            if (months.Contains("February")) moy |= MonthsOfTheYear.February;
            if (months.Contains("March")) moy |= MonthsOfTheYear.March;
            if (months.Contains("April")) moy |= MonthsOfTheYear.April;
            if (months.Contains("May")) moy |= MonthsOfTheYear.May;
            if (months.Contains("June")) moy |= MonthsOfTheYear.June;
            if (months.Contains("July")) moy |= MonthsOfTheYear.July;
            if (months.Contains("August")) moy |= MonthsOfTheYear.August;
            if (months.Contains("September")) moy |= MonthsOfTheYear.September;
            if (months.Contains("October")) moy |= MonthsOfTheYear.October;
            if (months.Contains("November")) moy |= MonthsOfTheYear.November;
            if (months.Contains("December")) moy |= MonthsOfTheYear.December;
            return moy == 0 ? MonthsOfTheYear.AllMonths : moy;
        }
        
        private DaysOfTheWeek GetDayOfWeek(string day)
        {
            return day switch
            {
                "Monday" => DaysOfTheWeek.Monday,
                "Tuesday" => DaysOfTheWeek.Tuesday,
                "Wednesday" => DaysOfTheWeek.Wednesday,
                "Thursday" => DaysOfTheWeek.Thursday,
                "Friday" => DaysOfTheWeek.Friday,
                "Saturday" => DaysOfTheWeek.Saturday,
                "Sunday" => DaysOfTheWeek.Sunday,
                _ => DaysOfTheWeek.Monday
            };
        }
        
        private WhichWeek GetWhichWeek(string week)
        {
             return week switch
             {
                 "First" => WhichWeek.FirstWeek,
                 "Second" => WhichWeek.SecondWeek,
                 "Third" => WhichWeek.ThirdWeek,
                 "Fourth" => WhichWeek.FourthWeek,
                 "Last" => WhichWeek.LastWeek,
                 _ => WhichWeek.FirstWeek
             };
        }
        private TaskTriggerModel MapTriggerToModel(Trigger trigger)
        {
            var model = new TaskTriggerModel();
            
            // Simple type mapping
            if (trigger is DailyTrigger dt) 
            {
                model.TriggerType = "Daily";
                model.DailyInterval = dt.DaysInterval;
            }
            else if (trigger is WeeklyTrigger wt) 
            {
                model.TriggerType = "Weekly";
                model.WeeklyInterval = wt.WeeksInterval;
                
                // Map DaysOfWeek
                if ((wt.DaysOfWeek & DaysOfTheWeek.Monday) != 0) model.WeeklyDays.Add("Monday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Tuesday) != 0) model.WeeklyDays.Add("Tuesday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Wednesday) != 0) model.WeeklyDays.Add("Wednesday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Thursday) != 0) model.WeeklyDays.Add("Thursday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Friday) != 0) model.WeeklyDays.Add("Friday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Saturday) != 0) model.WeeklyDays.Add("Saturday");
                if ((wt.DaysOfWeek & DaysOfTheWeek.Sunday) != 0) model.WeeklyDays.Add("Sunday");
            }
            else if (trigger is MonthlyTrigger mt) 
            {
                model.TriggerType = "Monthly";
                model.MonthlyIsDayOfWeek = false;
                
                // Map Months
                MapMonths(mt.MonthsOfYear, model.MonthlyMonths);
                
                // Map Days
                model.MonthlyDays.AddRange(mt.DaysOfMonth);
                if (mt.RunOnLastDayOfMonth) model.MonthlyDays.Add(32); // 32 = Last
            }
            else if (trigger is MonthlyDOWTrigger mdt)
            {
                    model.TriggerType = "Monthly";
                    model.MonthlyIsDayOfWeek = true;
                    
                    // Map Months
                    MapMonths(mdt.MonthsOfYear, model.MonthlyMonths);
                    
                    // Map Week
                    if (mdt.WeeksOfMonth == WhichWeek.FirstWeek) model.MonthlyWeek = "First";
                    else if (mdt.WeeksOfMonth == WhichWeek.SecondWeek) model.MonthlyWeek = "Second";
                    else if (mdt.WeeksOfMonth == WhichWeek.ThirdWeek) model.MonthlyWeek = "Third";
                    else if (mdt.WeeksOfMonth == WhichWeek.FourthWeek) model.MonthlyWeek = "Fourth";
                    else if (mdt.WeeksOfMonth == WhichWeek.LastWeek) model.MonthlyWeek = "Last";
                    
                    // Map DayOfWeek
                    if (mdt.DaysOfWeek == DaysOfTheWeek.Monday) model.MonthlyDayOfWeek = "Monday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Tuesday) model.MonthlyDayOfWeek = "Tuesday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Wednesday) model.MonthlyDayOfWeek = "Wednesday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Thursday) model.MonthlyDayOfWeek = "Thursday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Friday) model.MonthlyDayOfWeek = "Friday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Saturday) model.MonthlyDayOfWeek = "Saturday";
                    else if (mdt.DaysOfWeek == DaysOfTheWeek.Sunday) model.MonthlyDayOfWeek = "Sunday";
            }
            else if (trigger is LogonTrigger) model.TriggerType = "AtLogon";
            else if (trigger is BootTrigger) model.TriggerType = "AtStartup";
            else if (trigger is EventTrigger et)
            {
                model.TriggerType = "Event";
                try
                {
                    // Basic parsing of Subscription XML to extract Log, Source, EventID
                    string sub = et.Subscription ?? "";
                    
                    // Extract Path (Log)
                    var pathMatch = System.Text.RegularExpressions.Regex.Match(sub, "Path=\"([^\"]+)\"");
                    if (pathMatch.Success) model.EventLog = pathMatch.Groups[1].Value;
                    
                    // Extract Source
                    var sourceMatch = System.Text.RegularExpressions.Regex.Match(sub, "Provider\\[@Name='([^']+)'\\]");
                    if (sourceMatch.Success) model.EventSource = sourceMatch.Groups[1].Value;
                    
                    // Extract EventID
                    var idMatch = System.Text.RegularExpressions.Regex.Match(sub, "EventID=(\\d+)");
                    if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out int eid)) model.EventId = eid;
                }
                catch {}
            }
            else if (trigger is TimeTrigger) model.TriggerType = "Once";

            // Map Start Boundary to ScheduleInfo
            if (trigger.StartBoundary != DateTime.MinValue)
            {
                model.ScheduleInfo = trigger.StartBoundary.ToString("yyyy-MM-dd HH:mm:ss");
            }
            
            // Expiration
            if (trigger.EndBoundary != DateTime.MaxValue)
            {
                    model.ExpirationDate = trigger.EndBoundary;
            }

            // Repetition
            if (trigger.Repetition.Interval != TimeSpan.Zero)
            {
                try { model.RepetitionInterval = System.Xml.XmlConvert.ToString(trigger.Repetition.Interval); } catch {}
            }
            if (trigger.Repetition.Duration != TimeSpan.Zero)
            {
                try { model.RepetitionDuration = System.Xml.XmlConvert.ToString(trigger.Repetition.Duration); } catch {}
            }
            
            // Random Delay
            try 
            { 
                var dTrigger = (dynamic)trigger;
                if (dTrigger.RandomDelay != TimeSpan.Zero)
                {
                    model.RandomDelay = System.Xml.XmlConvert.ToString(dTrigger.RandomDelay); 
                }
            } 
            catch {}

            return model;
        }

        public string GetTaskXml(string path)
        {
            using (var ts = new TaskService())
            {
                var task = ts.GetTask(path);
                return task?.Xml ?? "";
            }
        }

        public void UpdateTaskXml(string path, string xml)
        {
            using (var ts = new TaskService())
            {
                ts.RootFolder.RegisterTask(path, xml, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
            }
        }
        public TaskFolderModel GetFolderStructure()
        {
            using (var ts = new TaskService())
            {
                var root = new TaskFolderModel { Name = "Task Scheduler Library", Path = "\\" };
                EnumFolders(ts.RootFolder, root);
                return root;
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

        public void CreateFolder(string path)
        {
            using (var ts = new TaskService())
            {
                // Ensure path starts with \
                if (!path.StartsWith("\\")) path = "\\" + path;

                // Check if already exists
                try 
                {
                    if (ts.GetFolder(path) != null) return;
                }
                catch {}

                // Split path to find parent
                var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;

                var folderName = parts.Last();
                var parentPath = "\\" + string.Join("\\", parts.Take(parts.Length - 1));
                
                TaskFolder parentFolder;
                if (string.IsNullOrWhiteSpace(parentPath) || parentPath == "\\")
                    parentFolder = ts.RootFolder;
                else
                    parentFolder = ts.GetFolder(parentPath);
                
                if (parentFolder == null) 
                    throw new Exception($"Parent folder '{parentPath}' not found. Please create parent folders first.");
                
                parentFolder.CreateFolder(folderName);
            }
        }

        public void DeleteFolder(string path)
        {
            using (var ts = new TaskService())
            {
                var folder = ts.GetFolder(path);
                if (folder != null && folder.Path != "\\")
                {
                   var parent = folder.Parent;
                   parent?.DeleteFolder(folder.Name);
                }
            }
        }

        public void RegisterTaskFromXml(string folderPath, string taskName, string xmlContent)
        {
            using (var ts = new TaskService())
            {
                var folder = ts.GetFolder(folderPath);
                if (folder == null) throw new DirectoryNotFoundException($"Folder '{folderPath}' not found.");
                
                folder.RegisterTask(taskName, xmlContent, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
            }
        }
    }
}
