using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>Snoozing one task, alone and together with a snooze of all tasks.</summary>
public sealed class TaskSnoozeTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";
    private const string Weekly = @"\Reports\Weekly";
    private const string Defender = @"\Microsoft\Windows\Defender\Scan";

    [Fact]
    public async Task SnoozingATask_DisablesIt_AndReEnablesItWhenTheTimeIsUp()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();

        var outcome = await snooze.SnoozeTaskAsync(Backup, After(30));

        Assert.Equal(SnoozeTaskOutcome.Snoozed, outcome);
        Assert.False(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
        Assert.Equal([new TaskSnooze(Backup, new SnoozeWindow(Start.AddMinutes(30)))], snooze.State.Tasks);

        AdvanceMinutes(30);

        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ASnoozedTask_CannotBeStartedFromTheApp_WhileOtherTasksCan()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();

        await snooze.SnoozeTaskAsync(Backup, After(30));

        Assert.True(snooze.BlocksRun(Backup));
        Assert.True(snooze.BlocksRun(@"\BACKUP"));
        Assert.False(snooze.BlocksRun(Weekly));
        Assert.NotNull(snooze.State.WindowOf(@"\backup"));
    }

    [Fact]
    public async Task SnoozingASnoozedTaskAgain_MovesItsEnd_AndItIsStillReEnabledThen()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(30));

        var outcome = await snooze.SnoozeTaskAsync(Backup, After(120));

        Assert.Equal(SnoozeTaskOutcome.Snoozed, outcome);
        Assert.Equal(new SnoozeWindow(Start.AddMinutes(120)), snooze.State.WindowOf(Backup));

        // The first end no longer counts.
        AdvanceMinutes(30);
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(90);
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal([$"SetEnabled {Backup} False", $"SetEnabled {Backup} True"], Store.Calls);
    }

    [Fact]
    public async Task ASnoozeCanBeShortened()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(120));

        await snooze.SnoozeTaskAsync(Backup, After(10));
        AdvanceMinutes(10);

        Assert.True(Store.IsEnabled(Backup));
        Assert.Empty(snooze.State.Tasks);
    }

    [Fact]
    public async Task ATaskThatIsAlreadyDisabled_IsNotSnoozed_AndIsNeverEnabled()
    {
        Store.Add(Backup, enabled: false);
        var snooze = await StartedService();

        var outcome = await snooze.SnoozeTaskAsync(Backup, After(30));

        Assert.Equal(SnoozeTaskOutcome.AlreadyDisabled, outcome);
        Assert.Empty(snooze.State.Tasks);
        Assert.False(snooze.BlocksRun(Backup));

        AdvanceMinutes(30);
        Assert.False(Store.IsEnabled(Backup));
        Assert.Empty(Store.Calls);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task ATaskThatDoesNotExist_CannotBeSnoozed()
    {
        var snooze = await StartedService();

        await Assert.ThrowsAsync<TaskNotFoundException>(() => snooze.SnoozeTaskAsync(@"\Gone", After(30)));

        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskWindowsRefusesToDisable_IsNotSnoozed_AndNothingAboutItStaysOnRecord()
    {
        Store.Add(Backup);
        Store.Protected.Add(Backup);
        var snooze = await StartedService();

        await Assert.ThrowsAsync<TaskAccessDeniedException>(() => snooze.SnoozeTaskAsync(Backup, After(30)));

        Assert.Empty(snooze.State.Tasks);
        Assert.False(snooze.BlocksRun(Backup));
        Assert.True(Store.IsEnabled(Backup));

        snooze.Dispose();
        var restarted = await StartedService();
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Fact]
    public async Task ResumingATask_ReEnablesItAtOnce()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(30));

        await snooze.ResumeTaskAsync(@"\backup");

        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ResumingATaskThatIsNotSnoozed_DoesNothing()
    {
        Store.Add(Backup, enabled: false);
        var snooze = await StartedService();

        await snooze.ResumeTaskAsync(Backup);

        Assert.False(Store.IsEnabled(Backup));
        Assert.Empty(Store.Calls);
    }

    [Fact]
    public async Task ATaskSnoozedUntilRestart_StaysDisabledThroughARestartOfTheApp_AndIsReEnabledInTheNextWindowsSession()
    {
        Store.Add(Backup);
        var first = await StartedService();
        await first.SnoozeTaskAsync(Backup, SnoozeEnd.AtRestart);
        Assert.Equal(SnoozeWindow.UntilRestart, first.State.WindowOf(Backup));

        Time.Advance(TimeSpan.FromDays(2));
        first.Dispose();
        var sameSession = await StartedService();
        Assert.False(Store.IsEnabled(Backup));
        Assert.True(sameSession.BlocksRun(Backup));
        sameSession.Dispose();

        Session.Id = "session-2";
        var nextSession = await StartedService();
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), nextSession.State);
    }

    [Fact]
    public async Task SnoozingOneTask_DoesNotListAllTasks()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();

        await snooze.SnoozeTaskAsync(Backup, After(30));
        await snooze.ResumeTaskAsync(Backup);

        Assert.Equal(0, Store.Listings);
    }

    [Fact]
    public async Task EachTask_EndsItsSnoozeAtItsOwnTime()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(30));
        await snooze.SnoozeTaskAsync(Weekly, After(60));

        AdvanceMinutes(30);
        Assert.True(Store.IsEnabled(Backup));
        Assert.False(Store.IsEnabled(Weekly));

        AdvanceMinutes(30);
        Assert.True(Store.IsEnabled(Weekly));
    }

    [Fact]
    public async Task ATaskSnoozedBothWays_StaysDisabledWhenTheSnoozeOfAllTasksEnds_UntilItsOwnSnoozeEnds()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(120));
        await snooze.SnoozeAllAsync(After(60));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(Weekly));
        Assert.False(Store.IsEnabled(Backup));
        Assert.True(snooze.BlocksRun(Backup));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal([$"SetEnabled {Backup} False", $"SetEnabled {Weekly} False", $"SetEnabled {Weekly} True", $"SetEnabled {Backup} True"], Store.Calls);
    }

    [Fact]
    public async Task ATaskSnoozedBothWays_StaysDisabledWhenItsOwnSnoozeEnds_UntilTheSnoozeOfAllTasksEnds()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(120));

        var outcome = await snooze.SnoozeTaskAsync(Backup, After(30));
        Assert.Equal(SnoozeTaskOutcome.Snoozed, outcome);

        AdvanceMinutes(30);
        Assert.False(Store.IsEnabled(Backup));
        Assert.Null(snooze.State.WindowOf(Backup));
        Assert.Equal([Backup, Weekly], snooze.State.Global?.SuspendedTasks);

        AdvanceMinutes(90);
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(1, Store.Calls.Count(call => call == $"SetEnabled {Backup} False"));
        Assert.Equal(1, Store.Calls.Count(call => call == $"SetEnabled {Backup} True"));
    }

    [Fact]
    public async Task ATaskSnoozedOnItsOwnBeforeAllTasksAreSnoozed_IsNotSwitchedOnInTheMiddleOfThatSnooze()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(30));
        await snooze.SnoozeAllAsync(After(120));

        AdvanceMinutes(30);
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(90);
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task ResumingATaskWhileAllTasksAreSnoozed_LeavesItDisabled_UntilThatSnoozeEndsToo()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(120));
        await snooze.SnoozeTaskAsync(Backup, After(30));

        await snooze.ResumeTaskAsync(Backup);
        Assert.False(Store.IsEnabled(Backup));

        await snooze.ResumeAllAsync();
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task ASnoozeOfAllTasksThatDoesNotSuspendTriggers_LeavesASnoozedTaskToItsOwnEnd()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(120));
        await snooze.SnoozeAllAsync(After(60));

        AdvanceMinutes(60);
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task SnoozingATaskTheUserReEnabledWhileAllTasksAreSnoozed_DisablesItAgain()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(120));
        Store.SetEnabled(Backup, true);

        await snooze.SnoozeTaskAsync(Backup, After(30));
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(120);
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task ATaskRenamedWhileItIsSnoozed_StaysSnoozed_AndIsReEnabledUnderItsNewName()
    {
        Store.Add(Backup);
        var first = await StartedService();
        await first.SnoozeTaskAsync(Backup, After(60));

        // Renaming registers the task, disabled as it is, under the new name and removes the old one.
        string renamed = Store.Save(Store.Load(Backup) with { Name = "Nightly" }, @"\", Backup);
        await first.TaskMovedAsync(Backup, renamed);

        Assert.Equal([new TaskSnooze(@"\Nightly", new SnoozeWindow(Start.AddMinutes(60)))], first.State.Tasks);
        Assert.True(first.BlocksRun(@"\Nightly"));
        Assert.False(first.BlocksRun(Backup));

        first.Dispose();
        var second = await StartedService();
        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(@"\Nightly"));
        Assert.Equal(new SnoozeState(), second.State);
    }

    [Fact]
    public async Task ATaskMovedWhileAllTasksAreSnoozed_IsReEnabledWhereItIsNow()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        string moved = Store.Save(Store.Load(Weekly), @"\Archive", Weekly);
        await snooze.TaskMovedAsync(Weekly, moved);
        Assert.Equal([Backup, @"\Archive\Weekly"], snooze.State.Global?.SuspendedTasks);

        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(@"\Archive\Weekly"));
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task ATaskThatCouldNotBeReEnabled_IsTriedAgainWhereItWasMovedTo()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(60));
        Store.Protected.Add(Backup);
        await snooze.ResumeTaskAsync(Backup);
        Assert.Single(snooze.State.PendingRestores);

        string renamed = Store.Save(Store.Load(Backup) with { Name = "Nightly" }, @"\");
        Store.Protected.Remove(Backup);
        Store.Delete(Backup);
        await snooze.TaskMovedAsync(Backup, renamed);

        Assert.True(Store.IsEnabled(@"\Nightly"));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task TheMoveOfATaskThatIsNotSnoozed_ChangesNothing()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(60));
        SnoozeState before = snooze.State;

        await snooze.TaskMovedAsync(Weekly, @"\Elsewhere\Weekly");

        Assert.Equal(before, snooze.State);
    }

    [Fact]
    public async Task TheTasksOfAFolderThatWasRenamedWhileTheyAreSnoozed_AreReEnabledWhereTheyAreNow()
    {
        Store.Add(@"\Jobs\Nightly").Add(@"\Jobs\Reports\Weekly").Add(@"\JobsOfOthers\Sync").Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        await snooze.SnoozeTaskAsync(@"\Jobs\Nightly", After(120));

        // Task Scheduler cannot rename a folder: its tasks are registered again under the new one, disabled as they are.
        Store.Add(@"\Work\Nightly", enabled: false).Add(@"\Work\Reports\Weekly", enabled: false);
        Store.Delete(@"\Jobs\Nightly");
        Store.Delete(@"\Jobs\Reports\Weekly");
        await snooze.FolderMovedAsync(@"\Jobs", @"\Work");

        Assert.Equal([@"\Work\Nightly", @"\Work\Reports\Weekly", @"\JobsOfOthers\Sync", Backup], snooze.State.Global?.SuspendedTasks);
        Assert.Equal(@"\Work\Nightly", Assert.Single(snooze.State.Tasks).TaskPath);

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(@"\Work\Reports\Weekly"));
        Assert.True(Store.IsEnabled(@"\JobsOfOthers\Sync"));
        Assert.False(Store.IsEnabled(@"\Work\Nightly"));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(@"\Work\Nightly"));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskOfWindowsSnoozedOnItsOwn_IsNotHeldByASnoozeThatLeavesWindowsTasksAlone()
    {
        Store.Add(Defender);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Defender, After(30));
        await snooze.SnoozeAllAsync(After(120));

        AdvanceMinutes(30);

        Assert.True(Store.IsEnabled(Defender));
    }
}
