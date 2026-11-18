using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

/// <summary>
/// What runs the events of the real log add up to. Every test folds the records Windows wrote
/// for one scenario of the evidence run; see <see cref="Evidence"/>.
/// </summary>
public sealed class TaskRunTests
{
    private static IEnumerable<TaskEvent> Events(IEnumerable<LogRecord> records) =>
        records.Select(TaskEvent.From).OfType<TaskEvent>();

    private static IReadOnlyList<TaskRun> RunsOf(string taskName) => TaskRun.FromEvents(Events(Evidence.Of(taskName)));

    private static DateTimeOffset TimeOf(long recordId) => Evidence.Log.Single(record => record.RecordId == recordId).Time;

    private static int[] Ids(TaskRun run) => run.Events.Select(e => e.EventId).ToArray();

    [Fact]
    public void ARunWhoseActionReturnsZero_Succeeded()
    {
        var run = Assert.Single(RunsOf("A_ExitZero"));

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.True(run.IsFinished);
        Assert.Equal(0u, run.ResultCode);
        Assert.Equal(Evidence.Folder + @"\A_ExitZero", run.TaskPath);
        Assert.Equal(Guid.Parse("4483e347-1005-4795-8711-6a94f872bcad"), run.InstanceId);
        Assert.Equal(@"runnervmm7ud1\runneradmin", run.User);
        Assert.Equal(TimeOf(14), run.Started);
        Assert.Equal(TimeOf(18), run.Ended);
        Assert.Equal(TimeOf(18) - TimeOf(14), run.Duration);
        Assert.Equal([100, 200, 110, 201, 102], Ids(run));
    }

    [Fact]
    public void ARunWhoseActionReturnsThree_FailedWithThatExitCode_ThoughTheLogCallsItSuccessfullyFinished()
    {
        var run = Assert.Single(RunsOf("B_ExitThree"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(3u, run.ResultCode);
        Assert.Equal(102, run.Events[^1].EventId);
    }

    [Fact]
    public void AnExitCodeAbove65535_IsOnlyKnownByItsLower16Bits()
    {
        var run = Assert.Single(RunsOf("B2_ExitLarge"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(70000u & 0xFFFF, run.ResultCode);
    }

    [Fact]
    public void ANegativeExitCode_IsKeptAsItIs()
    {
        var run = Assert.Single(RunsOf("B3_ExitNegative"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(uint.MaxValue, run.ResultCode);
    }

    [Fact]
    public void ATaskWhoseOnlyActionCannotBeLaunched_CouldNotStart_AndThatIsOneRunNotTwo()
    {
        // The log holds a failed launch that names the instance and a failed start that does not.
        var run = Assert.Single(RunsOf("C_MissingExe"));

        Assert.Equal(RunStatus.CouldNotStart, run.Status);
        Assert.Equal(0x80070002, run.ResultCode);
        Assert.Null(run.Started);
        Assert.Equal(TimeOf(50), run.Ended);
        Assert.Null(run.Duration);
        Assert.Equal(Guid.Parse("1685ddf0-e636-49d4-9dc4-74892d9dc1ab"), run.InstanceId);
        Assert.Equal([203, 101, 110], Ids(run));
    }

    [Fact]
    public void ARunThatIsStoppedOnRequest_IsStopped_AndTheCodeOfTheKilledProcessIsNotItsResult()
    {
        var run = Assert.Single(RunsOf("D_Stopped"));

        Assert.Equal(RunStatus.Stopped, run.Status);
        Assert.Null(run.ResultCode);
        Assert.Equal(TimeOf(55), run.Started);
        Assert.Equal(TimeOf(64), run.Ended);
        Assert.Equal([100, 200, 110, 330, 201, 102, 111], Ids(run));
    }

    [Fact]
    public void TwoActionsOfWhichTheFirstReturnsTwo_MakeOneFailedRun_WithTheFirstExitCode()
    {
        // Task Scheduler runs the second action all the same and reports its 0 as the task's last result.
        var run = Assert.Single(RunsOf("E_TwoActions"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(2u, run.ResultCode);
        Assert.Equal([100, 200, 110, 201, 200, 201, 102], Ids(run));
        Assert.Equal(TimeOf(76), run.Ended);
    }

    [Fact]
    public void ARunThatExceedsItsTimeLimit_TimedOut_ThoughTheReasonIsLoggedAfterTheCompletion()
    {
        var run = Assert.Single(RunsOf("F_TimeLimit"));

        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Null(run.ResultCode);
        Assert.Equal(TimeOf(37), run.Started);
        Assert.Equal(TimeOf(173), run.Ended);
        Assert.Equal([107, 100, 200, 201, 102, 329], Ids(run));
    }

    [Fact]
    public void AStartRequestThatTaskSchedulerIgnores_IsNotARun()
    {
        // Three requests: one ran, one was ignored because of it, one was forced to run next to it.
        var runs = RunsOf("G_IgnoreNew");

        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(RunStatus.Stopped, run.Status));
        Assert.DoesNotContain(runs, run => run.InstanceId == Guid.Parse("62ab9657-17da-42fb-9746-53a5757dd1dc"));
    }

    [Fact]
    public void TwoInstancesRunningAtTheSameTime_AreTwoRuns_NewestFirst()
    {
        var runs = RunsOf("H_Parallel");

        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(RunStatus.Succeeded, run.Status));
        Assert.Equal(Guid.Parse("47d318b6-f97b-49a5-b912-976555c19c0f"), runs[0].InstanceId);
        Assert.Equal(Guid.Parse("8f19f4be-103f-4256-abd0-b5bd6d4ea2eb"), runs[1].InstanceId);
        Assert.Equal(TimeOf(105), runs[0].Started);
        Assert.Equal(TimeOf(111), runs[0].Ended);
        Assert.Equal(TimeOf(101), runs[1].Started);
        Assert.Equal(TimeOf(109), runs[1].Ended);
    }

    [Fact]
    public void RegisteringChangingDisablingAndDeletingATask_IsNotARun()
    {
        Assert.NotEmpty(Evidence.Of("I_Lifecycle"));
        Assert.Empty(RunsOf("I_Lifecycle"));
    }

    [Fact]
    public void ATaskWhoseAccountCannotBeLoggedOn_CouldNotStart_WithTheLogonError()
    {
        var run = Assert.Single(RunsOf("J_LogonFails"));

        Assert.Equal(RunStatus.CouldNotStart, run.Status);
        Assert.Equal(0x8007052E, run.ResultCode);
        Assert.Null(run.InstanceId);
        Assert.Null(run.Started);
        Assert.Equal(TimeOf(121), run.Ended);
        Assert.Equal(@"runnervmm7ud1\ftsevbef53e", run.User);
        Assert.Equal([101], Ids(run));
    }

    [Fact]
    public void WhenALaterActionCannotBeLaunched_TheRunFailed_ThoughNoCompletionIsEverLogged()
    {
        var run = Assert.Single(RunsOf("K1_SecondMissing"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(0x80070002, run.ResultCode);
        Assert.Equal(TimeOf(126), run.Started);
        Assert.Equal(TimeOf(130), run.Ended);
        Assert.Equal(203, run.Events[^1].EventId);
    }

    [Fact]
    public void WhenTheFirstActionCannotBeLaunchedButTheNextOneRuns_ItIsOneFailedRun()
    {
        // No "started" event at all here: the failed launch and failed start take its place.
        var run = Assert.Single(RunsOf("K2_FirstMissing"));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(0x80070002, run.ResultCode);
        Assert.Equal(TimeOf(136), run.Started);
        Assert.Equal(TimeOf(139), run.Ended);
        Assert.Equal([203, 101, 200, 110, 201, 102], Ids(run));
    }

    [Fact]
    public void ARunThatHasNotEndedYet_IsRunning()
    {
        // The stopped task's log, cut off before the stop.
        var run = Assert.Single(TaskRun.FromEvents(Events(Evidence.Of("D_Stopped").Where(record => record.RecordId <= 57))));

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.IsFinished);
        Assert.Equal(TimeOf(55), run.Started);
        Assert.Null(run.Ended);
        Assert.Null(run.ResultCode);
        Assert.Null(run.Duration);
    }

    [Fact]
    public void ARunWhoseStartIsNotAmongTheEvents_IsStillARun_WithoutItsStart()
    {
        var run = Assert.Single(TaskRun.FromEvents(Events(Evidence.Of("A_ExitZero").Where(record => record.RecordId >= 17))));

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Null(run.Started);
        Assert.Equal(TimeOf(18), run.Ended);
    }

    [Fact]
    public void TheWholeLog_GivesOneRunPerRunThatHappened_WhateverOrderItsEventsComeIn()
    {
        var runs = TaskRun.FromEvents(Events(Evidence.Log.Reverse()));

        Assert.Equal(21, runs.Count);
        Assert.Equal(7, runs.Count(run => run.Status == RunStatus.Succeeded));
        Assert.Equal(6, runs.Count(run => run.Status == RunStatus.Failed));
        Assert.Equal(2, runs.Count(run => run.Status == RunStatus.CouldNotStart));
        Assert.Equal(5, runs.Count(run => run.Status == RunStatus.Stopped));
        Assert.Equal(1, runs.Count(run => run.Status == RunStatus.TimedOut));
        Assert.Equal(runs.OrderByDescending(run => run.Started ?? run.Ended), runs);
        Assert.Contains(runs, run => run.TaskPath == @"\Microsoft\Windows\Flighting\FeatureConfig\ReconcileFeatures" && run.User == @"NT AUTHORITY\SYSTEM");
    }

    [Fact]
    public void TheEventsRunsAreAskedForAcrossAllTasks_AreEnoughToGetEveryRunRight()
    {
        // The dashboard does not read triggers and the like; the runs must come out the same without them.
        var all = TaskRun.FromEvents(Events(Evidence.Log));
        var fromRunEvents = TaskRun.FromEvents(Events(Evidence.Log).Where(e => e.Kind is not (TaskEventKind.Other or TaskEventKind.Registered or TaskEventKind.Updated or TaskEventKind.Deleted)));

        Assert.Equal(
            all.Select(run => (run.TaskPath, run.InstanceId, run.Status, run.ResultCode, run.Started, run.Ended)),
            fromRunEvents.Select(run => (run.TaskPath, run.InstanceId, run.Status, run.ResultCode, run.Started, run.Ended)));
    }
}
