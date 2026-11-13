using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using Xunit.Abstractions;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// The history, the live stream and completion actions against the real Task Scheduler and its
/// real log. Every test leaves the log switched the way it found it.
/// </summary>
[Collection(SchedulerSandbox.Collection)]
public sealed class TaskHistoryIntegrationTests
{
    private const string MissingProgram = @"C:\FtsTests\does-not-exist.exe";
    private const string RunsForHalfAMinute = "/c ping -n 30 127.0.0.1 > nul";

    private readonly ITestOutputHelper _out;
    private readonly WindowsEventLog _eventLog = new();
    private readonly TaskHistory _history;
    private readonly TaskStore _store = new();

    public TaskHistoryIntegrationTests(ITestOutputHelper output)
    {
        _out = output;
        _history = new TaskHistory(_eventLog);
    }

    [SkippableFact]
    public void TheHistoryLog_TellsWhetherItIsOn_AndCanBeSwitchedOn()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: false);

        Assert.Equal(LogState.Disabled, _history.GetLogState());
        Assert.Equal(EnableLogResult.Enabled, _history.EnableLog());
        Assert.Equal(LogState.Enabled, _history.GetLogState());
        Assert.Equal(EnableLogResult.Enabled, _history.EnableLog());
    }

    [SkippableFact]
    public void ALogThatDoesNotExist_IsUnavailable_AndSaysSoInsteadOfThrowingSomethingRaw()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        const string noSuchLog = "FluentTaskScheduler-Tests/NoSuchLog";

        Assert.Equal(LogState.Unavailable, _eventLog.GetState(noSuchLog));
        Assert.Equal(EnableLogResult.Failed, _eventLog.Enable(noSuchLog));
        Assert.Throws<HistoryUnavailableException>(() => _eventLog.Read(noSuchLog, "*", newestFirst: true).ToList());
        var refused = Assert.Throws<HistoryUnavailableException>(() => _eventLog.Subscribe(noSuchLog, "*", _ => { }, _ => { }));
        _out.WriteLine("subscribing to a log that does not exist: " + refused.Message);
    }

    [SkippableFact]
    public void AQueryWindowsCannotParse_IsReportedAsAnUnreadableLog()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");

        Assert.Throws<HistoryUnavailableException>(() => _eventLog.Read(TaskSchedulerLog.Channel, "*[this is not XPath", newestFirst: false).ToList());
    }

    [SkippableFact]
    public void TheHistoryOfRealRuns_IsReadBack_WithTheOutcomeOfEach()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: true);
        using var sandbox = new SchedulerSandbox();
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        string succeeds = Save(sandbox, "Succeeds", "/c exit 0");
        string fails = Save(sandbox, "Fails", "/c exit 3");
        string cannotStart = _store.Save(Draft("CannotStart") with { Actions = [new ActionDraft { Path = MissingProgram }] }, sandbox.FolderPath);
        foreach (string path in new[] { succeeds, fails, cannotStart }) _store.Run(path);

        var succeeded = FinishedRunOf(succeeds);
        Assert.Equal(RunStatus.Succeeded, succeeded.Status);
        Assert.Equal(0u, succeeded.ResultCode);
        Assert.NotNull(succeeded.Started);
        Assert.True(succeeded.Ended >= succeeded.Started);
        Assert.True(succeeded.Started >= before, "The run is older than the test.");
        Assert.NotEqual("", succeeded.User);

        var failed = FinishedRunOf(fails);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Equal(3u, failed.ResultCode);

        var notStarted = FinishedRunOf(cannotStart);
        Assert.Equal(RunStatus.CouldNotStart, notStarted.Status);
        Assert.Equal(0x80070002, notStarted.ResultCode);
        Assert.Null(notStarted.Started);

        var events = _history.GetEvents(fails, 100);
        Assert.All(events, e => Assert.Equal(fails, e.TaskPath));
        Assert.Equal(events.OrderByDescending(e => e.RecordId), events);
        Assert.Contains(events, e => e.Kind == TaskEventKind.Registered);
        Assert.Contains(events, e => e.Kind == TaskEventKind.ActionCompleted && e.ResultCode == 3 && e.Action.Length > 0);
        Assert.Equal(events.Take(2), _history.GetEvents(fails, 2));
        Assert.Equal(events, _history.GetEvents(fails, 100, from: before, to: DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Empty(_history.GetEvents(fails, 100, from: DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Empty(_history.GetEvents(fails, 100, to: before));

        var runs = _history.GetRuns(before);
        Assert.Equal(runs.OrderByDescending(run => run.Started ?? run.Ended), runs);
        Assert.Contains(runs, run => run.TaskPath == succeeds && run.Status == RunStatus.Succeeded);
        Assert.Contains(runs, run => run.TaskPath == fails && run.Status == RunStatus.Failed && run.ResultCode == 3);
        Assert.Contains(runs, run => run.TaskPath == cannotStart && run.Status == RunStatus.CouldNotStart);
        Assert.Empty(_history.GetRuns(DateTimeOffset.UtcNow.AddHours(1)));
    }

    [SkippableFact]
    public void ATaskWhoseNameHasQuotesAndAnAmpersand_HasItsHistoryFound()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: true);
        using var sandbox = new SchedulerSandbox();

        string path = Save(sandbox, "Tom's R&D (nightly)", "/c exit 0");
        _store.Run(path);

        Assert.Equal(RunStatus.Succeeded, FinishedRunOf(path).Status);
    }

    [SkippableFact]
    public void TheSystemLog_ShowsWhatHappenedToThisComputerLately()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        var halfAMonthAgo = DateTimeOffset.UtcNow.AddDays(-15);

        var events = _history.GetSystemEvents(halfAMonthAgo, TimeSpan.FromDays(15));
        foreach (var e in events.TakeLast(40)) _out.WriteLine($"{e.Time:O} {e.Kind}");

        // No computer goes a month without starting up, shutting down or someone logging on.
        Assert.NotEmpty(events);
        Assert.Equal(events.OrderBy(e => e.Time), events);
        Assert.All(events, e => Assert.InRange(e.Time, halfAMonthAgo.AddDays(-15), DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [SkippableFact]
    public async Task TheStream_ReportsRealRuns_WithTheirOutcome_AndTheTasksBeingRegistered()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: true);
        using var sandbox = new SchedulerSandbox();
        using var stream = new TaskEventStream(_eventLog);
        var seen = new Seen(stream);
        try
        {
            stream.Start();
            await stream.WhenIdleAsync();

            string succeeds = Save(sandbox, "Succeeds", "/c exit 0");
            string fails = Save(sandbox, "Fails", "/c exit 3");
            string cannotStart = _store.Save(Draft("CannotStart") with { Actions = [new ActionDraft { Path = MissingProgram }] }, sandbox.FolderPath);
            string stopped = Save(sandbox, "Stopped", RunsForHalfAMinute);
            foreach (string path in new[] { succeeds, fails, cannotStart, stopped }) _store.Run(path);

            Assert.True(Eventually(() => seen.Has($"started {stopped}")), "The start of the long-running task was not reported.");
            _store.Stop(stopped);

            Assert.True(Eventually(() => seen.Has($"completed {succeeds} Succeeded 0")), "The successful run was not reported.");
            Assert.True(Eventually(() => seen.Has($"completed {fails} Failed 3")), "The failed run was not reported.");
            Assert.True(Eventually(() => seen.Has($"completed {cannotStart} CouldNotStart {0x80070002}")), "The task that could not start was not reported.");
            Assert.True(Eventually(() => seen.Has($"completed {stopped} Stopped ")), "The stopped run was not reported.");

            string[] lines = seen.Lines;
            Assert.Contains($"Registered {succeeds}", lines);
            Assert.True(Array.IndexOf(lines, $"started {succeeds}") < Array.IndexOf(lines, $"completed {succeeds} Succeeded 0"), "The end was reported before the start.");
            Assert.DoesNotContain($"started {cannotStart}", lines);
            foreach (string path in new[] { succeeds, fails, cannotStart, stopped })
                Assert.Single(lines, line => line.StartsWith($"completed {path} ", StringComparison.Ordinal));
        }
        finally
        {
            foreach (string line in seen.Lines) _out.WriteLine(line);
        }
    }

    [SkippableFact]
    public async Task TheStream_BeginsToReport_OnceTheLogIsSwitchedOn_AndAgainAfterItWasOffInBetween()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: false);
        using var sandbox = new SchedulerSandbox();
        using var stream = new TaskEventStream(_eventLog);
        var seen = new Seen(stream);
        try
        {
            stream.Start();
            await stream.WhenIdleAsync();

            Assert.Equal(EnableLogResult.Enabled, _history.EnableLog());
            await LetTheStreamLook(stream, TaskEventStream.CheckWhileOff);
            string first = Save(sandbox, "AfterSwitchingOn", "/c exit 0");
            _store.Run(first);
            Assert.True(Eventually(() => seen.Has($"completed {first} Succeeded 0")), "Nothing was reported after the log was switched on.");

            LogSwitch.Set(on: false);
            await LetTheStreamLook(stream, TaskEventStream.CheckWhileOn);
            LogSwitch.Set(on: true);
            await LetTheStreamLook(stream, TaskEventStream.CheckWhileOff);
            string second = Save(sandbox, "AfterOffAndOn", "/c exit 0");
            _store.Run(second);
            Assert.True(Eventually(() => seen.Has($"completed {second} Succeeded 0")), "Nothing was reported after the log was switched off and on again.");
        }
        finally
        {
            foreach (string line in seen.Lines) _out.WriteLine(line);
        }
    }

    [SkippableFact]
    public async Task ACompletionAction_StartsTheTaskListedForTheOutcome_AfterTheFirstTaskFinished()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var log = new LogSwitch(on: true);
        using var sandbox = new SchedulerSandbox();
        using var folder = new TempFolder();
        using var settings = new SettingsStore(folder.Paths);
        var notifier = new Notifier();
        using var stream = new TaskEventStream(_eventLog);
        using var service = new CompletionActionService(stream, _store, new TaskRunner(_store, new NoSnooze(), notifier), settings, notifier);
        try
        {
            stream.Start();
            await stream.WhenIdleAsync();

            string followUp = Save(sandbox, "FollowUp", "/c exit 0");
            string alert = Save(sandbox, "Alert", "/c exit 0");
            var actions = new CompletionActions { Enabled = true, OnSuccess = [followUp], OnFailure = [alert] };
            string good = _store.Save(Draft("Good", "/c exit 0") with { CompletionActions = actions }, sandbox.FolderPath);
            string bad = _store.Save(Draft("Bad", "/c exit 3") with { CompletionActions = actions }, sandbox.FolderPath);

            _store.Run(good);
            Assert.True(Eventually(() => notifier.Shown.Contains("CompletionActionStarted Good -> FollowUp (success)")), "The follow-up task was not reported as started.");
            Assert.Equal(RunStatus.Succeeded, FinishedRunOf(followUp).Status);
            Assert.Empty(RunsOf(alert));

            _store.Run(bad);
            Assert.True(Eventually(() => notifier.Shown.Contains("CompletionActionStarted Bad -> Alert (failure)")), "The alert task was not reported as started.");
            Assert.Equal(RunStatus.Succeeded, FinishedRunOf(alert).Status);
            Assert.Single(RunsOf(followUp));
            Assert.Single(RunsOf(alert));
        }
        finally
        {
            foreach (string line in notifier.Shown) _out.WriteLine(line);
        }
    }

    [SkippableFact]
    public void TheCompletionActionsOfATask_AreReadFromItsDescription_AndReadingThemNeverFails()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var actions = new CompletionActions { Enabled = true, OnSuccess = [@"\Some\Task"], OnFailure = [@"\Tom's R&D <nightly>"] };
        string with = _store.Save(Draft("With") with { Description = "What the user wrote", CompletionActions = actions }, sandbox.FolderPath);
        string without = Save(sandbox, "Without", "/c exit 0");

        Assert.Equal(actions, _store.GetCompletionActions(with));
        Assert.Equal(CompletionActions.None, _store.GetCompletionActions(without));
        Assert.Equal(CompletionActions.None, _store.GetCompletionActions(sandbox.PathOf("Missing")));

        // The tasks Windows itself registered: whatever can be read of them, asking must not throw.
        var system = _store.GetTasks().Where(row => row.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)).ToList();
        _out.WriteLine($"{system.Count} tasks under \\Microsoft");
        Assert.NotEmpty(system);
        Assert.All(system, row => Assert.Equal(row.CompletionActions, _store.GetCompletionActions(row.Path)));
    }

    [SkippableFact]
    public void AnUnforcedStart_LeavesARunningTaskAlone_WhileAForcedOneRunsNextToIt()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string busy = Save(sandbox, "Busy", RunsForHalfAMinute);
        string idle = Save(sandbox, "Idle", "/c exit 0");
        int Instances() => sandbox.Service.GetTask(busy).GetInstances().Count;

        _store.Run(idle, force: false);
        Assert.True(Eventually(() => sandbox.Service.GetTask(idle).LastRunTime > DateTime.Now.AddMinutes(-5)), "An unforced start did not start a task that was free to run.");

        _store.Run(busy);
        Assert.True(Eventually(() => Instances() == 1), "The task did not start.");

        // The task's own rule, the default, is not to start a second instance.
        _store.Run(busy, force: false);
        Thread.Sleep(2000);
        Assert.Equal(1, Instances());

        _store.Run(busy);
        Assert.True(Eventually(() => Instances() == 2), "A forced start did not run next to the running instance.");

        _store.Stop(busy);
        Assert.True(Eventually(() => Instances() == 0), "The task did not stop.");
    }

    private static TaskDraft Draft(string name, string arguments = "/c exit 0") => new()
    {
        Name = name,
        Description = "Created by a test",
        Triggers = [new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2031, 1, 1, 8, 0, 0) }],
        Actions = [new ActionDraft { Path = "cmd.exe", Arguments = arguments }]
    };

    /// <returns>The path of the saved task.</returns>
    private string Save(SchedulerSandbox sandbox, string name, string arguments) => _store.Save(Draft(name, arguments), sandbox.FolderPath);

    private IReadOnlyList<TaskRun> RunsOf(string taskPath) => TaskRun.FromEvents(_history.GetEvents(taskPath, 500));

    /// <summary>Waits for the log to show a run of the task that is over. The log lags behind Task Scheduler by about a second.</summary>
    private TaskRun FinishedRunOf(string taskPath)
    {
        TaskRun? finished = null;
        bool found = Eventually(() => (finished = RunsOf(taskPath).FirstOrDefault(run => run.IsFinished)) is not null);
        if (!found || finished is null)
        {
            foreach (var e in _history.GetEvents(taskPath, 500)) _out.WriteLine($"{e.Time:O} {e.EventId} {e.Kind} {e.InstanceId} {e.ResultCode}");
            Assert.Fail($"The log shows no finished run of {taskPath}.");
        }
        return finished;
    }

    /// <summary>Gives the stream's next look at the log time to happen, and waits until it has acted on it.</summary>
    private static async Task LetTheStreamLook(TaskEventStream stream, TimeSpan interval)
    {
        await Task.Delay(interval + TimeSpan.FromSeconds(2));
        await stream.WhenIdleAsync();
    }

    private static bool Eventually(Func<bool> condition, int seconds = 40)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition()) return true;
            Thread.Sleep(250);
        }
        return condition();
    }

    /// <summary>Switches Task Scheduler's history log for one test, and back to how it was afterwards.</summary>
    private sealed class LogSwitch : IDisposable
    {
        private readonly bool _wasOn;

        public LogSwitch(bool on)
        {
            using (var configuration = new EventLogConfiguration(TaskSchedulerLog.Channel)) _wasOn = configuration.IsEnabled;
            Set(on);
        }

        public static void Set(bool on)
        {
            using var configuration = new EventLogConfiguration(TaskSchedulerLog.Channel);
            if (configuration.IsEnabled == on) return;
            configuration.IsEnabled = on;
            configuration.SaveChanges();
        }

        public void Dispose() => Set(_wasOn);
    }

    /// <summary>What a stream reported, one line per notification, collected on its thread and read on the test's.</summary>
    private sealed class Seen
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public Seen(ITaskEventStream stream)
        {
            stream.RunStarted += (_, run) => _lines.Enqueue($"started {run.TaskPath}");
            stream.RunCompleted += (_, run) => _lines.Enqueue($"completed {run.TaskPath} {run.Status} {run.ResultCode}");
            stream.TaskChanged += (_, change) => _lines.Enqueue($"{change.Kind} {change.TaskPath}");
        }

        public string[] Lines => [.. _lines];

        /// <summary>True when a line was reported that begins like this.</summary>
        public bool Has(string beginning) => _lines.Any(line => line.StartsWith(beginning, StringComparison.Ordinal));
    }

    private sealed class Notifier : INotifier
    {
        private readonly ConcurrentQueue<string> _shown = new();

        public string[] Shown => [.. _shown];

        public void TaskStarted(string taskName) => _shown.Enqueue($"TaskStarted {taskName}");
        public void TaskCouldNotStart(string taskName, string reason) => _shown.Enqueue($"TaskCouldNotStart {taskName}: {reason}");
        public void RunSuppressed(string taskName) => _shown.Enqueue($"RunSuppressed {taskName}");
        public void SnoozeStarted(string status) => _shown.Enqueue($"SnoozeStarted {status}");
        public void SnoozeEnded() => _shown.Enqueue("SnoozeEnded");
        public void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess) =>
            _shown.Enqueue($"CompletionActionStarted {finishedTask} -> {startedTask} ({(afterSuccess ? "success" : "failure")})");
        public void TaskRunsSoon(string taskName, int minutes) => _shown.Enqueue($"TaskRunsSoon {taskName} in {minutes}");
        public void HiddenToTray() => _shown.Enqueue("HiddenToTray");
    }

    private sealed class NoSnooze : ISnoozeGate
    {
        public bool BlocksRun(string taskPath) => false;

        public void RecordSuppressedRun(string taskPath, RunOrigin origin)
        {
        }
    }
}
