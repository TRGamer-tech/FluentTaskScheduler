using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>
/// The service between a faked stream and a faked Task Scheduler, with the real
/// <see cref="TaskRunner"/> in between, so what the user is told is checked along with what is started.
/// </summary>
public sealed class CompletionActionServiceTests : IDisposable
{
    private const string A = @"\Jobs\A", B = @"\Jobs\B", C = @"\Jobs\C";

    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero));
    private readonly FakeTaskEventStream _stream = new();
    private readonly FakeTaskStore _store = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly Snooze _snooze = new();
    private readonly SettingsStore _settings;
    private readonly List<CompletionActionService> _services = [];

    public CompletionActionServiceTests()
    {
        _settings = new SettingsStore(_folder.Paths, _time);
        _store.Add(B).Add(C);
        NewService();
    }

    public void Dispose()
    {
        _services.ForEach(service => service.Dispose());
        _settings.Dispose();
        _folder.Dispose();
    }

    private CompletionActionService NewService()
    {
        var service = new CompletionActionService(_stream, _store, new TaskRunner(_store, _snooze, _notifier), _settings, _notifier, _time);
        _services.Add(service);
        return service;
    }

    private void Register(string path, CompletionActions actions) => _store.Add(path, draft: new TaskDraft { CompletionActions = actions });

    private static CompletionActions OnSuccess(params string[] targets) => new() { Enabled = true, OnSuccess = targets };
    private static CompletionActions OnFailure(params string[] targets) => new() { Enabled = true, OnFailure = targets };

    private static TaskRun Finished(string path, RunStatus status = RunStatus.Succeeded, uint? code = null) =>
        FakeTaskEventStream.Finished(path, status, resultCode: code);

    [Fact]
    public void WhenARunSucceeds_TheTasksListedForSuccessAreStarted_TheWayATriggerWouldStartThem()
    {
        Register(A, new CompletionActions { Enabled = true, OnSuccess = [B, C], OnFailure = [@"\Jobs\Alert"] });

        _stream.Complete(Finished(A));

        Assert.Equal([$"Run {B} unforced", $"Run {C} unforced"], _store.Calls);
    }

    [Theory]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.CouldNotStart)]
    [InlineData(RunStatus.Stopped)]
    [InlineData(RunStatus.TimedOut)]
    public void WhenARunEndsAnyOtherWay_TheTasksListedForFailureAreStarted(RunStatus status)
    {
        Register(A, new CompletionActions { Enabled = true, OnSuccess = [B], OnFailure = [C] });

        _stream.Complete(Finished(A, status));

        Assert.Equal([$"Run {C} unforced"], _store.Calls);
    }

    [Fact]
    public void TheUserIsToldThatATaskWasStarted_WhenItsStartShowsUp_NotWhenItWasAskedFor()
    {
        Register(A, OnSuccess(B));

        _stream.Complete(Finished(A));
        Assert.Empty(_notifier.Shown);

        _stream.Start(FakeTaskEventStream.Running(B));
        Assert.Equal(["CompletionActionStarted A -> B (success)"], _notifier.Shown);

        // A later start of the same task has nothing to do with it.
        _stream.Start(FakeTaskEventStream.Running(B));
        Assert.Single(_notifier.Shown);
    }

    [Fact]
    public void AfterAFailure_TheUserIsToldThatTheTaskWasStartedBecauseOfIt()
    {
        Register(A, OnFailure(B));

        _stream.Complete(Finished(A, RunStatus.Failed, 3));
        _stream.Start(FakeTaskEventStream.Running(B));

        Assert.Equal(["CompletionActionStarted A -> B (failure)"], _notifier.Shown);
    }

    [Fact]
    public void ATargetThatNoLongerExists_IsReportedAsNotStarted_AndTheOtherTargetsStillStart()
    {
        Register(A, OnSuccess(@"\Jobs\Gone", B));

        _stream.Complete(Finished(A));
        _stream.Start(FakeTaskEventStream.Running(B));

        Assert.Equal([$"Run {B} unforced"], _store.Calls);
        Assert.Equal([@"TaskCouldNotStart Gone: The task '\Jobs\Gone' does not exist.", "CompletionActionStarted A -> B (success)"], _notifier.Shown);
    }

    [Fact]
    public void ATargetWindowsRefusesToStart_IsReportedAsNotStarted_AndTheOtherTargetsStillStart()
    {
        Register(A, OnSuccess(B, C));
        _store.Protected.Add(B);

        _stream.Complete(Finished(A));

        Assert.Equal([$"Run {C} unforced"], _store.Calls);
        Assert.StartsWith("TaskCouldNotStart B: ", Assert.Single(_notifier.Shown));
    }

    [Fact]
    public void ATargetThatIsDeletedJustAsItIsBeingStarted_IsReportedAsNotStarted_AndTheOtherTargetsStillStart()
    {
        Register(A, OnSuccess(B, C));

        // Task Scheduler's answer for a task that went away after it was found.
        _store.Before = (operation, path) =>
        {
            if (operation == "Run" && path == B) throw new FileNotFoundException("The system cannot find the file specified.");
        };

        _stream.Complete(Finished(A));

        Assert.Equal([$"Run {C} unforced"], _store.Calls);
        Assert.Equal("TaskCouldNotStart B: The system cannot find the file specified.", Assert.Single(_notifier.Shown));
    }

    [Fact]
    public void ATargetThatTaskSchedulerAcceptsButCannotStart_IsReportedAsNotStarted_WithWindowsReason()
    {
        Register(A, OnSuccess(B));
        _stream.Complete(Finished(A));

        // What the log shows for B: a failed start, here because its program is missing.
        _stream.Complete(Finished(B, RunStatus.CouldNotStart, 0x80070002));

        string shown = Assert.Single(_notifier.Shown);
        Assert.StartsWith("TaskCouldNotStart B: ", shown);
        Assert.EndsWith("(0x80070002)", shown);
    }

    [Fact]
    public void WhileTasksAreSnoozed_NothingIsStarted_AndNothingIsAnnouncedAsStarted()
    {
        Register(A, OnSuccess(B));
        _snooze.Blocking = true;

        _stream.Complete(Finished(A));
        _stream.Start(FakeTaskEventStream.Running(B)); // B's own schedule, say

        Assert.Empty(_store.Calls);
        Assert.Equal(["RunSuppressed B"], _notifier.Shown);
        Assert.Equal([$"{B} CompletionAction"], _snooze.Suppressed);
    }

    [Fact]
    public void WhileTheSettingIsOff_NothingIsStarted_AndSwitchingItOnTakesEffectWithTheNextRun()
    {
        Register(A, OnSuccess(B));
        _settings.Update(s => s with { EnableTaskPipelines = false });

        _stream.Complete(Finished(A));
        Assert.Empty(_store.Calls);

        _settings.Update(s => s with { EnableTaskPipelines = true });
        _stream.Complete(Finished(A));
        Assert.Equal([$"Run {B} unforced"], _store.Calls);

        _settings.Update(s => s with { EnableTaskPipelines = false });
        _stream.Complete(Finished(A));
        Assert.Single(_store.Calls);
    }

    [Fact]
    public void ATaskIsReadWhenItsRunEnds_SoAChangeToItsActionsAppliesToTheNextRun()
    {
        Register(A, OnSuccess(B));
        _stream.Complete(Finished(A));

        Register(A, OnSuccess(C));
        _stream.Complete(Finished(A));

        Assert.Equal([$"Run {B} unforced", $"Run {C} unforced"], _store.Calls);
    }

    [Fact]
    public void ATaskWithoutCompletionActions_OrWithActionsSwitchedOff_StartsNothing()
    {
        Register(A, OnSuccess(B) with { Enabled = false });

        _stream.Complete(Finished(A));
        _stream.Complete(Finished(C));

        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void TheRunOfATaskThatHasBeenDeletedMeanwhile_StartsNothing()
    {
        _stream.Complete(Finished(@"\Jobs\Deleted"));

        Assert.Empty(_store.Calls);
        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public void TwoTasksThatStartEachOther_DoNotGoRoundInCircles()
    {
        Register(A, OnSuccess(B));
        Register(B, OnSuccess(A));

        _stream.Complete(Finished(A));
        var runOfB = FakeTaskEventStream.Running(B);
        _stream.Start(runOfB);
        _stream.Complete(runOfB with { Status = RunStatus.Succeeded, Ended = runOfB.Started });

        Assert.Equal([$"Run {B} unforced"], _store.Calls);
    }

    [Fact]
    public void ATaskThatKeepsBeingStarted_IsLeftAloneAfterTwentyStartsInTenMinutes()
    {
        Register(A, OnSuccess(B));

        for (int i = 0; i < 30; i++) _stream.Complete(Finished(A));
        Assert.Equal(CompletionActionRules.MaxStartsPerTask, _store.Calls.Count);

        _time.Advance(CompletionActionRules.StartsCountedFor);
        _stream.Complete(Finished(A));
        Assert.Equal(CompletionActionRules.MaxStartsPerTask + 1, _store.Calls.Count);
    }

    [Fact]
    public void WhenTwoCopiesOfTheAppSeeTheSameRun_OnlyOneOfThemActsOnIt()
    {
        NewService();
        Register(A, OnSuccess(B));

        _stream.Complete(Finished(A));
        _stream.Complete(Finished(A));

        // Two runs, two copies of the app: two starts, not four.
        Assert.Equal([$"Run {B} unforced", $"Run {B} unforced"], _store.Calls);
    }

    [Fact]
    public void WhatTheUserStartsByHand_StillRunsAtOnce_WhateverTheTaskItselfSays()
    {
        new TaskRunner(_store, _snooze, _notifier).Run(B);

        Assert.Equal([$"Run {B}"], _store.Calls);
    }

    [Fact]
    public void AfterDispose_RunsAreNoLongerActedOn()
    {
        Register(A, OnSuccess(B));
        _services.ForEach(service => service.Dispose());

        _stream.Complete(Finished(A));

        Assert.Equal(0, _stream.RunSubscribers);
        Assert.Empty(_store.Calls);
    }

    private sealed class Snooze : ISnoozeGate
    {
        public bool Blocking { get; set; }
        public List<string> Suppressed { get; } = [];

        public bool BlocksRun(string taskPath) => Blocking;
        public void RecordSuppressedRun(string taskPath, RunOrigin origin) => Suppressed.Add($"{taskPath} {origin}");
    }
}
