using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Verdict = FluentTaskScheduler.Scheduling.CompletionActionRules.Verdict;

namespace FluentTaskScheduler.Tests.Scheduling;

public sealed class CompletionActionRulesTests
{
    private const string A = @"\A", B = @"\B", C = @"\C";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero));
    private readonly CompletionActionRules _rules;

    public CompletionActionRulesTests() => _rules = new CompletionActionRules(_time);

    private static CompletionActions OnSuccess(params string[] targets) => new() { Enabled = true, OnSuccess = targets };
    private static CompletionActions OnFailure(params string[] targets) => new() { Enabled = true, OnFailure = targets };

    private static TaskRun Finished(string path, RunStatus status = RunStatus.Succeeded, Guid? instance = null) =>
        FakeTaskEventStream.Finished(path, status, instance);

    private static (string, Verdict)[] Decisions(CompletionActionRules.Plan plan) =>
        plan.Decisions.Select(decision => (decision.Target, decision.Verdict)).ToArray();

    /// <summary>Lets a run of <paramref name="source"/> end and its target start, the way the service and the log would; returns the target's run.</summary>
    private TaskRun Chain(TaskRun source, CompletionActions actions, string target)
    {
        var plan = _rules.Decide(source, actions);
        Assert.Contains((target, Verdict.Start), Decisions(plan));
        _rules.Requested(target, plan);

        var started = FakeTaskEventStream.Running(target);
        Assert.NotNull(_rules.Started(started));
        return started with { Status = RunStatus.Succeeded, Ended = started.Started };
    }

    [Fact]
    public void ARunThatSucceeded_StartsTheTasksListedForSuccess()
    {
        var actions = new CompletionActions { Enabled = true, OnSuccess = [B, C], OnFailure = [@"\Alert"] };

        var plan = _rules.Decide(Finished(A), actions);

        Assert.Equal([(B, Verdict.Start), (C, Verdict.Start)], Decisions(plan));
        Assert.True(plan.AfterSuccess);
        Assert.Equal(A, plan.Source);
    }

    [Theory]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.CouldNotStart)]
    [InlineData(RunStatus.Stopped)]
    [InlineData(RunStatus.TimedOut)]
    public void ARunThatEndedAnyOtherWay_StartsTheTasksListedForFailure(RunStatus status)
    {
        var actions = new CompletionActions { Enabled = true, OnSuccess = [B], OnFailure = [@"\Alert"] };

        var plan = _rules.Decide(Finished(A, status), actions);

        Assert.Equal([(@"\Alert", Verdict.Start)], Decisions(plan));
        Assert.False(plan.AfterSuccess);
    }

    [Fact]
    public void ActionsThatAreSwitchedOffOrEmpty_StartNothing()
    {
        Assert.Empty(_rules.Decide(Finished(A), OnSuccess(B) with { Enabled = false }).Decisions);
        Assert.Empty(_rules.Decide(Finished(A), CompletionActions.None).Decisions);
        Assert.Empty(_rules.Decide(Finished(A, RunStatus.Failed), OnSuccess(B)).Decisions);
    }

    [Fact]
    public void ATaskListedTwice_IsStartedOnce()
    {
        var plan = _rules.Decide(Finished(A), OnSuccess(B, @"\b", C, B));

        Assert.Equal([(B, Verdict.Start), (C, Verdict.Start)], Decisions(plan));
    }

    [Fact]
    public void ATaskDoesNotStartItself()
    {
        var plan = _rules.Decide(Finished(A), OnSuccess(@"\a", B));

        Assert.Equal([(@"\a", Verdict.AlreadyInChain), (B, Verdict.Start)], Decisions(plan));
    }

    [Fact]
    public void ACycle_EndsWhereItWouldComeBackToATaskOfTheChain()
    {
        // A starts B, and B is set up to start A again.
        var runOfB = Chain(Finished(A), OnSuccess(B), B);

        var plan = _rules.Decide(runOfB, OnSuccess(A));

        Assert.Equal([(A, Verdict.AlreadyInChain)], Decisions(plan));
        Assert.Equal([A, B], plan.Chain);
    }

    [Fact]
    public void AChain_IsFollowedThroughEveryTaskItPassed()
    {
        var runOfB = Chain(Finished(A), OnSuccess(B), B);
        var runOfC = Chain(runOfB, OnSuccess(C), C);

        var plan = _rules.Decide(runOfC, OnSuccess(A, B, @"\D"));

        Assert.Equal([(A, Verdict.AlreadyInChain), (B, Verdict.AlreadyInChain), (@"\D", Verdict.Start)], Decisions(plan));
        Assert.Equal([A, B, C], plan.Chain);
    }

    [Fact]
    public void AChain_HoldsHoweverLongALinkRuns()
    {
        var plan = _rules.Decide(Finished(A), OnSuccess(B));
        _rules.Requested(B, plan);
        var runOfB = FakeTaskEventStream.Running(B);
        _rules.Started(runOfB);

        _time.Advance(TimeSpan.FromHours(30));

        Assert.Equal([(A, Verdict.AlreadyInChain)], Decisions(_rules.Decide(runOfB with { Status = RunStatus.Succeeded }, OnSuccess(A))));
    }

    [Fact]
    public void ARunThatNoCompletionActionStarted_BelongsToNoChain()
    {
        Chain(Finished(A), OnSuccess(B), B);

        // B again, this time started by its own trigger.
        var plan = _rules.Decide(Finished(B), OnSuccess(A));

        Assert.Equal([(A, Verdict.Start)], Decisions(plan));
        Assert.Equal([B], plan.Chain);
    }

    [Fact]
    public void AChain_IsForgottenOnceTheRunItLedToHasBeenDealtWith()
    {
        var runOfB = Chain(Finished(A), OnSuccess(B), B);

        Assert.Equal(Verdict.AlreadyInChain, Assert.Single(_rules.Decide(runOfB, OnSuccess(A)).Decisions).Verdict);
        Assert.Equal(Verdict.Start, Assert.Single(_rules.Decide(runOfB, OnSuccess(A)).Decisions).Verdict);
    }

    [Fact]
    public void ARequest_IsAnsweredByTheNextStartOfThatTask_AndOnlyByThat()
    {
        var plan = _rules.Decide(Finished(A, RunStatus.Failed), OnFailure(B));
        _rules.Requested(B, plan);

        Assert.Null(_rules.Started(FakeTaskEventStream.Running(C)));

        var request = _rules.Started(FakeTaskEventStream.Running(@"\b"));
        Assert.NotNull(request);
        Assert.Equal((B, A, false), (request.Target, request.Source, request.AfterSuccess));

        Assert.Null(_rules.Started(FakeTaskEventStream.Running(B)));
    }

    [Fact]
    public void ARequestThatIsNotFollowedByAStart_IsGivenUpAfterAMinute()
    {
        _rules.Requested(B, _rules.Decide(Finished(A), OnSuccess(B)));

        _time.Advance(CompletionActionRules.StartSeenWithin - TimeSpan.FromSeconds(1));
        Assert.Empty(_rules.TakeOverdue());

        _time.Advance(TimeSpan.FromSeconds(1));
        var overdue = Assert.Single(_rules.TakeOverdue());
        Assert.Equal((B, A), (overdue.Target, overdue.Source));

        Assert.Empty(_rules.TakeOverdue());
        Assert.Null(_rules.Started(FakeTaskEventStream.Running(B)));
    }

    [Fact]
    public void ATaskThatWasAskedForAndCouldNotStart_IsReported_AndStaysPartOfItsChain()
    {
        _rules.Requested(B, _rules.Decide(Finished(A), OnSuccess(B)));

        var plan = _rules.Decide(Finished(B, RunStatus.CouldNotStart), OnFailure(A, C));

        Assert.NotNull(plan.Unstarted);
        Assert.Equal((B, A, true), (plan.Unstarted.Target, plan.Unstarted.Source, plan.Unstarted.AfterSuccess));
        Assert.Equal([(A, Verdict.AlreadyInChain), (C, Verdict.Start)], Decisions(plan));

        // The request is settled: it does not come up again as one that was never answered.
        _time.Advance(CompletionActionRules.StartSeenWithin);
        Assert.Empty(_rules.TakeOverdue());
    }

    [Fact]
    public void ATaskThatCouldNotStartOnItsOwn_IsNotMistakenForARequestedOne()
    {
        var plan = _rules.Decide(Finished(B, RunStatus.CouldNotStart), OnFailure(A));

        Assert.Null(plan.Unstarted);
        Assert.Equal([(A, Verdict.Start)], Decisions(plan));
    }

    [Fact]
    public void OneTask_IsStartedAtMostTwentyTimesInTenMinutes()
    {
        for (int i = 0; i < CompletionActionRules.MaxStartsPerTask; i++)
        {
            var plan = _rules.Decide(Finished(A), OnSuccess(B));
            Assert.Equal([(B, Verdict.Start)], Decisions(plan));
            _rules.Requested(B, plan);
            _time.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Equal([(B, Verdict.StartedTooOften)], Decisions(_rules.Decide(Finished(A), OnSuccess(B))));

        // Ten minutes after the first of the twenty, one more is allowed.
        _time.Advance(CompletionActionRules.StartsCountedFor - TimeSpan.FromSeconds(10 * CompletionActionRules.MaxStartsPerTask));
        var next = _rules.Decide(Finished(A), OnSuccess(B));
        Assert.Equal([(B, Verdict.Start)], Decisions(next));
        _rules.Requested(B, next);
        Assert.Equal([(B, Verdict.StartedTooOften)], Decisions(_rules.Decide(Finished(A), OnSuccess(B))));
    }

    [Fact]
    public void TheLimit_IsCountedPerTask_AndOnlyForStartsThatWereRequested()
    {
        for (int i = 0; i < CompletionActionRules.MaxStartsPerTask; i++) _rules.Requested(B, _rules.Decide(Finished(A), OnSuccess(B)));

        // Deciding alone does not count: C was planned as often as B and never asked for.
        for (int i = 0; i < CompletionActionRules.MaxStartsPerTask; i++) _rules.Decide(Finished(A), OnSuccess(C));

        Assert.Equal([(B, Verdict.StartedTooOften), (C, Verdict.Start)], Decisions(_rules.Decide(Finished(A), OnSuccess(B, C))));
    }

    [Fact]
    public void ATaskThatIsStartedOnceAMinute_NeverReachesTheLimit()
    {
        for (int minute = 0; minute < 120; minute++)
        {
            var plan = _rules.Decide(Finished(A), OnSuccess(B));
            Assert.Equal([(B, Verdict.Start)], Decisions(plan));
            _rules.Requested(B, plan);
            _rules.Started(FakeTaskEventStream.Running(B));
            _time.Advance(TimeSpan.FromMinutes(1));
        }
    }
}
