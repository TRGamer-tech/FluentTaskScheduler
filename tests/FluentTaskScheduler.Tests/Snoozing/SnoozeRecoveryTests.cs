using System.Globalization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>
/// What must never happen is a task that this app disabled and then forgot. These tests break
/// things at the worst moments: the app dies in the middle, Windows refuses, the state file is
/// damaged or cannot be written.
/// </summary>
public sealed class SnoozeRecoveryTests : SnoozeTestBase
{
    private const string A = @"\A";
    private const string B = @"\B";
    private const string C = @"\C";

    private static void AssertAllEnabled(FakeTaskStore store) => Assert.All(store.Paths, path => Assert.True(store.IsEnabled(path), path));
    private static void AssertAllDisabled(FakeTaskStore store) => Assert.All(store.Paths, path => Assert.False(store.IsEnabled(path), path));

    [Fact]
    public void AStateFileAsThisVersionWritesIt_IsReadBackInFull()
    {
        // Spelled out, because the names in this file are a format that later versions have to go on reading.
        File.WriteAllText(StateFile,
            """
            {
              "Ledger": {
                "Session": "session-1",
                "Global": { "Until": "2026-03-10T10:00:00+00:00" },
                "SuspendsTriggers": true,
                "Tasks": [
                  { "Path": "\\A", "Window": null, "ByGlobal": true, "Disabled": true, "FailedRestore": null },
                  { "Path": "\\B", "Window": { "Until": null }, "ByGlobal": false, "Disabled": true, "FailedRestore": null },
                  { "Path": "\\C", "Window": null, "ByGlobal": false, "Disabled": true, "FailedRestore": { "TaskPath": "\\C", "Reason": "AccessDenied", "Detail": "Access to the task '\\C' was denied." } }
                ]
              },
              "SuppressedRuns": [
                { "Time": "2026-03-10T08:59:00+00:00", "TaskPath": "\\A", "Origin": "CompletionAction" }
              ]
            }
            """);

        var snooze = NewService();

        var expected = new SnoozeState
        {
            Global = new GlobalSnooze(new SnoozeWindow(Start.AddMinutes(60)), SuspendsTriggers: true, SuspendedTasks: [A]),
            Tasks = [new TaskSnooze(B, SnoozeWindow.UntilRestart)],
            SuppressedRuns = [new SuppressedRun(Start.AddMinutes(-1), A, RunOrigin.CompletionAction)],
            PendingRestores = [new PendingRestore(C, RestoreFailure.AccessDenied, @"Access to the task '\C' was denied.")]
        };
        Assert.Equal(expected, snooze.State);
    }

    [Fact]
    public async Task ATaskSaidToBeHeldByASnoozeOfAllTasksThatIsNotThere_IsReEnabledAtStartup()
    {
        File.WriteAllText(StateFile,
            """{ "Ledger": { "Session": "session-1", "Global": null, "SuspendsTriggers": false, "Tasks": [ { "Path": "\\A", "ByGlobal": true, "Disabled": true } ] }, "SuppressedRuns": [] }""");
        Store.Add(A, enabled: false);

        var snooze = await StartedService();

        Assert.True(Store.IsEnabled(A));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Theory]
    [InlineData(1)] // recorded, but no task touched yet
    [InlineData(2)] // \A is disabled, \B and \C are not
    [InlineData(3)]
    public async Task DyingWhileTasksAreBeingDisabled_TheNextStartFinishesTheSnooze_AndItStillEndsOnTime(int dyingAtChange)
    {
        Store.Add(A).Add(B).Add(C);
        SuspendTriggers();
        var snooze = await StartedService();
        var left = DieAtChange(dyingAtChange);
        await snooze.SnoozeAllAsync(After(60));
        snooze.Dispose();

        var restarted = NewService(left.Folder, left.Store);
        Assert.True(restarted.BlocksRun(A));
        await restarted.StartAsync();

        AssertAllDisabled(left.Store);
        Assert.Equal([A, B, C], restarted.State.Global?.SuspendedTasks);

        AdvanceMinutes(60);
        AssertAllEnabled(left.Store);
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Fact]
    public async Task DyingWhileTasksAreBeingDisabled_AndComingBackAfterTheSnoozeIsOver_ReEnablesWhatWasDisabled()
    {
        Store.Add(A).Add(B).Add(C);
        SuspendTriggers();
        var snooze = await StartedService();
        var left = DieAtChange(3);
        await snooze.SnoozeAllAsync(After(60));
        snooze.Dispose();
        Assert.False(left.Store.IsEnabled(A));
        Assert.False(left.Store.IsEnabled(B));

        AdvanceMinutes(90);
        var restarted = NewService(left.Folder, left.Store);
        await restarted.StartAsync();

        AssertAllEnabled(left.Store);
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Theory]
    [InlineData(1)] // the end is recorded, but no task re-enabled yet
    [InlineData(2)]
    [InlineData(3)]
    public async Task DyingWhileTasksAreBeingReEnabled_TheNextStartReEnablesTheRest(int dyingAtChange)
    {
        Store.Add(A).Add(B).Add(C);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        var left = DieAtChange(dyingAtChange);
        await snooze.ResumeAllAsync();
        snooze.Dispose();
        Assert.False(left.Store.IsEnabled(C));

        var restarted = NewService(left.Folder, left.Store);
        Assert.False(restarted.BlocksRun(A));
        await restarted.StartAsync();

        AssertAllEnabled(left.Store);
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Fact]
    public async Task DyingWhenASnoozeRunsOut_BeforeItsTasksAreReEnabled_TheNextStartReEnablesThem()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        var left = DieAtChange(1);
        AdvanceMinutes(60);
        snooze.Dispose();
        AssertAllDisabled(left.Store);

        var restarted = NewService(left.Folder, left.Store);
        await restarted.StartAsync();

        AssertAllEnabled(left.Store);
    }

    [Fact]
    public async Task DyingBetweenRecordingTheSnoozeOfOneTaskAndDisablingIt_TheNextStartDisablesIt_AndReEnablesItOnTime()
    {
        Store.Add(A);
        var snooze = await StartedService();
        var left = DieAtChange(1);
        await snooze.SnoozeTaskAsync(A, After(60));
        snooze.Dispose();
        Assert.True(left.Store.IsEnabled(A));

        var restarted = NewService(left.Folder, left.Store);
        Assert.True(restarted.BlocksRun(A));
        await restarted.StartAsync();
        Assert.False(left.Store.IsEnabled(A));

        AdvanceMinutes(60);
        Assert.True(left.Store.IsEnabled(A));
    }

    [Fact]
    public async Task DyingAfterTheLastTaskWasReEnabled_ButBeforeThatWasRecorded_LeavesNothingToComplainAbout()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        var left = DieAtChange(1);
        await snooze.ResumeAllAsync();
        snooze.Dispose();

        // The files of the moment of death, with the tasks as they are after all were re-enabled.
        var restarted = NewService(left.Folder, Store);
        await restarted.StartAsync();

        AssertAllEnabled(Store);
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Fact]
    public async Task ATaskThatMayNotBeReEnabled_StaysOnRecordWithTheReason_WhileTheOthersAreReEnabled()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        // The snooze was started as administrator; it ends without those rights.
        Store.Protected.Add(B);
        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(A));
        Assert.False(Store.IsEnabled(B));
        var pending = Assert.Single(snooze.State.PendingRestores);
        Assert.Equal(B, pending.TaskPath);
        Assert.Equal(RestoreFailure.AccessDenied, pending.Reason);
        Assert.NotEmpty(pending.Detail);

        // The snooze itself is over: nothing is blocked any more.
        Assert.Null(snooze.State.Global);
        Assert.False(snooze.BlocksRun(B));
    }

    [Fact]
    public async Task ATaskThatCouldNotBeReEnabled_IsRememberedAcrossARestart_AndTriedAgainAtEveryStart()
    {
        Store.Add(A);
        SuspendTriggers();
        var first = await StartedService();
        await first.SnoozeAllAsync(After(60));
        Store.Protected.Add(A);
        await first.ResumeAllAsync();
        first.Dispose();

        var second = NewService();
        Assert.Equal(A, Assert.Single(second.State.PendingRestores).TaskPath);
        await second.StartAsync();
        Assert.Equal(A, Assert.Single(second.State.PendingRestores).TaskPath);
        second.Dispose();

        Store.Protected.Clear();
        var third = await StartedService();
        Assert.True(Store.IsEnabled(A));
        Assert.Equal(new SnoozeState(), third.State);
    }

    [Fact]
    public async Task RetryRestores_ReEnablesWhatCouldNotBeReEnabledBefore()
    {
        Store.Add(A);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));
        Store.Protected.Add(A);
        await snooze.ResumeTaskAsync(A);
        Assert.Single(snooze.State.PendingRestores);

        await snooze.RetryRestoresAsync();
        Assert.Single(snooze.State.PendingRestores);

        Store.Protected.Clear();
        await snooze.RetryRestoresAsync();

        Assert.True(Store.IsEnabled(A));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskThatCouldNotBeReEnabled_IsNotTriedAgainByUnrelatedCommands()
    {
        Store.Add(A).Add(B);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));
        Store.Protected.Add(A);
        await snooze.ResumeTaskAsync(A);
        int attemptsOnA = 0;
        Store.Before = (_, path) => attemptsOnA += path == A ? 1 : 0;

        await snooze.SnoozeTaskAsync(B, After(30));
        AdvanceMinutes(30);

        Assert.Equal(0, attemptsOnA);
        Assert.Single(snooze.State.PendingRestores);
    }

    [Fact]
    public async Task ATaskThatCouldNotBeReEnabled_CanBeSnoozedAgain_AndIsReEnabledAtThatEnd()
    {
        Store.Add(A);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));
        Store.Protected.Add(A);
        await snooze.ResumeTaskAsync(A);

        var outcome = await snooze.SnoozeTaskAsync(A, After(30));
        Assert.Equal(SnoozeTaskOutcome.Snoozed, outcome);
        Assert.Empty(snooze.State.PendingRestores);

        Store.Protected.Clear();
        AdvanceMinutes(30);
        Assert.True(Store.IsEnabled(A));
    }

    [Fact]
    public async Task AnUnexpectedErrorWhenReEnablingATask_IsRecordedWithItsMessage()
    {
        Store.Add(A);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));
        Store.Before = (_, _) => throw new InvalidOperationException("The RPC server is unavailable.");

        await snooze.ResumeTaskAsync(A);

        Assert.Equal(new PendingRestore(A, RestoreFailure.Error, "The RPC server is unavailable."), Assert.Single(snooze.State.PendingRestores));
    }

    [Fact]
    public async Task ATaskThatTurnsOutToBeEnabled_IsNotReportedAsStillDisabled()
    {
        Store.Add(A);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));

        // An administrator enabled it meanwhile; this app may not touch it any more.
        Store.Add(A, enabled: true);
        Store.Protected.Add(A);
        await snooze.ResumeTaskAsync(A);

        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskWhoseStateCannotEvenBeRead_IsTakenToBeStillDisabled()
    {
        Store.Add(A);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(A, After(60));
        Store.Protected.Add(A);
        Store.ReadingFails = new InvalidOperationException("Task Scheduler does not answer.");

        await snooze.ResumeTaskAsync(A);

        Assert.Equal(A, Assert.Single(snooze.State.PendingRestores).TaskPath);
    }

    [Fact]
    public async Task DyingWhileDisablingAsAdministrator_AndComingBackWithoutThoseRights_KeepsTheDisabledTaskOnRecord()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        var left = DieAtChange(2);
        await snooze.SnoozeAllAsync(After(60));
        snooze.Dispose();

        // \A was disabled before the app died. Now nothing may be changed.
        left.Store.Protected.Add(A);
        left.Store.Protected.Add(B);
        var restarted = NewService(left.Folder, left.Store);
        await restarted.StartAsync();
        Assert.Equal([A], restarted.State.Global?.SuspendedTasks);

        AdvanceMinutes(60);
        Assert.Equal(A, Assert.Single(restarted.State.PendingRestores).TaskPath);
        Assert.True(left.Store.IsEnabled(B));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"Ledger\": { \"Tasks\": [")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{ \"Ledger\": { \"Tasks\": null }, \"SuppressedRuns\": [] }")]
    [InlineData("{ \"Ledger\": { \"Tasks\": [ { \"Path\": null } ] }, \"SuppressedRuns\": [] }")]
    [InlineData("{ \"Ledger\": { \"Tasks\": [ { \"Path\": \"\\\\A\", \"Disabled\": \"yes\" } ] }, \"SuppressedRuns\": [] }")]
    [InlineData("{ \"Ledger\": { \"Tasks\": [] }, \"SuppressedRuns\": [ null ] }")]
    public void AStateFileThatCannotBeRead_IsSetAside_AndReported_InsteadOfBeingTakenForNoSnooze(string content)
    {
        File.WriteAllText(StateFile, content);

        var snooze = NewService();

        string? kept = snooze.State.UnreadableStateFile;
        Assert.NotNull(kept);
        Assert.NotEqual(StateFile, kept);
        Assert.Equal(content, File.ReadAllText(kept));
        Assert.False(File.Exists(StateFile));
        Assert.Equal(new SnoozeState { UnreadableStateFile = kept }, snooze.State);
    }

    [Fact]
    public async Task AfterAnUnreadableFileWasSetAside_SnoozingWorksAgain_AndTheKeptFileIsLeftAlone()
    {
        File.WriteAllText(StateFile, "damaged");
        Store.Add(A);
        var first = await StartedService();
        string kept = first.State.UnreadableStateFile!;

        await first.SnoozeTaskAsync(A, After(60));
        first.Dispose();

        var second = await StartedService();
        Assert.NotNull(second.State.WindowOf(A));
        Assert.Null(second.State.UnreadableStateFile);
        Assert.Equal("damaged", File.ReadAllText(kept));
    }

    [Fact]
    public void TheNameADamagedStateFileIsKeptUnder_IsTheSameWhateverTheUsersCalendar()
    {
        File.WriteAllText(StateFile, "damaged");
        CultureInfo before = CultureInfo.CurrentCulture;
        // Its calendar counts the years of the Buddhist era, in which 2026 is 2569.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
        try
        {
            Assert.Equal(Folder.File("snooze.unreadable-20260310-090000.json"), NewService().State.UnreadableStateFile);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public async Task AnUnreadableStateFileThatCannotBeSetAside_IsLeftUntouched_AndNothingNewIsSnoozedMeanwhile()
    {
        File.WriteAllText(StateFile, "damaged");
        // A folder sits where the file would be moved to.
        Directory.CreateDirectory(Folder.File("snooze.unreadable-20260310-090000.json"));
        Store.Add(A);
        SuspendTriggers();
        var snooze = await StartedService();
        Assert.Equal(StateFile, snooze.State.UnreadableStateFile);

        IOException refused = await Assert.ThrowsAsync<IOException>(() => snooze.SnoozeTaskAsync(A, After(60)));
        await Assert.ThrowsAsync<IOException>(() => snooze.SnoozeAllAsync(After(60)));

        // The user is told why, so it is one of the app's texts.
        Assert.Equal("Snooze.Error.RecordUnreadable", refused.Message);
        Assert.True(Store.IsEnabled(A));
        Assert.Empty(Store.Calls);
        Assert.Null(snooze.State.Global);
        Assert.Empty(snooze.State.Tasks);
        Assert.Equal("damaged", File.ReadAllText(StateFile));
    }

    [Fact]
    public async Task AStateFileThatCannotBeOpenedAtStartup_StaysInItsPlaceUntouched_AndCountsAgainAtTheNextStart()
    {
        Store.Add(A).Add(B);
        var first = await StartedService();
        await first.SnoozeTaskAsync(A, After(60));
        first.Dispose();
        string content = File.ReadAllText(StateFile);

        SnoozeService second;
        using (new FileStream(StateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Something else has the file to itself while the app starts.
            second = NewService();
        }
        await second.StartAsync();

        Assert.Equal(StateFile, second.State.UnreadableStateFile);
        Assert.Empty(second.State.Tasks);
        await Assert.ThrowsAsync<IOException>(() => second.SnoozeTaskAsync(B, After(60)));
        Assert.True(Store.IsEnabled(B));
        Assert.Equal(content, File.ReadAllText(StateFile));
        second.Dispose();

        var third = await StartedService();
        Assert.Null(third.State.UnreadableStateFile);
        Assert.NotNull(third.State.WindowOf(A));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(A));
    }

    [Fact]
    public async Task ASnoozeThatCannotBeWrittenToDisk_DoesNotHappen()
    {
        // Nothing can be written where a folder is.
        Directory.CreateDirectory(StateFile);
        Store.Add(A);
        SuspendTriggers();
        var snooze = await StartedService();

        var allTasks = await Record.ExceptionAsync(() => snooze.SnoozeAllAsync(After(60)));
        var oneTask = await Record.ExceptionAsync(() => snooze.SnoozeTaskAsync(A, After(60)));

        Assert.True(allTasks is IOException or UnauthorizedAccessException, $"{allTasks}");
        Assert.True(oneTask is IOException or UnauthorizedAccessException, $"{oneTask}");
        Assert.True(Store.IsEnabled(A));
        Assert.Empty(Store.Calls);
        Assert.False(snooze.BlocksRun(A));
        Assert.Equal(new SnoozeState(), snooze.State);
        Assert.Empty(Notifier.Shown);
    }

    [Fact]
    public async Task TheEndOfASnooze_GoesAhead_EvenWhenItCannotBeWrittenToDisk()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        await snooze.SnoozeTaskAsync(B, After(60));
        File.Delete(StateFile);
        Directory.CreateDirectory(StateFile);

        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(A));
        Assert.True(Store.IsEnabled(B));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task WhatIsSnoozed_IsOnDiskBeforeAnyTaskIsDisabled()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        string onDiskAtFirstChange = "";
        Store.Before = (_, _) =>
        {
            if (onDiskAtFirstChange.Length == 0) onDiskAtFirstChange = File.ReadAllText(StateFile);
        };

        await snooze.SnoozeAllAsync(After(60));

        Assert.Contains(@"""Path"": ""\\A""", onDiskAtFirstChange);
        Assert.Contains(@"""Path"": ""\\B""", onDiskAtFirstChange);
    }
}
