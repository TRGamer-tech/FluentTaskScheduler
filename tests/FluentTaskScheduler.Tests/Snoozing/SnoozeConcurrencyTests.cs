using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>Commands that arrive while another one is still at work, and callers that must not be kept waiting.</summary>
public sealed class SnoozeConcurrencyTests : SnoozeTestBase
{
    private const string A = @"\A";
    private const string B = @"\B";
    private const string C = @"\C";

    /// <summary>A failing test should say so instead of hanging; nothing ever waits this long when the code is right.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Makes Task Scheduler stop answering at its <paramref name="number"/>-th change from now on.
    /// The returned handle tells when that point is reached and lets the change go ahead.
    /// </summary>
    private Stall StallAtChange(int number)
    {
        var stall = new Stall();
        int changes = 0;
        Store.Before = (_, _) =>
        {
            if (Interlocked.Increment(ref changes) != number) return;
            stall.WorkerThread = Environment.CurrentManagedThreadId;
            stall.Reached.Set();
            Assert.True(stall.Released.Wait(Patience));
        };
        return stall;
    }

    private sealed class Stall : IDisposable
    {
        public ManualResetEventSlim Reached { get; } = new();
        public ManualResetEventSlim Released { get; } = new();
        public int WorkerThread { get; set; }

        public void WaitUntilReached() => Assert.True(Reached.Wait(Patience), "Task Scheduler was never asked.");

        public void Dispose()
        {
            Released.Set();
            Reached.Dispose();
        }
    }

    [Fact]
    public async Task ACommand_ReturnsToItsCallerAtOnce_AndTalksToTaskSchedulerOnAnotherThread()
    {
        Store.Add(A);
        SuspendTriggers();
        var snooze = await StartedService();
        using var stall = StallAtChange(1);

        Task command = snooze.SnoozeAllAsync(After(60));

        stall.WaitUntilReached();
        Assert.False(command.IsCompleted);
        Assert.NotEqual(Environment.CurrentManagedThreadId, stall.WorkerThread);

        stall.Released.Set();
        await command;
        Assert.False(Store.IsEnabled(A));
    }

    [Fact]
    public async Task WhileTasksAreStillBeingDisabled_RunsAreAlreadyRefused_AndAskingCostsNoWaiting()
    {
        Store.Add(A);
        SuspendTriggers();
        var snooze = await StartedService();
        using var stall = StallAtChange(1);
        Task command = snooze.SnoozeAllAsync(After(60));
        stall.WaitUntilReached();

        // All of this happens on the test's thread while the command is stuck in Task Scheduler.
        Assert.True(snooze.BlocksRun(A));
        Assert.NotNull(snooze.State.Global);
        snooze.RecordSuppressedRun(A, RunOrigin.Manual);
        Assert.Single(snooze.State.SuppressedRuns);

        stall.Released.Set();
        await command;
    }

    [Fact]
    public async Task AResumeIssuedWhileTasksAreStillBeingDisabled_WaitsItsTurn_AndLeavesNothingDisabled()
    {
        Store.Add(A).Add(B).Add(C);
        SuspendTriggers();
        var snooze = await StartedService();
        using var stall = StallAtChange(2);
        Task snoozing = snooze.SnoozeAllAsync(After(60));
        stall.WaitUntilReached();

        Task resuming = snooze.ResumeAllAsync();
        Assert.False(resuming.IsCompleted);
        stall.Released.Set();
        await Task.WhenAll(snoozing, resuming);

        // First everything was disabled, then everything re-enabled: no step of one in the middle of the other.
        Assert.Equal(
            [$"SetEnabled {A} False", $"SetEnabled {B} False", $"SetEnabled {C} False", $"SetEnabled {A} True", $"SetEnabled {B} True", $"SetEnabled {C} True"],
            Store.Calls);
        Assert.Equal(new SnoozeState(), snooze.State);

        snooze.Dispose();
        Assert.Equal(new SnoozeState(), NewService().State);
    }

    [Fact]
    public async Task ASnoozeIssuedWhileAnEarlierOneIsStillBeingUndone_IsNotWipedByIt()
    {
        Store.Add(A).Add(B).Add(C);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(60));
        using var stall = StallAtChange(2);
        Task resuming = snooze.ResumeAllAsync();
        stall.WaitUntilReached();

        Task snoozingAgain = snooze.SnoozeAllAsync(After(120));
        stall.Released.Set();
        await Task.WhenAll(resuming, snoozingAgain);

        Assert.All(Store.Paths, path => Assert.False(Store.IsEnabled(path), path));
        Assert.Equal([A, B, C], snooze.State.Global?.SuspendedTasks);

        // The record of the new snooze is complete: its end re-enables everything, also after a restart.
        snooze.Dispose();
        var restarted = await StartedService();
        Assert.Equal([A, B, C], restarted.State.Global?.SuspendedTasks);
        AdvanceMinutes(120);
        Assert.All(Store.Paths, path => Assert.True(Store.IsEnabled(path), path));
    }

    [Fact]
    public async Task ASnoozeThatRunsOutWhileACommandIsAtWork_EndsRightAfterThatCommand()
    {
        Store.Add(A).Add(B);
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(30));
        Store.Add(C);
        using var stall = StallAtChange(1);
        Task snoozingC = snooze.SnoozeTaskAsync(C, After(120));
        stall.WaitUntilReached();

        AdvanceMinutes(30);
        Assert.NotNull(snooze.State.Global);

        stall.Released.Set();
        await snoozingC;
        await Eventually(snooze, state => state is { Global: null, IsBusy: false });

        Assert.True(Store.IsEnabled(A));
        Assert.True(Store.IsEnabled(B));
        Assert.False(Store.IsEnabled(C));
    }

    [Fact]
    public async Task ManyCommandsAtOnceFromManyThreads_LeaveTheRecordAndTheTasksInAgreement()
    {
        string[] paths = Enumerable.Range(1, 12).Select(number => $@"\Task{number}").ToArray();
        foreach (string path in paths) Store.Add(path);
        SuspendTriggers();
        var snooze = await StartedService();

        var random = new Random(20260310);
        var commands = Enumerable.Range(0, 200).Select<int, Func<Task>>(_ =>
        {
            string path = paths[random.Next(paths.Length)];
            return random.Next(5) switch
            {
                0 => () => snooze.SnoozeAllAsync(After(60)),
                1 => () => snooze.ResumeAllAsync(),
                2 => () => snooze.SnoozeTaskAsync(path, After(60)),
                3 => () => snooze.ResumeTaskAsync(path),
                _ => () => snooze.RetryRestoresAsync()
            };
        }).ToArray();

        await Task.WhenAll(commands.Select(command => Task.Run(command)));

        // Whatever order they ran in: a task is disabled exactly if the record says a snooze holds it.
        SnoozeState state = snooze.State;
        var held = state.Tasks.Select(task => task.TaskPath).Concat(state.Global?.SuspendedTasks ?? []).ToHashSet();
        Assert.All(paths, path => Assert.Equal(held.Contains(path), !Store.IsEnabled(path)));
        Assert.Empty(state.PendingRestores);

        await snooze.ResumeAllAsync();
        foreach (string path in paths) await snooze.ResumeTaskAsync(path);

        Assert.All(paths, path => Assert.True(Store.IsEnabled(path), path));
        Assert.Equal(new SnoozeState(), snooze.State);
        snooze.Dispose();
        Assert.Equal(new SnoozeState(), NewService().State);
    }

    [Fact]
    public async Task DisposingWhileACommandIsAtWork_DoesNotWaitForIt_AndTheCommandStillFinishes()
    {
        Store.Add(A);
        SuspendTriggers();
        var snooze = await StartedService();
        using var stall = StallAtChange(1);
        Task command = snooze.SnoozeAllAsync(After(60));
        stall.WaitUntilReached();

        snooze.Dispose();

        stall.Released.Set();
        await command;
        Assert.False(Store.IsEnabled(A));
        Assert.Equal([A], NewService().State.Global?.SuspendedTasks);
    }
}
