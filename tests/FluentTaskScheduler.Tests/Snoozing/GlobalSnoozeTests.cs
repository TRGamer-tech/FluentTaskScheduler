using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>Snoozing all tasks at once.</summary>
public sealed class GlobalSnoozeTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";
    private const string Weekly = @"\Reports\Weekly";
    private const string Defender = @"\Microsoft\Windows\Defender\Scan";

    [Fact]
    public async Task SnoozingAllTasks_BlocksRunsFromTheApp_UntilTheTimeIsUp()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        Assert.False(snooze.BlocksRun(Backup));

        await snooze.SnoozeAllAsync(After(30));

        Assert.True(snooze.BlocksRun(Backup));
        Assert.Equal(new SnoozeWindow(Start.AddMinutes(30)), snooze.State.Global?.Window);

        AdvanceMinutes(29);
        Assert.True(snooze.BlocksRun(Backup));

        AdvanceMinutes(1);
        Assert.False(snooze.BlocksRun(Backup));
        Assert.Null(snooze.State.Global);
    }

    [Fact]
    public async Task SnoozingUntilAMoment_EndsThen_HoweverLongListingTheTasksTakes()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        Store.Listed = () => Time.Advance(TimeSpan.FromSeconds(20));

        await snooze.SnoozeAllAsync(SnoozeEnd.Until(Start.AddMinutes(30)));

        Assert.Equal(new SnoozeWindow(Start.AddMinutes(30)), snooze.State.Global?.Window);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ASnoozeForNoTimeAtAll_IsRefused(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SnoozeEnd.After(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public async Task WithoutTheSettingToSuspendTriggers_NoTaskIsTouched_AndTheTasksAreNotEvenListed()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(30));
        AdvanceMinutes(30);

        Assert.Empty(Store.Calls);
        Assert.Equal(0, Store.Listings);
    }

    [Fact]
    public async Task WithTriggersSuspended_EveryEnabledTaskIsDisabled_AndReEnabledWhenTheSnoozeEnds()
    {
        Store.Add(Backup).Add(Weekly).Add(@"\Retired", enabled: false);
        SuspendTriggers();
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(60));

        Assert.False(Store.IsEnabled(Backup));
        Assert.False(Store.IsEnabled(Weekly));
        Assert.True(snooze.State.Global?.SuspendsTriggers);
        Assert.Equal([Backup, Weekly], snooze.State.Global?.SuspendedTasks);

        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskThatWasDisabledBeforeTheSnooze_IsNotEnabledByItsEnd()
    {
        Store.Add(Backup).Add(@"\Retired", enabled: false);
        SuspendTriggers();
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(60));
        AdvanceMinutes(60);

        Assert.False(Store.IsEnabled(@"\Retired"));
        Assert.Equal([$"SetEnabled {Backup} False", $"SetEnabled {Backup} True"], Store.Calls);
    }

    [Fact]
    public async Task TasksOfWindows_AreLeftAlone()
    {
        Store.Add(Backup).Add(Defender);
        SuspendTriggers();
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(60));

        Assert.True(Store.IsEnabled(Defender));
        Assert.Equal([Backup], snooze.State.Global?.SuspendedTasks);
    }

    [Fact]
    public async Task TasksOfWindows_AreSuspendedToo_WhenTheSettingIncludesThem()
    {
        Store.Add(Backup).Add(Defender);
        SuspendTriggers(includeMicrosoftTasks: true);
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(60));
        Assert.False(Store.IsEnabled(Defender));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(Defender));
    }

    [Fact]
    public async Task ResumingEarly_ReEnablesTheTasksAtOnce()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        await snooze.ResumeAllAsync();

        Assert.True(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
        Assert.False(snooze.BlocksRun(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ResumingWhenNothingIsSnoozed_DoesNothing()
    {
        Store.Add(Backup);
        var snooze = await StartedService();

        await snooze.ResumeAllAsync();

        Assert.Empty(Store.Calls);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task ATaskTheUserReEnabledDuringTheSnooze_StaysEnabled_AndIsNotReportedAsAProblem()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        Store.SetEnabled(Weekly, true);
        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(Weekly));
        Assert.True(Store.IsEnabled(Backup));
        Assert.Empty(snooze.State.PendingRestores);
    }

    [Fact]
    public async Task ATaskDeletedDuringTheSnooze_IsForgotten()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        Store.Delete(Weekly);
        AdvanceMinutes(60);

        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task ATaskWindowsRefusesToDisable_IsSkipped_AndNeverTouchedAgain()
    {
        Store.Add(Backup).Add(@"\Protected");
        Store.Protected.Add(@"\Protected");
        SuspendTriggers();
        var attempts = new List<string>();
        Store.Before = (operation, path) => attempts.Add($"{operation} {path}");
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(60));

        Assert.True(Store.IsEnabled(@"\Protected"));
        Assert.False(Store.IsEnabled(Backup));
        Assert.Equal([Backup], snooze.State.Global?.SuspendedTasks);

        AdvanceMinutes(60);

        Assert.Equal(1, attempts.Count(attempt => attempt == @"SetEnabled \Protected"));
        Assert.Empty(snooze.State.PendingRestores);
    }

    [Fact]
    public async Task SnoozingAgain_MovesTheEnd_WithoutDisablingTheSameTasksTwice()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        AdvanceMinutes(30);
        Store.Add(@"\AddedMeanwhile");

        await snooze.SnoozeAllAsync(After(120));

        Assert.Equal(new SnoozeWindow(Start.AddMinutes(150)), snooze.State.Global?.Window);
        Assert.Equal([$"SetEnabled {Backup} False", $"SetEnabled {Weekly} False", @"SetEnabled \AddedMeanwhile False"], Store.Calls);

        // The first end passes without effect.
        AdvanceMinutes(60);
        Assert.False(Store.IsEnabled(Backup));
        Assert.True(snooze.BlocksRun(Backup));

        AdvanceMinutes(60);
        Assert.All(Store.Paths, path => Assert.True(Store.IsEnabled(path)));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task SnoozingAgain_DisablesATaskTheUserReEnabledInBetween()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        Store.SetEnabled(Backup, true);

        await snooze.SnoozeAllAsync(After(60));
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task SnoozingAgainWithoutSuspendingTriggers_ReEnablesWhatTheFirstSnoozeDisabled_ButStillBlocksRuns()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        Settings.Update(settings => settings with { SnoozeSuspendsScheduledTasks = false });
        await snooze.SnoozeAllAsync(After(60));

        Assert.True(Store.IsEnabled(Backup));
        Assert.True(snooze.BlocksRun(Backup));
        Assert.Equal(new GlobalSnooze(new SnoozeWindow(Start.AddMinutes(60)), SuspendsTriggers: false, SuspendedTasks: []), snooze.State.Global);
    }

    [Fact]
    public async Task NarrowingTheSnoozeToLeaveWindowsTasksAlone_ReEnablesThem()
    {
        Store.Add(Backup).Add(Defender);
        SuspendTriggers(includeMicrosoftTasks: true);
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));

        SuspendTriggers(includeMicrosoftTasks: false);
        await snooze.SnoozeAllAsync(After(60));

        Assert.True(Store.IsEnabled(Defender));
        Assert.False(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task TheStartOfASnooze_IsAnnouncedWithItsStatus_AndSoIsItsEnd()
    {
        var snooze = await StartedService();

        await snooze.SnoozeAllAsync(After(30));
        Assert.Equal(["SnoozeStarted Snooze.Status.EndsAt"], Notifier.Shown);

        AdvanceMinutes(30);
        Assert.Equal(["SnoozeStarted Snooze.Status.EndsAt", "SnoozeEnded"], Notifier.Shown);
    }

    [Fact]
    public async Task ASnoozeTheUserEnds_IsNotAnnouncedAsEnded()
    {
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(SnoozeEnd.AtRestart);

        await snooze.ResumeAllAsync();

        Assert.Equal(["SnoozeStarted Snooze.Status.UntilReboot"], Notifier.Shown);
    }

    [Fact]
    public async Task ASnoozeUntilRestart_OutlivesARestartOfTheApp_AndEndsWithTheWindowsSession()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var first = await StartedService();
        await first.SnoozeAllAsync(SnoozeEnd.AtRestart);
        Assert.Equal(SnoozeWindow.UntilRestart, first.State.Global?.Window);

        // No amount of time ends it.
        Time.Advance(TimeSpan.FromDays(3));
        Assert.True(first.BlocksRun(Backup));
        first.Dispose();

        var sameSession = await StartedService();
        Assert.True(sameSession.BlocksRun(Backup));
        Assert.False(Store.IsEnabled(Backup));
        sameSession.Dispose();

        Session.Id = "session-2";
        var nextSession = NewService();
        Assert.False(nextSession.BlocksRun(Backup));
        Assert.Null(nextSession.State.Global);

        await nextSession.StartAsync();
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task ASnoozeThatRanOutWhileTheAppWasClosed_IsOverAtTheNextStart_AndItsTasksAreReEnabledThen()
    {
        Store.Add(Backup).Add(Weekly);
        SuspendTriggers();
        var first = await StartedService();
        await first.SnoozeAllAsync(After(60));
        first.Dispose();

        AdvanceMinutes(600);
        Assert.False(Store.IsEnabled(Backup));

        var second = NewService();
        Assert.Null(second.State.Global);
        Assert.False(second.BlocksRun(Backup));

        await second.StartAsync();
        Assert.True(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
        Assert.Equal(new SnoozeState(), second.State);
    }

    [Fact]
    public async Task ASnoozeStillRunningAtTheNextStart_GoesOn_AndEndsOnTime()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var first = await StartedService();
        await first.SnoozeAllAsync(After(60));
        first.Dispose();
        AdvanceMinutes(20);

        var second = await StartedService();
        Assert.True(second.BlocksRun(Backup));
        Assert.Equal([$"SetEnabled {Backup} False"], Store.Calls);

        AdvanceMinutes(40);
        Assert.False(second.BlocksRun(Backup));
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task WhenTheComputerSleptThroughTheEndOfASnooze_TheSnoozeEndsWithinAMinuteOfWakingUp()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(180));

        // Eight hours pass on the clock while the timers stand still.
        Time.ShiftClock(TimeSpan.FromHours(8));
        Assert.True(snooze.BlocksRun(Backup));

        AdvanceMinutes(1);
        Assert.False(snooze.BlocksRun(Backup));
        Assert.True(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task SettingTheClockBack_DoesNotEndASnoozeEarly()
    {
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(30));

        // The clock is corrected from 09:00 to 08:00: the end at 09:30 is now ninety minutes away.
        Time.ShiftClock(TimeSpan.FromHours(-1));
        AdvanceMinutes(60);
        Assert.NotNull(snooze.State.Global);

        AdvanceMinutes(30);
        Assert.Null(snooze.State.Global);
    }

    [Fact]
    public async Task EveryChange_IsReportedThroughChanged_AndOnlyChangesAre()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        int changes = 0;
        snooze.Changed += (_, _) => changes++;

        await snooze.ResumeAllAsync();
        Assert.Equal(0, changes);

        await snooze.SnoozeAllAsync(After(30));
        Assert.Equal(1, changes);

        AdvanceMinutes(30);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task TheStateIsBusy_WhileTasksAreBeingDisabledOrReEnabled_AndAChangeTellsWhenThatIsDone()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        var seen = new List<(bool Busy, bool TaskEnabled)>();
        snooze.Changed += (_, _) => seen.Add((snooze.State.IsBusy, Store.IsEnabled(Backup)));

        await snooze.SnoozeAllAsync(After(30));
        Assert.Equal([(true, true), (false, false)], seen);

        seen.Clear();
        AdvanceMinutes(30);
        Assert.Equal([(true, false), (false, true)], seen);
    }

    [Fact]
    public async Task ASubscriberThatThrows_StopsNeitherTheSnoozeNorTheOtherSubscribers()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var snooze = await StartedService();
        bool heard = false;
        snooze.Changed += (_, _) => throw new InvalidOperationException("The view is gone.");
        snooze.Changed += (_, _) => heard = true;

        await snooze.SnoozeAllAsync(After(30));

        Assert.True(heard);
        Assert.False(Store.IsEnabled(Backup));
        Assert.NotNull(snooze.State.Global);
    }

    [Fact]
    public async Task WhenTheTasksCannotBeListed_TheSnoozeDoesNotStart()
    {
        Store.Add(Backup);
        SuspendTriggers();
        Store.ListingFails = new InvalidOperationException("Task Scheduler does not answer.");
        var snooze = await StartedService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => snooze.SnoozeAllAsync(After(30)));

        Assert.Null(snooze.State.Global);
        Assert.False(snooze.BlocksRun(Backup));
        Assert.Empty(Notifier.Shown);
        Assert.False(File.Exists(StateFile));
    }
}
