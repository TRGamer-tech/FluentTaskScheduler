using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Threading;
using FluentTaskScheduler.Services;
using Microsoft.Win32.TaskScheduler;
using Xunit;
using Xunit.Abstractions;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Tests.Integration
{
    /// <summary>
    /// Exploratory checks against the real Task Scheduler on the machine running the tests.
    /// They record how the library and the current services behave; the findings are printed to
    /// the test output rather than asserted.
    /// </summary>
    public class SchedulerProbes
    {
        private readonly ITestOutputHelper _out;
        public SchedulerProbes(ITestOutputHelper output) => _out = output;

        private static readonly string[] IgnoredOnCompare = { "Date", "URI" };

        /// <summary>A task that uses about every setting the editor does not model.</summary>
        private static TaskDefinition BuildRichDefinition(TaskService ts)
        {
            var td = ts.NewTask();
            td.RegistrationInfo.Author = "FTS Tests";
            td.RegistrationInfo.Description = "Rich task";
            td.RegistrationInfo.Documentation = "docs here";
            td.RegistrationInfo.Source = "integration test";
            td.RegistrationInfo.Version = new Version(3, 1);

            td.Settings.Priority = (ProcessPriorityClass)5;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            td.Settings.DeleteExpiredTaskAfter = TimeSpan.FromDays(90);
            td.Settings.MultipleInstances = TaskInstancesPolicy.Queue;
            td.Settings.AllowHardTerminate = false;
            td.Settings.StartWhenAvailable = true;
            td.Settings.WakeToRun = true;
            td.Settings.Hidden = true;
            td.Settings.Enabled = false;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.DisallowStartOnRemoteAppSession = true;
            td.Settings.IdleSettings.IdleDuration = TimeSpan.FromMinutes(20);
            td.Settings.IdleSettings.WaitTimeout = TimeSpan.FromHours(3);
            td.Settings.IdleSettings.RestartOnIdle = true;
            td.Settings.RestartInterval = TimeSpan.FromMinutes(5);
            td.Settings.RestartCount = 4;

            td.Triggers.Add(new WeeklyTrigger(DaysOfTheWeek.Monday | DaysOfTheWeek.Thursday, 2)
            {
                StartBoundary = new DateTime(2030, 1, 2, 3, 4, 5),
                EndBoundary = new DateTime(2031, 1, 2, 3, 4, 5),
                Enabled = false,
                ExecutionTimeLimit = TimeSpan.FromMinutes(30),
                RandomDelay = TimeSpan.FromMinutes(7),
                Repetition = new RepetitionPattern(TimeSpan.FromMinutes(15), TimeSpan.FromHours(2), true)
            });
            td.Triggers.Add(new LogonTrigger { Delay = TimeSpan.FromSeconds(45) });
            td.Triggers.Add(new BootTrigger { Delay = TimeSpan.FromMinutes(2) });
            td.Triggers.Add(new EventTrigger
            {
                Subscription = "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='disk'] and (Level=1 or Level=2) and (EventID=7 or EventID=11)]]</Select></Query></QueryList>",
                Delay = TimeSpan.FromSeconds(30)
            });
            td.Triggers.Add(new MonthlyDOWTrigger(DaysOfTheWeek.Monday | DaysOfTheWeek.Friday, MonthsOfTheYear.January | MonthsOfTheYear.July, WhichWeek.FirstWeek | WhichWeek.ThirdWeek)
            {
                StartBoundary = new DateTime(2030, 2, 1, 8, 0, 0)
            });
            td.Triggers.Add(new SessionStateChangeTrigger(TaskSessionStateChangeType.ConsoleConnect));
            td.Triggers.Add(new IdleTrigger());

            td.Actions.Add(new ExecAction("cmd.exe", "/c exit 0", @"C:\Windows"));
            td.Actions.Add(new ExecAction("cmd.exe", "/c exit 1"));
            td.Principal.RunLevel = TaskRunLevel.Highest;
            return td;
        }

        [SkippableFact]
        public void Library_ReRegisteringAnExistingDefinition()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            using var sandbox = new SchedulerSandbox();

            var td = BuildRichDefinition(sandbox.Service);
            var task = sandbox.Folder.RegisterTaskDefinition("Rich", td, TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);
            string before = task.Xml;
            _out.WriteLine("=== registered XML ===");
            _out.WriteLine(before);

            var settings = task.Definition.Settings;
            _out.WriteLine($"Priority enum={settings.Priority} int={(int)settings.Priority}");
            _out.WriteLine($"ExecutionTimeLimit={settings.ExecutionTimeLimit} DeleteExpiredTaskAfter={settings.DeleteExpiredTaskAfter}");
            _out.WriteLine($"Principal UserId='{task.Definition.Principal.UserId}' GroupId='{task.Definition.Principal.GroupId}' LogonType={task.Definition.Principal.LogonType} Account='{task.Definition.Principal.Account}'");

            // 1. Plain re-registration of the definition object.
            var again = sandbox.Folder.RegisterTaskDefinition("Rich", task.Definition);
            Report("re-register unchanged definition", before, again.Xml);

            // 2. Change one setting in place and re-register.
            var def = sandbox.Service.GetTask(sandbox.PathOf("Rich")).Definition;
            def.Settings.WakeToRun = false;
            var changed = sandbox.Folder.RegisterTaskDefinition("Rich", def);
            Report("change WakeToRun in place", before, changed.Xml);

            // 3. Rebuild the trigger list from clones.
            def = sandbox.Service.GetTask(sandbox.PathOf("Rich")).Definition;
            var clones = def.Triggers.Select(t => (Trigger)t.Clone()).ToList();
            def.Triggers.Clear();
            def.Triggers.AddRange(clones);
            try
            {
                var rebuilt = sandbox.Folder.RegisterTaskDefinition("Rich", def);
                Report("clear + re-add cloned triggers", changed.Xml, rebuilt.Xml);
            }
            catch (Exception ex) { _out.WriteLine("clear + re-add cloned triggers FAILED: " + ex); }

            // 4. Same for actions.
            def = sandbox.Service.GetTask(sandbox.PathOf("Rich")).Definition;
            var actionClones = def.Actions.Select(a => (Microsoft.Win32.TaskScheduler.Action)a.Clone()).ToList();
            def.Actions.Clear();
            def.Actions.AddRange(actionClones);
            try
            {
                var rebuilt = sandbox.Folder.RegisterTaskDefinition("Rich", def);
                Report("clear + re-add cloned actions", changed.Xml, rebuilt.Xml);
            }
            catch (Exception ex) { _out.WriteLine("clear + re-add cloned actions FAILED: " + ex); }
        }

        [SkippableFact]
        public void CurrentServices_EditRoundTrip()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            using var sandbox = new SchedulerSandbox();

            var connection = new TaskSchedulerConnection();
            var events = new TaskEventLogService(connection);
            var crud = new TaskCrudService(connection, events);

            // A task as rich as the definition above, minus the pieces the editor refuses to open.
            var td = BuildRichDefinition(sandbox.Service);
            var system = sandbox.Folder.RegisterTaskDefinition("RichSystem", td, TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);
            RoundTrip(crud, sandbox, system);

            // A plain task the way a user would create it for themselves.
            var simple = sandbox.Service.NewTask();
            simple.RegistrationInfo.Description = "Simple";
            simple.Triggers.Add(new DailyTrigger { StartBoundary = new DateTime(2030, 1, 1, 6, 30, 0) });
            simple.Actions.Add(new ExecAction("cmd.exe", "/c exit 0"));
            simple.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            var mine = sandbox.Folder.RegisterTaskDefinition("SimpleMine", simple);
            RoundTrip(crud, sandbox, mine);
        }

        private void RoundTrip(TaskCrudService crud, SchedulerSandbox sandbox, ScheduledTask task)
        {
            string before = task.Xml;
            var model = crud.GetTaskDetails(task.Path);
            if (model == null) { _out.WriteLine($"{task.Name}: GetTaskDetails returned null"); return; }

            _out.WriteLine($"--- {task.Name}: model as loaded ---");
            _out.WriteLine($"StopIfRunsLongerThan='{model.StopIfRunsLongerThan}' TaskPriority={model.TaskPriority} RunAsUser='{model.RunAsUser}' RunAsSystem={model.RunAsSystem} " +
                           $"DeleteExpiredTaskAfter={model.DeleteExpiredTaskAfter} HasUnsupportedElements={model.HasUnsupportedElements} ({model.UnsupportedElementsDescription})");
            foreach (var trigger in model.TriggersList)
                _out.WriteLine($"  trigger {trigger.TriggerType} '{trigger.ScheduleInfo}' session={trigger.SessionStateChangeType} log={trigger.EventLog} src={trigger.EventSource} id={trigger.EventId}");

            try
            {
                crud.RegisterTask(sandbox.FolderPath, model);
                Report($"{task.Name}: load into the model and save again unchanged", before, sandbox.Service.GetTask(task.Path).Xml);
            }
            catch (Exception ex) { _out.WriteLine($"{task.Name}: RegisterTask FAILED: {ex}"); }
        }

        [SkippableFact]
        public void Library_PrincipalFormats()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            using var sandbox = new SchedulerSandbox();

            void Try(string label, string? userId, TaskLogonType logonType, Action<TaskDefinition>? tweak = null)
            {
                try
                {
                    var td = sandbox.Service.NewTask();
                    td.Actions.Add(new ExecAction("cmd.exe", "/c exit 0"));
                    tweak?.Invoke(td);
                    var task = sandbox.Folder.RegisterTaskDefinition("P_" + label, td, TaskCreation.CreateOrUpdate, userId, null, logonType);
                    var p = task.Definition.Principal;
                    _out.WriteLine($"{label}: UserId='{p.UserId}' GroupId='{p.GroupId}' Account='{p.Account}' LogonType={p.LogonType} RunLevel={p.RunLevel} DisplayName='{p.DisplayName}'");
                }
                catch (Exception ex) { _out.WriteLine($"{label}: FAILED 0x{ex.HResult:X8} {ex.Message}"); }
            }

            _out.WriteLine($"current user: {System.Security.Principal.WindowsIdentity.GetCurrent().Name} sid {System.Security.Principal.WindowsIdentity.GetCurrent().User}");
            Try("default", null, TaskLogonType.InteractiveToken);
            Try("system-name", "SYSTEM", TaskLogonType.ServiceAccount);
            Try("system-full", @"NT AUTHORITY\SYSTEM", TaskLogonType.ServiceAccount);
            Try("system-sid", "S-1-5-18", TaskLogonType.ServiceAccount);
            Try("local-service", @"NT AUTHORITY\LOCAL SERVICE", TaskLogonType.ServiceAccount);
            Try("network-service", @"NT AUTHORITY\NETWORK SERVICE", TaskLogonType.ServiceAccount);
            Try("group-users", @"BUILTIN\Users", TaskLogonType.Group);
            Try("s4u-self", System.Security.Principal.WindowsIdentity.GetCurrent().Name, TaskLogonType.S4U);
            Try("highest", null, TaskLogonType.InteractiveToken, td => td.Principal.RunLevel = TaskRunLevel.Highest);
        }

        [SkippableFact]
        public void Library_StoredPasswordTask_UpdateWithoutPassword()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            using var sandbox = new SchedulerSandbox();

            string user = "ftsprobe" + Guid.NewGuid().ToString("N")[..5];
            string password = "Fts!" + Guid.NewGuid().ToString("N")[..14] + "aA1";
            _out.WriteLine("net user add: " + Run("net", $"user {user} {password} /add"));
            try
            {
                var td = sandbox.Service.NewTask();
                td.Actions.Add(new ExecAction("cmd.exe", "/c exit 0"));
                td.Triggers.Add(new DailyTrigger { StartBoundary = new DateTime(2030, 1, 1, 6, 30, 0) });
                ScheduledTask task;
                try
                {
                    task = sandbox.Folder.RegisterTaskDefinition("Stored", td, TaskCreation.CreateOrUpdate, user, password, TaskLogonType.Password);
                }
                catch (Exception ex) { _out.WriteLine($"initial registration with password FAILED 0x{ex.HResult:X8} {ex.Message}"); return; }

                var p = task.Definition.Principal;
                _out.WriteLine($"registered: UserId='{p.UserId}' LogonType={p.LogonType}");

                void Attempt(string label, System.Action action)
                {
                    try { action(); _out.WriteLine($"{label}: OK"); }
                    catch (Exception ex) { _out.WriteLine($"{label}: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}"); }
                }

                Attempt("RegisterChanges()", () => { var t = sandbox.Service.GetTask(task.Path); t.Definition.Settings.Hidden = true; t.RegisterChanges(); });
                Attempt("CreateOrUpdate, null password, Password logon", () =>
                {
                    var def = sandbox.Service.GetTask(task.Path).Definition;
                    def.Settings.Hidden = true;
                    sandbox.Folder.RegisterTaskDefinition("Stored", def, TaskCreation.CreateOrUpdate, def.Principal.UserId, null, TaskLogonType.Password);
                });
                Attempt("Update, null password, Password logon", () =>
                {
                    var def = sandbox.Service.GetTask(task.Path).Definition;
                    def.Settings.Hidden = true;
                    sandbox.Folder.RegisterTaskDefinition("Stored", def, TaskCreation.Update, def.Principal.UserId, null, TaskLogonType.Password);
                });
                Attempt("CreateOrUpdate, correct password", () =>
                {
                    var def = sandbox.Service.GetTask(task.Path).Definition;
                    def.Settings.Hidden = true;
                    sandbox.Folder.RegisterTaskDefinition("Stored", def, TaskCreation.CreateOrUpdate, def.Principal.UserId, password, TaskLogonType.Password);
                });
                Attempt("CreateOrUpdate, wrong password", () =>
                {
                    var def = sandbox.Service.GetTask(task.Path).Definition;
                    sandbox.Folder.RegisterTaskDefinition("Stored", def, TaskCreation.CreateOrUpdate, def.Principal.UserId, "definitely-wrong", TaskLogonType.Password);
                });
                Attempt("task.Enabled = false (no re-registration)", () => { sandbox.Service.GetTask(task.Path).Enabled = false; });
                _out.WriteLine("final hidden=" + sandbox.Service.GetTask(task.Path).Definition.Settings.Hidden + " enabled=" + sandbox.Service.GetTask(task.Path).Enabled);
            }
            finally
            {
                _out.WriteLine("net user delete: " + Run("net", $"user {user} /delete"));
            }
        }

        [SkippableFact]
        public void Library_TriggerToString_PerCulture()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            using var sandbox = new SchedulerSandbox();
            var task = sandbox.Folder.RegisterTaskDefinition("Rich", BuildRichDefinition(sandbox.Service), TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);

            var original = (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture);
            try
            {
                foreach (string name in new[] { "en-US", "de-DE", "ja-JP", "zh-CN", "fr-FR", "tr-TR" })
                {
                    var culture = new CultureInfo(name);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;
                    foreach (var trigger in sandbox.Service.GetTask(task.Path).Definition.Triggers)
                    {
                        try { _out.WriteLine($"{name} {trigger.GetType().Name}: {trigger}"); }
                        catch (Exception ex) { _out.WriteLine($"{name} {trigger.GetType().Name}: THROWS {ex.GetType().Name} {ex.Message}"); }
                    }
                }
            }
            finally
            {
                (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture) = original;
            }
        }

        [SkippableFact]
        public void CurrentServices_MappingEveryTaskOnThisMachine()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");

            var original = (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture);
            try
            {
                foreach (string name in new[] { "en-US", "de-DE", "ja-JP", "zh-CN" })
                {
                    var culture = new CultureInfo(name);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;

                    using var ts = new TaskService();
                    int total = 0, failed = 0;
                    var failures = new List<string>();
                    var watch = Stopwatch.StartNew();
                    foreach (var task in ts.RootFolder.AllTasks)
                    {
                        total++;
                        try { TaskModelMapper.MapTaskToModel(task); }
                        catch (Exception ex)
                        {
                            failed++;
                            if (failures.Count < 15) failures.Add($"    {task.Path}: {ex.GetType().Name} {ex.Message}");
                        }
                    }
                    _out.WriteLine($"{name}: {total} tasks, {failed} could not be mapped, {watch.ElapsedMilliseconds} ms");
                    foreach (string failure in failures) _out.WriteLine(failure);
                }
            }
            finally
            {
                (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture) = original;
            }
        }

        [SkippableFact]
        public void Machine_TaskSchedulerOperationalLog()
        {
            Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
            const string logName = "Microsoft-Windows-TaskScheduler/Operational";
            try
            {
                using var config = new EventLogConfiguration(logName);
                _out.WriteLine($"{logName}: IsEnabled={config.IsEnabled} MaximumSizeInBytes={config.MaximumSizeInBytes} LogMode={config.LogMode}");
            }
            catch (Exception ex) { _out.WriteLine("EventLogConfiguration FAILED: " + ex); }

            using var sandbox = new SchedulerSandbox();
            var td = sandbox.Service.NewTask();
            td.Actions.Add(new ExecAction("cmd.exe", "/c exit 3"));
            var task = sandbox.Folder.RegisterTaskDefinition("RunOnce", td);
            task.Run();
            Thread.Sleep(4000);
            _out.WriteLine($"after run: State={task.State} LastTaskResult={sandbox.Service.GetTask(task.Path).LastTaskResult} LastRunTime={sandbox.Service.GetTask(task.Path).LastRunTime:o}");

            var history = new TaskEventLogService(new TaskSchedulerConnection()).GetTaskHistory(task.Path);
            _out.WriteLine($"history entries: {history.Count}");
            foreach (var entry in history) _out.WriteLine($"  {entry.Time} id={entry.EventId} result='{entry.Result}' exit={entry.ExitCode} msg='{entry.Message}'");
        }

        private void Report(string title, string before, string after)
        {
            var diff = XmlDiff.Diff(before, after, IgnoredOnCompare);
            _out.WriteLine($"=== {title}: {(diff.Count == 0 ? "identical" : diff.Count + " differences")} ===");
            foreach (string line in diff) _out.WriteLine(line);
        }

        private static string Run(string file, string arguments)
        {
            var info = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return $"exit {process.ExitCode} {output.Trim().Replace("\r\n", " | ")}";
        }
    }
}
