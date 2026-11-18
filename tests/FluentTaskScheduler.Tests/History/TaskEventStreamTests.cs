using FluentTaskScheduler.History;
using FluentTaskScheduler.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.History;

/// <summary>
/// The stream is driven here by the records of the real log (see <see cref="Evidence"/>) and by
/// a clock the test moves. After every step the test waits until the stream has dealt with what
/// it was given, so nothing depends on how fast the machine is.
/// </summary>
public sealed class TaskEventStreamTests : IDisposable
{
    private readonly FakeEventLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero));
    private readonly TaskEventStream _stream;
    private readonly List<string> _reported = [];

    public TaskEventStreamTests()
    {
        _stream = new TaskEventStream(_log, _time);
        _stream.RunStarted += (_, run) => Report($"started {NameOf(run.TaskPath)}");
        _stream.RunCompleted += (_, run) => Report($"completed {NameOf(run.TaskPath)} {run.Status}");
        _stream.TaskChanged += (_, change) => Report($"{change.Kind} {NameOf(change.TaskPath)}");
    }

    public void Dispose() => _stream.Dispose();

    private void Report(string what)
    {
        lock (_reported) _reported.Add(what);
    }

    private string[] Reported()
    {
        lock (_reported) return [.. _reported];
    }

    private static string NameOf(string path) => path[(path.LastIndexOf('\\') + 1)..];

    private async Task StartAsync()
    {
        _stream.Start();
        await _stream.WhenIdleAsync();
    }

    /// <summary>Writes the records of a scenario task to the log, as Task Scheduler did, and waits until the stream has seen them.</summary>
    private async Task LogAsync(string taskName, Func<LogRecord, bool>? which = null)
    {
        foreach (var record in Evidence.Of(taskName).Where(which ?? (_ => true))) _log.Write(record);
        await _stream.WhenIdleAsync();
    }

    private async Task AdvanceAsync(TimeSpan by)
    {
        _time.Advance(by);
        await _stream.WhenIdleAsync();
    }

    [Fact]
    public async Task ARun_IsReportedWhenItStarts_AndOnceMoreWhenItIsOverAndNothingFollowed()
    {
        await StartAsync();

        await LogAsync("A_ExitZero", record => record.Id != 141);
        Assert.Equal(["Registered A_ExitZero", "Updated A_ExitZero", "started A_ExitZero"], Reported());

        await AdvanceAsync(TaskEventStream.SettleTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, Reported().Length);

        await AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal("completed A_ExitZero Succeeded", Reported()[^1]);

        await AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Equal(4, Reported().Length);
    }

    [Fact]
    public async Task ARunThatHitItsTimeLimit_IsReportedAsTimedOut_ThoughTheLogSaysSoOnlyAfterTheCompletion()
    {
        await StartAsync();

        await LogAsync("F_TimeLimit", record => record.Id is not (106 or 140 or 141));
        await AdvanceAsync(TaskEventStream.SettleTime);

        Assert.Equal(["started F_TimeLimit", "completed F_TimeLimit TimedOut"], Reported());
    }

    [Fact]
    public async Task ATaskThatCouldNotStart_IsReportedAsOver_WithoutEverHavingStarted()
    {
        await StartAsync();

        await LogAsync("C_MissingExe", record => record.Id is 203 or 101 or 110);
        await LogAsync("J_LogonFails", record => record.Id is 101 or 110);
        await AdvanceAsync(TaskEventStream.SettleTime);

        Assert.Equal(["completed C_MissingExe CouldNotStart", "completed J_LogonFails CouldNotStart"], Reported());
    }

    [Fact]
    public async Task WhenALaunchFailsAndTheNextActionStillRuns_TheRunIsReportedOnce_WhenItIsReallyOver()
    {
        await StartAsync();

        // Failed launch and failed start arrive first; the next action starts a moment later.
        await LogAsync("K2_FirstMissing", record => record.Id is 203 or 101);
        await AdvanceAsync(TimeSpan.FromMilliseconds(10));
        await LogAsync("K2_FirstMissing", record => record.Id is 200 or 110);
        await AdvanceAsync(TaskEventStream.SettleTime * 3);
        Assert.Equal(["started K2_FirstMissing"], Reported());

        await LogAsync("K2_FirstMissing", record => record.Id is 201 or 102);
        await AdvanceAsync(TaskEventStream.SettleTime);

        Assert.Equal(["started K2_FirstMissing", "completed K2_FirstMissing Failed"], Reported());
    }

    [Fact]
    public async Task ARunThatWasAlreadyGoingWhenListeningBegan_IsReportedWhenItEnds()
    {
        await StartAsync();

        await LogAsync("D_Stopped", record => record.RecordId is >= 62 and <= 65);
        await AdvanceAsync(TaskEventStream.SettleTime);

        Assert.Equal(["completed D_Stopped Stopped"], Reported());
    }

    [Fact]
    public async Task EverythingTheLogHeld_IsReportedAsOneStartAndOneEndPerRun()
    {
        await StartAsync();

        foreach (var record in Evidence.Log) _log.Write(record);
        await _stream.WhenIdleAsync();
        await AdvanceAsync(TaskEventStream.SettleTime);

        var reported = Reported();
        Assert.Equal(19, reported.Count(line => line.StartsWith("started ")));
        Assert.Equal(21, reported.Count(line => line.StartsWith("completed ")));
        Assert.Equal(2, reported.Count(line => line.StartsWith("completed H_Parallel Succeeded")));
        Assert.Single(reported, "completed K1_SecondMissing Failed");
        Assert.Single(reported, "completed E_TwoActions Failed");
    }

    [Fact]
    public async Task ATaskBeingRegisteredChangedDisabledEnabledAndDeleted_IsReportedStepByStep()
    {
        await StartAsync();

        await LogAsync("I_Lifecycle");

        Assert.Equal(
            ["Registered I_Lifecycle", "Updated I_Lifecycle", "Updated I_Lifecycle", "Updated I_Lifecycle", "Updated I_Lifecycle", "Deleted I_Lifecycle"],
            Reported());
    }

    [Fact]
    public async Task TheStream_SubscribesOnce_ForExactlyTheEventsItUnderstands()
    {
        _stream.Start();
        _stream.Start();
        await _stream.WhenIdleAsync();
        await AdvanceAsync(TaskEventStream.CheckWhileOn * 3);

        Assert.Equal(1, _log.SubscribeCalls);
        Assert.Equal(TaskSchedulerLog.LiveEvents, _log.SubscribedQuery);
    }

    [Fact]
    public async Task BeforeStart_TheStreamDoesNotTouchTheLog()
    {
        await AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(0, _log.SubscribeCalls);
    }

    [Fact]
    public async Task WhenTheLogIsOffAtStart_TheStreamSubscribesOnceItIsSwitchedOn_AndReportsFromThenOn()
    {
        _log.State = LogState.Disabled;
        await StartAsync();
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        // Windows would accept a subscription now and never deliver through it.
        Assert.Equal(0, _log.SubscribeCalls);

        _log.State = LogState.Enabled;
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(1, _log.LiveSubscriptions);

        await LogAsync("B_ExitThree", record => record.Id is not (106 or 140 or 141));
        await AdvanceAsync(TaskEventStream.SettleTime);
        Assert.Equal(["started B_ExitThree", "completed B_ExitThree Failed"], Reported());
    }

    [Fact]
    public async Task WhenTheLogIsSwitchedOffAndOnAgain_TheStreamSubscribesAnew()
    {
        await StartAsync();
        Assert.Equal(1, _log.LiveSubscriptions);

        _log.State = LogState.Disabled;
        await AdvanceAsync(TaskEventStream.CheckWhileOn);
        _log.State = LogState.Enabled;
        await AdvanceAsync(TaskEventStream.CheckWhileOff);

        Assert.Equal(2, _log.SubscribeCalls);
        Assert.Equal(1, _log.LiveSubscriptions);
        await LogAsync("A_ExitZero", record => record.Id == 100);
        Assert.Equal(["started A_ExitZero"], Reported());
    }

    [Fact]
    public async Task ASubscriptionThatBreaks_IsReplaced()
    {
        await StartAsync();

        _log.BreakSubscriptions(new InvalidOperationException("the log was cleared"));
        await _stream.WhenIdleAsync();
        Assert.Equal(0, _log.LiveSubscriptions);

        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(1, _log.LiveSubscriptions);
        await LogAsync("A_ExitZero", record => record.Id == 100);
        Assert.Equal(["started A_ExitZero"], Reported());
    }

    [Fact]
    public async Task ALogThatCannotBeSubscribedTo_IsTriedAgainUntilItCan()
    {
        _log.Unreadable = true;
        await StartAsync();
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(2, _log.SubscribeCalls);
        Assert.Equal(0, _log.LiveSubscriptions);

        _log.Unreadable = false;
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(1, _log.LiveSubscriptions);
    }

    [Fact]
    public async Task WhenAskingForTheStateOfTheLogGoesWrong_TheStreamKeepsAsking()
    {
        _log.StateFailure = new InvalidOperationException("unexpected");
        await StartAsync();
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(0, _log.SubscribeCalls);

        _log.StateFailure = null;
        await AdvanceAsync(TaskEventStream.CheckWhileOff);
        Assert.Equal(1, _log.LiveSubscriptions);
    }

    [Fact]
    public async Task ASubscriberThatThrows_KeepsNeitherTheOthersNorLaterNotificationsFromArriving()
    {
        var careful = new List<string>();
        var stream = new TaskEventStream(_log, _time);
        stream.RunStarted += (_, _) => throw new InvalidOperationException("a careless subscriber");
        stream.RunStarted += (_, run) => careful.Add(NameOf(run.TaskPath));
        using (stream)
        {
            stream.Start();
            await stream.WhenIdleAsync();

            foreach (var record in Evidence.Of("A_ExitZero").Concat(Evidence.Of("B_ExitThree"))) _log.Write(record);
            await stream.WhenIdleAsync();
        }

        Assert.Equal(["A_ExitZero", "B_ExitThree"], careful);
    }

    [Fact]
    public async Task AfterDispose_NothingMoreIsReported_AndTheSubscriptionIsGivenUp()
    {
        await StartAsync();
        await LogAsync("A_ExitZero", record => record.Id == 100);

        _stream.Dispose();
        _stream.Dispose();

        foreach (var record in Evidence.Of("A_ExitZero").Concat(Evidence.Of("B_ExitThree"))) _log.Write(record);
        await AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(["started A_ExitZero"], Reported());
        Assert.True(SpinWait.SpinUntil(() => _log.LiveSubscriptions == 0, TimeSpan.FromSeconds(10)), "The subscription is still live.");
    }

    [Fact]
    public async Task Dispose_ReturnsAtOnce_EvenWhileASubscriberIsStillBusy()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _stream.RunStarted += (_, _) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        await StartAsync();
        foreach (var record in Evidence.Of("A_ExitZero")) _log.Write(record);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "The subscriber was never called.");

        // What a UI thread would do while the notification is still being delivered.
        await Task.Run(_stream.Dispose).WaitAsync(TimeSpan.FromSeconds(10));

        release.Set();
        Assert.True(SpinWait.SpinUntil(() => _log.LiveSubscriptions == 0, TimeSpan.FromSeconds(10)), "The subscription is still live.");
    }

    [Fact]
    public async Task ASubscriberMayDisposeTheStream_FromInsideItsHandler()
    {
        _stream.RunStarted += (_, _) => _stream.Dispose();
        await StartAsync();

        foreach (var record in Evidence.Of("A_ExitZero").Concat(Evidence.Of("B_ExitThree"))) _log.Write(record);

        Assert.True(SpinWait.SpinUntil(() => _log.LiveSubscriptions == 0, TimeSpan.FromSeconds(10)), "The subscription is still live.");
        Assert.DoesNotContain(Reported(), line => line.Contains("B_ExitThree"));
    }
}
