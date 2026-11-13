using System.Diagnostics;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;
using Microsoft.Win32.TaskScheduler;
using Xunit.Abstractions;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// Reads tasks from the real Task Scheduler into drafts and writes drafts back, checking the
/// stored XML: an edit must change what was edited and nothing else.
/// </summary>
[Collection(SchedulerSandbox.Collection)]
public class TaskDraftRoundTripTests
{
    private readonly ITestOutputHelper _out;
    public TaskDraftRoundTripTests(ITestOutputHelper output) => _out = output;

    private static ScheduledTask RegisterRichSystemTask(SchedulerSandbox sandbox) =>
        sandbox.Folder.RegisterTaskDefinition("Rich", SampleTasks.Rich(sandbox.Service), TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);

    /// <summary>Applies <paramref name="edit"/> to the task the way the editor does and returns what changed in its XML.</summary>
    private List<string> Edit(SchedulerSandbox sandbox, ScheduledTask task, Func<TaskDraft, TaskDraft> edit)
    {
        var current = sandbox.Service.GetTask(task.Path);
        string before = current.Xml;
        var definition = current.Definition;
        var baseline = TaskDraftReader.Read(current);

        TaskDraftWriter.Apply(edit(baseline), baseline, definition);
        sandbox.Folder.RegisterTaskDefinition(task.Name, definition);

        var diff = XmlDiff.Diff(before, sandbox.Service.GetTask(task.Path).Xml, "Date", "URI");
        foreach (string line in diff) _out.WriteLine(line);
        return diff;
    }

    private static ScheduledTask RegisterWithTrigger(SchedulerSandbox sandbox, Trigger trigger)
    {
        var definition = sandbox.Service.NewTask();
        definition.Triggers.Add(trigger);
        definition.Actions.Add(new ExecAction("cmd.exe", "/c exit 0"));
        return sandbox.Folder.RegisterTaskDefinition("Window", definition);
    }

    private static TriggerDraft ReadTrigger(SchedulerSandbox sandbox, ScheduledTask task) =>
        Assert.Single(TaskDraftReader.Read(sandbox.Service.GetTask(task.Path)).Triggers);

    private static void AssertOnlyChanged(List<string> diff, params string[] allowedPaths)
    {
        Assert.NotEmpty(diff);
        var unexpected = diff.Where(line => !allowedPaths.Any(path => line[2..].StartsWith(path, StringComparison.Ordinal))).ToList();
        Assert.True(unexpected.Count == 0, "Unrelated parts of the task changed:\n" + string.Join("\n", unexpected));
    }

    [SkippableFact]
    public void SavingWithoutChanges_LeavesTheTaskIdentical()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        Assert.Empty(Edit(sandbox, RegisterRichSystemTask(sandbox), draft => draft));
    }

    public static IEnumerable<object[]> SingleSettingEdits()
    {
        static object[] Case(string name, Func<TaskDraft, TaskDraft> edit, params string[] paths) => new object[] { name, edit, paths };

        yield return Case("author", d => d with { Author = "Someone else" }, "/Task/RegistrationInfo/Author");
        yield return Case("description", d => d with { Description = "New text" }, "/Task/RegistrationInfo/Description");
        yield return Case("category", d => d with { Category = "Work" }, "/Task/RegistrationInfo/Description");
        yield return Case("enabled", d => d with { Enabled = !d.Enabled }, "/Task/Settings/Enabled");
        yield return Case("hidden", d => d with { Hidden = !d.Hidden }, "/Task/Settings/Hidden");
        yield return Case("highest privileges", d => d with { HighestPrivileges = !d.HighestPrivileges }, "/Task/Principals/Principal/RunLevel");
        yield return Case("run only if idle", d => d with { RunOnlyIfIdle = !d.RunOnlyIfIdle }, "/Task/Settings/RunOnlyIfIdle");
        yield return Case("idle duration", d => d with { RunOnlyIfIdle = true, IdleDuration = TimeSpan.FromMinutes(33) }, "/Task/Settings/RunOnlyIfIdle", "/Task/Settings/IdleSettings/Duration");
        yield return Case("stop on idle end", d => d with { StopOnIdleEnd = !d.StopOnIdleEnd }, "/Task/Settings/IdleSettings/StopOnIdleEnd");
        yield return Case("ac power", d => d with { RunOnlyOnAcPower = !d.RunOnlyOnAcPower }, "/Task/Settings/DisallowStartIfOnBatteries");
        yield return Case("stop on battery", d => d with { StopOnBattery = !d.StopOnBattery }, "/Task/Settings/StopIfGoingOnBatteries");
        yield return Case("network", d => d with { RunOnlyIfNetwork = !d.RunOnlyIfNetwork }, "/Task/Settings/RunOnlyIfNetworkAvailable");
        yield return Case("wake", d => d with { WakeToRun = !d.WakeToRun }, "/Task/Settings/WakeToRun");
        yield return Case("time limit off", d => d with { ExecutionTimeLimit = TimeSpan.Zero }, "/Task/Settings/ExecutionTimeLimit");
        yield return Case("time limit changed", d => d with { ExecutionTimeLimit = TimeSpan.FromMinutes(90) }, "/Task/Settings/ExecutionTimeLimit");
        yield return Case("run if missed", d => d with { RunIfMissed = !d.RunIfMissed }, "/Task/Settings/StartWhenAvailable");
        yield return Case("multiple instances", d => d with { MultipleInstances = TaskInstancesPolicy.Parallel }, "/Task/Settings/MultipleInstancesPolicy");
        yield return Case("priority", d => d with { Priority = ProcessPriorityClass.High }, "/Task/Settings/Priority");
        yield return Case("delete when expired", d => d with { DeleteWhenExpired = !d.DeleteWhenExpired }, "/Task/Settings/DeleteExpiredTaskAfter");
        yield return Case("hard terminate", d => d with { AllowHardTerminate = !d.AllowHardTerminate }, "/Task/Settings/AllowHardTerminate");
        yield return Case("restart count", d => d with { RestartCount = 9 }, "/Task/Settings/RestartOnFailure");
        yield return Case("restart off", d => d with { RestartCount = 0 }, "/Task/Settings/RestartOnFailure");
    }

    [SkippableTheory]
    [MemberData(nameof(SingleSettingEdits))]
    public void ChangingOneSetting_ChangesOnlyThatSetting(string name, Func<TaskDraft, TaskDraft> edit, string[] expectedPaths)
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        _out.WriteLine(name);

        AssertOnlyChanged(Edit(sandbox, RegisterRichSystemTask(sandbox), edit), expectedPaths);
    }

    [SkippableFact]
    public void AnIdleDurationWhileTheIdleConditionIsOff_IsLeftAlone()
    {
        // The editor hides the duration while the condition is off, and what was typed there
        // before, such as a value Task Scheduler refuses, goes along unseen.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        Assert.Empty(Edit(sandbox, RegisterRichSystemTask(sandbox), draft => draft with { IdleDuration = TimeSpan.FromSeconds(30) }));
    }

    [SkippableFact]
    public void SwitchingTheRunTimeLimitOff_StaysOffAndKeepsTheAccount()
    {
        // Issue #28: the limit came back as 72 hours on the next edit and the account was rewritten.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = RegisterRichSystemTask(sandbox);

        Edit(sandbox, task, draft => draft with { ExecutionTimeLimit = TimeSpan.Zero });
        var reloaded = TaskDraftReader.Read(sandbox.Service.GetTask(task.Path));
        Assert.Equal(TimeSpan.Zero, reloaded.ExecutionTimeLimit);
        Assert.Equal(TaskAccountKind.System, reloaded.Account.Kind);

        // Saving again without touching anything must not bring the limit back.
        Assert.Empty(Edit(sandbox, task, draft => draft));
        Assert.Equal(TimeSpan.Zero, TaskDraftReader.Read(sandbox.Service.GetTask(task.Path)).ExecutionTimeLimit);
    }

    [SkippableFact]
    public void ChangingATriggersStartTime_KeepsEverythingElseAboutIt()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var newStart = new DateTime(2030, 6, 7, 9, 15, 0);

        var diff = Edit(sandbox, RegisterRichSystemTask(sandbox), draft => draft with
        {
            Triggers = draft.Triggers.Select((t, i) => i == 0 ? t with { Start = newStart } : t).ToArray()
        });

        AssertOnlyChanged(diff, "/Task/Triggers/CalendarTrigger[0]/StartBoundary");
    }

    /// <summary>
    /// A start kept with its time zone ("synchronize across time zones") is shown and changed on
    /// the local clock, and stays tied to its zone after the edit.
    /// </summary>
    [SkippableFact]
    public void ChangingAStartKeptWithItsTimeZone_KeepsItInATimeZone()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var utc = new DateTime(2030, 1, 1, 6, 30, 0, DateTimeKind.Utc);
        var definition = SampleTasks.Simple(sandbox.Service);
        definition.Triggers[0].StartBoundary = utc;
        var task = sandbox.Folder.RegisterTaskDefinition("Zoned", definition);
        // Task Scheduler keeps such a moment as "Z" or as an offset from UTC; which, is its own business.
        string stored = StartBoundaryIn(task.Xml);
        _out.WriteLine($"Stored: {stored}");
        Assert.Matches(ZoneAtTheEnd, stored);

        Edit(sandbox, task, draft =>
        {
            var trigger = new TriggerViewModel(draft.Triggers[0]);
            Assert.Equal(utc.ToLocalTime().TimeOfDay, trigger.StartTime);
            trigger.StartTime += TimeSpan.FromHours(1);
            Assert.True(trigger.TryBuild(out TriggerDraft changed, out string? problem), problem);
            return draft with { Triggers = [changed] };
        });

        string saved = StartBoundaryIn(sandbox.Service.GetTask(task.Path).Xml);
        _out.WriteLine($"Saved: {saved}");
        Assert.Matches(ZoneAtTheEnd, saved);
        Assert.Equal(utc.AddHours(1), DateTimeOffset.Parse(saved, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime);
    }

    private const string ZoneAtTheEnd = @"(Z|[+-]\d\d:\d\d)$";

    private static string StartBoundaryIn(string taskXml) =>
        System.Xml.Linq.XDocument.Parse(taskXml).Descendants().First(element => element.Name.LocalName == "StartBoundary").Value;

    [SkippableFact]
    public void MovingATriggerPastTheEndItHad_IsSaved()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = RegisterWithTrigger(sandbox, new DailyTrigger { StartBoundary = new DateTime(2030, 11, 1, 6, 30, 0), EndBoundary = new DateTime(2030, 11, 30, 6, 30, 0) });
        DateTime start = new(2030, 12, 1, 6, 30, 0), end = new(2030, 12, 31, 6, 30, 0);

        var diff = Edit(sandbox, task, draft => draft with { Triggers = [draft.Triggers[0] with { Start = start, End = end }] });

        AssertOnlyChanged(diff, "/Task/Triggers/CalendarTrigger/StartBoundary", "/Task/Triggers/CalendarTrigger/EndBoundary");
        var saved = ReadTrigger(sandbox, task);
        Assert.Equal(start, saved.Start);
        Assert.Equal(end, saved.End);
    }

    [SkippableFact]
    public void AnExpiredTrigger_CanBeSetForLater_WithoutAnEnd()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = RegisterWithTrigger(sandbox, new TimeTrigger { StartBoundary = new DateTime(2020, 5, 1, 9, 0, 0), EndBoundary = new DateTime(2020, 5, 2, 9, 0, 0) });
        var next = new DateTime(2031, 5, 1, 9, 0, 0);

        Edit(sandbox, task, draft => draft with { Triggers = [draft.Triggers[0] with { Start = next, End = null }] });

        var saved = ReadTrigger(sandbox, task);
        Assert.Equal(next, saved.Start);
        Assert.Null(saved.End);
    }

    [SkippableFact]
    public void ATriggerSynchronisedAcrossTimeZones_IsReadInLocalTime_AndItsEndCanBeChanged()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var start = new DateTime(2030, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var task = RegisterWithTrigger(sandbox, new DailyTrigger { StartBoundary = start, EndBoundary = start.AddMinutes(30) });
        _out.WriteLine(task.Xml);
        Assert.Contains("<StartBoundary>2030-01-02T08:00:00Z</StartBoundary>", task.Xml);

        var read = ReadTrigger(sandbox, task);
        Assert.Equal(start.ToLocalTime(), read.Start);
        Assert.Equal(DateTimeKind.Local, read.Start?.Kind);
        Assert.Equal(start.AddMinutes(30).ToLocalTime(), read.End);

        DateTime end = start.AddHours(2).ToLocalTime();
        Edit(sandbox, task, draft => draft with { Triggers = [draft.Triggers[0] with { End = end }] });

        var saved = ReadTrigger(sandbox, task);
        Assert.Equal(start.ToLocalTime(), saved.Start);
        Assert.Equal(end, saved.End);
    }

    [SkippableFact]
    public void RemovingATrigger_KeepsTheOthersIntact()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = RegisterRichSystemTask(sandbox);
        var original = XmlDiff.Flatten(task.Xml, "Date", "URI");

        // Drop the idle trigger (the last one).
        Edit(sandbox, task, draft => draft with { Triggers = draft.Triggers.Take(draft.Triggers.Count - 1).ToArray() });

        var after = XmlDiff.Flatten(sandbox.Service.GetTask(task.Path).Xml, "Date", "URI");
        var lost = original.Except(after).Where(line => !line.Contains("/IdleTrigger")).ToList();
        var gained = after.Except(original).ToList();
        Assert.True(lost.Count == 0, "Lost:\n" + string.Join("\n", lost));
        Assert.True(gained.Count == 0, "Gained:\n" + string.Join("\n", gained));
    }

    [SkippableFact]
    public void AddingATrigger_KeepsTheExistingOnesIntact()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = RegisterRichSystemTask(sandbox);
        var original = XmlDiff.Flatten(task.Xml, "Date", "URI");

        var added = new TriggerDraft { Type = TriggerType.Once, Start = new DateTime(2032, 3, 4, 5, 6, 0) };
        Edit(sandbox, task, draft => draft with { Triggers = draft.Triggers.Append(added).ToArray() });

        var after = XmlDiff.Flatten(sandbox.Service.GetTask(task.Path).Xml, "Date", "URI");
        var lost = original.Except(after).ToList();
        Assert.True(lost.Count == 0, "Lost:\n" + string.Join("\n", lost));
        Assert.Contains(after, line => line.Contains("/TimeTrigger/StartBoundary = 2032-03-04T05:06:00"));
    }

    [SkippableFact]
    public void ChangingAnAction_KeepsTheOtherAction()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        var diff = Edit(sandbox, RegisterRichSystemTask(sandbox), draft => draft with
        {
            Actions = draft.Actions.Select((a, i) => i == 1 ? a with { Arguments = "/c exit 2" } : a).ToArray()
        });

        AssertOnlyChanged(diff, "/Task/Actions/Exec[1]/Arguments");
    }

    public static IEnumerable<object[]> NewTriggers()
    {
        var start = new DateTime(2031, 5, 6, 7, 8, 0);
        static object[] Case(TriggerDraft draft) => new object[] { draft };

        yield return Case(new TriggerDraft { Type = TriggerType.Once, Start = start });
        yield return Case(new TriggerDraft { Type = TriggerType.Daily, Start = start, DaysInterval = 3, RandomDelay = TimeSpan.FromMinutes(5) });
        yield return Case(new TriggerDraft { Type = TriggerType.Weekly, Start = start, WeeksInterval = 2, DaysOfWeek = DaysOfTheWeek.Tuesday | DaysOfTheWeek.Sunday });
        yield return Case(new TriggerDraft { Type = TriggerType.Monthly, Start = start, Months = MonthsOfTheYear.March | MonthsOfTheYear.December, DaysOfMonth = 1u | 1u << 14 | 1u << 30 });
        yield return Case(new TriggerDraft { Type = TriggerType.Monthly, Start = start, DaysOfMonth = 0, LastDayOfMonth = true });
        yield return Case(new TriggerDraft { Type = TriggerType.Monthly, Start = start, MonthlyByWeekday = true, DaysOfWeek = DaysOfTheWeek.Friday, Weeks = WhichWeek.SecondWeek });
        yield return Case(new TriggerDraft { Type = TriggerType.Monthly, Start = start, MonthlyByWeekday = true, DaysOfWeek = DaysOfTheWeek.Monday, Weeks = WhichWeek.LastWeek });
        yield return Case(new TriggerDraft { Type = TriggerType.AtLogon });
        yield return Case(new TriggerDraft { Type = TriggerType.AtStartup });
        yield return Case(new TriggerDraft { Type = TriggerType.OnIdle });
        yield return Case(new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("System", "disk", 7) });
        yield return Case(new TriggerDraft { Type = TriggerType.Event, EventSubscription = SampleTasks.CustomEventQuery });
        foreach (TaskSessionStateChangeType change in Enum.GetValues<TaskSessionStateChangeType>())
            yield return Case(new TriggerDraft { Type = TriggerType.SessionStateChange, SessionChange = change });
        yield return Case(new TriggerDraft { Type = TriggerType.Daily, Start = start, End = start.AddYears(1), Enabled = false, RepetitionInterval = TimeSpan.FromMinutes(10), RepetitionDuration = TimeSpan.FromHours(1) });
    }

    [SkippableTheory]
    [MemberData(nameof(NewTriggers))]
    public void ANewTrigger_ReadsBackAsItWasWritten(TriggerDraft trigger)
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        var draft = new TaskDraft
        {
            Name = "New",
            Actions = new[] { new ActionDraft { Path = "cmd.exe", Arguments = "/c exit 0" } },
            Triggers = new[] { trigger }
        };
        var definition = sandbox.Service.NewTask();
        TaskDraftWriter.Apply(draft, null, definition);
        var task = sandbox.Folder.RegisterTaskDefinition(draft.Name, definition);
        _out.WriteLine(task.Xml);

        var reloaded = Assert.Single(TaskDraftReader.Read(task).Triggers);
        Assert.Equal(trigger with { SourceIndex = 0 }, reloaded);
    }

    [SkippableFact]
    public void ANewTask_ReadsBackAsItWasWritten()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        var draft = new TaskDraft
        {
            Name = "Everything",
            Description = "Does things",
            Author = "Me",
            Category = "Work",
            Tags = new[] { "a", "b" },
            Enabled = false,
            Hidden = true,
            RunOnlyIfIdle = true,
            IdleDuration = TimeSpan.FromMinutes(15),
            StopOnIdleEnd = true,
            RunOnlyOnAcPower = true,
            StopOnBattery = true,
            RunOnlyIfNetwork = true,
            WakeToRun = true,
            ExecutionTimeLimit = TimeSpan.FromHours(2),
            RunIfMissed = true,
            MultipleInstances = TaskInstancesPolicy.StopExisting,
            Priority = ProcessPriorityClass.AboveNormal,
            AllowHardTerminate = false,
            RestartCount = 3,
            RestartInterval = TimeSpan.FromMinutes(2),
            Triggers = new[] { new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2031, 1, 1, 8, 0, 0) } },
            Actions = new[]
            {
                new ActionDraft { Path = "cmd.exe", Arguments = "/c exit 0", WorkingDirectory = @"C:\Windows" },
                new ActionDraft { Path = "powershell.exe" }
            }
        };

        var definition = sandbox.Service.NewTask();
        TaskDraftWriter.Apply(draft, null, definition);
        var task = sandbox.Folder.RegisterTaskDefinition(draft.Name, definition);
        var reloaded = TaskDraftReader.Read(task);

        Assert.Equal(draft.Triggers.Select((t, i) => t with { SourceIndex = i }), reloaded.Triggers);
        Assert.Equal(draft.Actions.Select((a, i) => a with { SourceIndex = i }), reloaded.Actions);
        Assert.Equal(draft with { Triggers = reloaded.Triggers, Actions = reloaded.Actions }, reloaded);
    }

    [SkippableFact]
    public void TheAccountOfATask_IsRecognised()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        TaskAccount AccountOf(string name, string? userId, TaskLogonType logonType)
        {
            var task = sandbox.Folder.RegisterTaskDefinition(name, SampleTasks.Simple(sandbox.Service), TaskCreation.CreateOrUpdate, userId, null, logonType);
            return TaskDraftReader.Read(task).Account;
        }

        Assert.Equal(TaskAccount.CurrentUser, AccountOf("Mine", null, TaskLogonType.InteractiveToken));
        Assert.Equal(TaskAccount.System, AccountOf("System", "SYSTEM", TaskLogonType.ServiceAccount));
        Assert.Equal(TaskAccountKind.LocalService, AccountOf("LocalService", @"NT AUTHORITY\LOCAL SERVICE", TaskLogonType.ServiceAccount).Kind);
        Assert.Equal(TaskAccountKind.NetworkService, AccountOf("NetworkService", @"NT AUTHORITY\NETWORK SERVICE", TaskLogonType.ServiceAccount).Kind);
        Assert.Equal(TaskAccountKind.Group, AccountOf("Group", @"BUILTIN\Users", TaskLogonType.Group).Kind);
    }
}
