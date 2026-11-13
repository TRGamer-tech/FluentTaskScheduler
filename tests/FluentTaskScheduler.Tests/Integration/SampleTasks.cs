using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>Task definitions the integration tests register in the sandbox.</summary>
internal static class SampleTasks
{
    /// <summary>
    /// A task that uses settings far beyond what the editor shows: per-trigger time limits and
    /// delays, a disabled trigger, a custom event query, several weeks of the month, idle
    /// details, a version and documentation.
    /// </summary>
    public static TaskDefinition Rich(TaskService service)
    {
        var td = service.NewTask();
        td.RegistrationInfo.Author = "FTS Tests";
        td.RegistrationInfo.Description = "Rich task";
        td.RegistrationInfo.Documentation = "docs here";
        td.RegistrationInfo.Source = "integration test";
        td.RegistrationInfo.Version = new Version(3, 1);

        td.Settings.Priority = (ProcessPriorityClass)5;
        td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(1);
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
        td.Triggers.Add(new EventTrigger { Subscription = CustomEventQuery, Delay = TimeSpan.FromSeconds(30) });
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

    /// <summary>An event subscription the editor's log/source/ID fields cannot express.</summary>
    public const string CustomEventQuery =
        "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='disk'] and (Level=1 or Level=2) and (EventID=7 or EventID=11)]]</Select></Query></QueryList>";

    /// <summary>The smallest useful task: one daily trigger, one action, registered for the current user.</summary>
    public static TaskDefinition Simple(TaskService service)
    {
        var td = service.NewTask();
        td.RegistrationInfo.Description = "Simple";
        td.Triggers.Add(new DailyTrigger { StartBoundary = new DateTime(2030, 1, 1, 6, 30, 0) });
        td.Actions.Add(new ExecAction("cmd.exe", "/c exit 0"));
        return td;
    }
}
