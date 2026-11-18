using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>The snooze as the gate in front of every run this app starts, and the record of what it refused.</summary>
public sealed class SuppressedRunTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";
    private const string Weekly = @"\Reports\Weekly";

    [Fact]
    public async Task ARunRequestedWhileAllTasksAreSnoozed_IsRefused_AndRecordedWithItsOrigin()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();
        var runner = new TaskRunner(Store, snooze, Notifier);
        await snooze.SnoozeAllAsync(After(30));
        AdvanceMinutes(10);

        Assert.Equal(RunOutcome.Suppressed, runner.Run(Backup));
        AdvanceMinutes(5);
        Assert.Equal(RunOutcome.Suppressed, runner.Run(Weekly, RunOrigin.CompletionAction));

        Assert.DoesNotContain(Store.Calls, call => call.StartsWith("Run", StringComparison.Ordinal));
        Assert.Contains("RunSuppressed Backup", Notifier.Shown);
        Assert.Equal(
            [
                new SuppressedRun(Start.AddMinutes(15), Weekly, RunOrigin.CompletionAction),
                new SuppressedRun(Start.AddMinutes(10), Backup, RunOrigin.Manual)
            ],
            snooze.State.SuppressedRuns);
    }

    [Fact]
    public async Task ARunOfATaskThatIsSnoozedOnItsOwn_IsRefused_WhileOtherTasksStart()
    {
        Store.Add(Backup).Add(Weekly);
        var snooze = await StartedService();
        var runner = new TaskRunner(Store, snooze, Notifier);
        await snooze.SnoozeTaskAsync(Backup, After(30));

        Assert.Equal(RunOutcome.Suppressed, runner.Run(Backup));
        Assert.Equal(RunOutcome.Started, runner.Run(Weekly));

        Assert.Equal(Backup, Assert.Single(snooze.State.SuppressedRuns).TaskPath);
        Assert.True(Store.IsRunning(Weekly));
    }

    [Fact]
    public async Task OnceTheSnoozeIsOver_RunsStartAgain_AndTheRefusedOnesStayOnRecord()
    {
        Store.Add(Backup);
        var snooze = await StartedService();
        var runner = new TaskRunner(Store, snooze, Notifier);
        await snooze.SnoozeAllAsync(After(30));
        runner.Run(Backup);

        AdvanceMinutes(30);

        Assert.Equal(RunOutcome.Started, runner.Run(Backup));
        Assert.Single(snooze.State.SuppressedRuns);
    }

    [Fact]
    public async Task RefusedRuns_AreWrittenAMomentLater_AndAreStillThereAfterARestart()
    {
        var first = await StartedService();
        await first.SnoozeAllAsync(After(30));
        string before = File.ReadAllText(StateFile);

        for (int i = 0; i < 50; i++) first.RecordSuppressedRun(Backup, RunOrigin.Manual);
        Assert.Equal(before, File.ReadAllText(StateFile));

        Time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotEqual(before, File.ReadAllText(StateFile));
        first.Dispose();

        var second = NewService();
        Assert.Equal(50, second.State.SuppressedRuns.Count);
        Assert.Equal(first.State.SuppressedRuns, second.State.SuppressedRuns);
    }

    [Fact]
    public async Task ARefusedRunThatIsNotOnDiskYet_IsWrittenWhenTheServiceIsDisposed()
    {
        var first = await StartedService();
        first.RecordSuppressedRun(Backup, RunOrigin.CompletionAction);

        first.Dispose();

        Assert.Equal([new SuppressedRun(Start, Backup, RunOrigin.CompletionAction)], NewService().State.SuppressedRuns);
    }

    [Fact]
    public async Task OnlyTheLatestRefusedRuns_AreKept_NewestFirst()
    {
        var snooze = await StartedService();

        for (int i = 0; i < SnoozeService.MaxSuppressedRuns + 20; i++)
        {
            snooze.RecordSuppressedRun($@"\Task{i}", RunOrigin.Manual);
            Time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(SnoozeService.MaxSuppressedRuns, snooze.State.SuppressedRuns.Count);
        Assert.Equal($@"\Task{SnoozeService.MaxSuppressedRuns + 19}", snooze.State.SuppressedRuns[0].TaskPath);
        Assert.Equal(@"\Task20", snooze.State.SuppressedRuns[^1].TaskPath);
    }

    [Fact]
    public async Task ARefusedRun_IsReportedThroughChanged()
    {
        var snooze = await StartedService();
        int changes = 0;
        snooze.Changed += (_, _) => changes++;

        snooze.RecordSuppressedRun(Backup, RunOrigin.Manual);

        Assert.Equal(1, changes);
    }
}
