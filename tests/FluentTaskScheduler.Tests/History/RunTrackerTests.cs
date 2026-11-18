using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

/// <summary>Following runs while their events come in one by one, as the live stream does.</summary>
public sealed class RunTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    private readonly RunTracker _tracker = new(capacity: 100);

    private static List<TaskEvent> EventsOf(string taskName) =>
        Evidence.Of(taskName).Select(TaskEvent.From).OfType<TaskEvent>().ToList();

    /// <summary>Feeds the events as if they all arrived at <paramref name="received"/>; returns what was reported as started.</summary>
    private List<TaskRun> Feed(IEnumerable<TaskEvent> events, DateTimeOffset received) =>
        events.Select(e => _tracker.Add(e, received)).OfType<TaskRun>().ToList();

    [Fact]
    public void ARun_IsReportedAsStarted_ByTheEventThatShowsItStarting_AndOnlyByThat()
    {
        var events = EventsOf("A_ExitZero");
        int start = events.FindIndex(e => e.Kind == TaskEventKind.RunStarted);

        Assert.Empty(Feed(events.Take(start), T0));

        var started = _tracker.Add(events[start], T0);
        Assert.NotNull(started);
        Assert.Equal(RunStatus.Running, started.Status);
        Assert.Equal(events[start].Time, started.Started);
        Assert.Equal(Evidence.Folder + @"\A_ExitZero", started.TaskPath);

        Assert.Empty(Feed(events.Skip(start + 1), T0));
    }

    [Fact]
    public void ATaskThatCouldNotStart_IsNeverReportedAsStarted()
    {
        Assert.Empty(Feed(EventsOf("C_MissingExe"), T0));
        Assert.Empty(Feed(EventsOf("J_LogonFails"), T0));
    }

    [Fact]
    public void ARunWithoutAStartedEvent_IsReportedAsStarted_WhenItsFirstActionIsLaunched()
    {
        // First action could not be launched, the second one runs: there is no event 100.
        var events = EventsOf("K2_FirstMissing");

        var started = Assert.Single(Feed(events, T0));

        Assert.Equal(events.First(e => e.Kind == TaskEventKind.ActionStarted).Time, started.Started);
    }

    [Fact]
    public void ARunThatIsOver_IsHandedOutOnce_AndOnlyAfterItHasBeenQuiet()
    {
        Feed(EventsOf("A_ExitZero"), T0);

        Assert.Empty(_tracker.TakeFinished(quietSince: T0 - TimeSpan.FromTicks(1)));
        Assert.Equal(T0, _tracker.OldestSettling);

        var run = Assert.Single(_tracker.TakeFinished(quietSince: T0));
        Assert.Equal(RunStatus.Succeeded, run.Status);

        Assert.Empty(_tracker.TakeFinished(quietSince: T0 + Quiet));
        Assert.Null(_tracker.OldestSettling);
        Assert.Empty(_tracker.Runs());
    }

    [Fact]
    public void ARunThatIsStillGoing_IsNotHandedOut_HoweverQuietItIs()
    {
        Feed(EventsOf("D_Stopped").Where(e => e.RecordId <= 57), T0);

        Assert.Empty(_tracker.TakeFinished(quietSince: T0 + TimeSpan.FromHours(5)));
        Assert.Null(_tracker.OldestSettling);
        Assert.Equal(RunStatus.Running, Assert.Single(_tracker.Runs()).Status);
    }

    [Fact]
    public void TheReasonForAStop_ThatArrivesAfterTheCompletion_IsWaitedFor()
    {
        // A run that hit its time limit: completion first, the reason after it.
        var events = EventsOf("F_TimeLimit");
        Feed(events.Where(e => e.Kind != TaskEventKind.TimedOut), T0);
        Feed(events.Where(e => e.Kind == TaskEventKind.TimedOut), T0 + TimeSpan.FromMilliseconds(300));

        Assert.Empty(_tracker.TakeFinished(quietSince: T0));

        var run = Assert.Single(_tracker.TakeFinished(quietSince: T0 + TimeSpan.FromMilliseconds(300)));
        Assert.Equal(RunStatus.TimedOut, run.Status);
    }

    [Fact]
    public void AfterAFailedLaunch_TheRunIsOnlyOver_IfNoFurtherActionFollows()
    {
        var events = EventsOf("K2_FirstMissing");
        int nextAction = events.FindIndex(e => e.Kind == TaskEventKind.ActionStarted);

        Feed(events.Take(nextAction), T0);
        Assert.Equal(RunStatus.CouldNotStart, Assert.Single(_tracker.Runs()).Status);
        Assert.Equal(T0, _tracker.OldestSettling);

        // The next action is launched before the run has been quiet for long enough.
        Feed(events.Skip(nextAction).Take(1), T0 + TimeSpan.FromMilliseconds(4));
        Assert.Null(_tracker.OldestSettling);
        Assert.Empty(_tracker.TakeFinished(quietSince: T0 + Quiet));

        Feed(events.Skip(nextAction + 1), T0 + TimeSpan.FromMilliseconds(150));
        var run = Assert.Single(_tracker.TakeFinished(quietSince: T0 + Quiet));
        Assert.Equal(RunStatus.Failed, run.Status);
    }

    [Fact]
    public void AnEventThatArrivesAfterItsRunWasHandedOut_DoesNotBringTheRunBack()
    {
        var events = EventsOf("D_Stopped");
        var straggler = events.Single(e => e.Kind == TaskEventKind.Terminated);
        Feed(events.Where(e => e != straggler), T0);
        Assert.Single(_tracker.TakeFinished(quietSince: T0));

        Assert.Null(_tracker.Add(straggler, T0 + Quiet));

        Assert.Empty(_tracker.Runs());
        Assert.Empty(_tracker.TakeFinished(quietSince: T0 + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void AStartRequestThatCameToNothing_IsForgotten_AndNeverHandedOut()
    {
        // The request Task Scheduler ignored because the task was already running: queued (325) and triggered (110).
        var ignored = EventsOf("G_IgnoreNew").Where(e => e.InstanceId == Guid.Parse("62ab9657-17da-42fb-9746-53a5757dd1dc")).ToList();
        Assert.Equal(2, ignored.Count);
        Feed(ignored, T0);

        Assert.Equal(T0, _tracker.OldestSettling);
        Assert.Empty(_tracker.TakeFinished(quietSince: T0));
        Assert.Null(_tracker.OldestSettling);
    }

    [Fact]
    public void RunsOfDifferentTasks_ThatOverlap_AreKeptApart()
    {
        // Everything the log held, in log order: 21 runs, some of them in parallel.
        var started = Feed(Evidence.Log.Select(TaskEvent.From).OfType<TaskEvent>(), T0);

        var finished = _tracker.TakeFinished(quietSince: T0);

        Assert.Equal(21, finished.Count);
        Assert.Equal(19, started.Count); // the two tasks that could not start never did
        Assert.Equal(finished.Count, finished.Select(run => (run.TaskPath, run.InstanceId)).Distinct().Count());
        Assert.Empty(_tracker.Runs());
    }

    [Fact]
    public void WhenMoreRunsAreGoingThanTheTrackerFollows_TheOldestIsGivenUp_ButStillReportedWhenItEnds()
    {
        var tracker = new RunTracker(capacity: 2);
        var first = EventsOf("D_Stopped");
        var others = EventsOf("G_IgnoreNew").Where(e => e.Kind == TaskEventKind.RunStarted).ToList();
        Assert.Equal(2, others.Count);

        Assert.NotNull(tracker.Add(first.First(e => e.Kind == TaskEventKind.RunStarted), T0));
        foreach (var start in others) Assert.NotNull(tracker.Add(start, T0));
        Assert.Equal(2, tracker.Runs().Count());

        foreach (var e in first.Where(e => e.RecordId >= 62)) Assert.Null(tracker.Add(e, T0));

        var run = Assert.Single(tracker.TakeFinished(quietSince: T0));
        Assert.Equal(RunStatus.Stopped, run.Status);
        Assert.Null(run.Started);
    }
}
