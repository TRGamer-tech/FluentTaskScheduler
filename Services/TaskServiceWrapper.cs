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
        public List<ScheduledTaskModel> GetAllTasks()
        {
            var tasks = new List<ScheduledTaskModel>();
            using (var ts = new TaskService())
            {
                EnumFolderTasks(ts.RootFolder, tasks);
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
                if (defActions != null)
                {
                    var execAction = defActions.FirstOrDefault() as ExecAction;
                    if (execAction != null)
                    {
                        model.ActionCommand = execAction.Path;
                        model.Arguments = execAction.Arguments;
                        model.WorkingDirectory = execAction.WorkingDirectory;
                    }
                }

                // Map Triggers and Repetition
                if (defTriggers != null)
                {
                    var trigger = defTriggers.FirstOrDefault();
                    if (trigger != null)
                    {
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
                            // Use dynamic to access RandomDelay as it is not on the base Trigger class in this version
                            var dTrigger = (dynamic)trigger;
                            if (dTrigger.RandomDelay != TimeSpan.Zero)
                            {
                                model.RandomDelay = System.Xml.XmlConvert.ToString(dTrigger.RandomDelay); 
                            }
                        } 
                        catch {}
                    }
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

        private void EnumFolderTasks(TaskFolder folder, List<ScheduledTaskModel> tasks)
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
                    if (defActions != null)
                    {
                        var execAction = defActions.FirstOrDefault() as ExecAction;
                        if (execAction != null)
                        {
                            model.ActionCommand = execAction.Path;
                            model.Arguments = execAction.Arguments;
                            model.WorkingDirectory = execAction.WorkingDirectory;
                        }
                    }

                    // Map Triggers and Repetition
                    if (defTriggers != null)
                    {
                        var trigger = defTriggers.FirstOrDefault();
                        if (trigger != null)
                        {
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
                                // Use dynamic to access RandomDelay as it is not on the base Trigger class in this version
                                var dTrigger = (dynamic)trigger;
                                if (dTrigger.RandomDelay != TimeSpan.Zero)
                                {
                                    model.RandomDelay = System.Xml.XmlConvert.ToString(dTrigger.RandomDelay); 
                                }
                            } 
                            catch {}
                        }
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

            foreach (var subFolder in folder.SubFolders)
            {
                try
                {
                    EnumFolderTasks(subFolder, tasks);
                }
                catch {}
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

                // Trigger handling based on TriggerType
                DateTime startTime = DateTime.Today.AddHours(9); // default 9am
                if (!string.IsNullOrWhiteSpace(model.ScheduleInfo) && DateTime.TryParse(model.ScheduleInfo, out var parsedStart))
                {
                    startTime = parsedStart;
                }

                switch (model.TriggerType)
                {
                    case "Daily":
                        td.Triggers.Add(new DailyTrigger 
                        { 
                            StartBoundary = startTime,
                            DaysInterval = model.DailyInterval
                        });
                        break;
                    case "Weekly":
                        var wt = new WeeklyTrigger 
                        { 
                            StartBoundary = startTime,
                            WeeksInterval = model.WeeklyInterval,
                            DaysOfWeek = GetDaysOfWeek(model.WeeklyDays)
                        };
                        td.Triggers.Add(wt);
                        break;
                    case "Monthly":
                        if (model.MonthlyIsDayOfWeek)
                        {
                            td.Triggers.Add(new MonthlyDOWTrigger 
                            { 
                                StartBoundary = startTime,
                                MonthsOfYear = GetMonths(model.MonthlyMonths),
                                DaysOfWeek = GetDayOfWeek(model.MonthlyDayOfWeek),
                                WeeksOfMonth = GetWhichWeek(model.MonthlyWeek)
                            });
                        }
                        else
                        {
                            var mt = new MonthlyTrigger 
                            { 
                                StartBoundary = startTime,
                                MonthsOfYear = GetMonths(model.MonthlyMonths),
                                DaysOfMonth = model.MonthlyDays.Where(d => d <= 31).ToArray()
                            };
                            if (model.MonthlyDays.Contains(32)) mt.RunOnLastDayOfMonth = true;
                            td.Triggers.Add(mt);
                        }
                        break;
                    case "AtLogon":
                        td.Triggers.Add(new LogonTrigger());
                        break;
                    case "AtStartup":
                        td.Triggers.Add(new BootTrigger());
                        break;
                    case "Once":
                        td.Triggers.Add(new TimeTrigger { StartBoundary = startTime });
                        break;
                    default:
                        td.Triggers.Add(new DailyTrigger { StartBoundary = startTime });
                        break;
                }
                
                // Set Expiration
                if (model.ExpirationDate.HasValue)
                {
                    foreach (Trigger t in td.Triggers)
                    {
                        t.EndBoundary = model.ExpirationDate.Value;
                    }
                }

                // Apply repetition to all triggers
                if (!string.IsNullOrWhiteSpace(model.RepetitionInterval))
                {
                    try
                    {
                        foreach (Trigger trigger in td.Triggers)
                        {
                            trigger.Repetition.Interval = System.Xml.XmlConvert.ToTimeSpan(model.RepetitionInterval);
                            if (!string.IsNullOrWhiteSpace(model.RepetitionDuration))
                            {
                                trigger.Repetition.Duration = System.Xml.XmlConvert.ToTimeSpan(model.RepetitionDuration);
                            }
                            
                            if (!string.IsNullOrWhiteSpace(model.RandomDelay))
                            {
                                try { ((dynamic)trigger).RandomDelay = System.Xml.XmlConvert.ToTimeSpan(model.RandomDelay); } catch {}
                            }
                        }
                    }
                    catch
                    {
                        // Invalid repetition format, skip
                    }
                }

                // Action handling with arguments support
                if (!string.IsNullOrWhiteSpace(model.ActionCommand))
                {
                    var action = new ExecAction(model.ActionCommand, model.Arguments, model.WorkingDirectory);
                    td.Actions.Add(action);
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
    }
}
