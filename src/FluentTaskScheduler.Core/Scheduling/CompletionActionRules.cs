using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Decides which tasks to start when a run is over, and remembers what it needs for that. It
/// starts nothing itself; <see cref="CompletionActionService"/> acts on what it decides.
/// <list type="number">
/// <item>A run that <see cref="RunStatus.Succeeded"/> starts the tasks listed for success. A run
/// that ended any other way (it failed, could not start, was stopped or timed out) starts the
/// tasks listed for failure. A start request that Task Scheduler turned down or queued is not a
/// run and starts nothing.</item>
/// <item>No cycles: a task is not started if it is already part of the chain, that is, if it is
/// the task that just finished or one of the tasks whose completion led to that run. A chain can
/// therefore contain each task only once and always ends.</item>
/// <item>No storms: completion actions start one task at most <see cref="MaxStartsPerTask"/>
/// times within <see cref="StartsCountedFor"/>. That leaves room for a task that is started
/// after another one every minute, and stops a loop that runs through something this app cannot
/// see, such as a task that starts another task itself.</item>
/// </list>
/// A chain is followed by the instance ID of each run a completion action started, so it holds
/// however long a run takes, and for as long as the app is running.
/// Not thread-safe: one caller at a time.
/// </summary>
internal sealed class CompletionActionRules
{
    public const int MaxStartsPerTask = 20;

    public static readonly TimeSpan StartsCountedFor = TimeSpan.FromMinutes(10);

    /// <summary>How long a start that was asked for may take to show up in the log. The log lags by about a second; after this long the start did not happen.</summary>
    public static readonly TimeSpan StartSeenWithin = TimeSpan.FromMinutes(1);

    private const int ChainsRemembered = 256;

    private readonly TimeProvider _time;
    private readonly List<Request> _awaited = [];
    private readonly List<(Guid Run, ValueList<string> Chain)> _chains = [];
    private readonly Dictionary<string, Queue<DateTimeOffset>> _starts = new(StringComparer.OrdinalIgnoreCase);

    public CompletionActionRules(TimeProvider time) => _time = time;

    public enum Verdict
    {
        Start,
        AlreadyInChain,
        StartedTooOften
    }

    public readonly record struct Decision(string Target, Verdict Verdict);

    /// <summary>A start a completion action asked Task Scheduler for.</summary>
    /// <param name="Source">The task whose run ended.</param>
    /// <param name="Chain">The tasks whose completion led to this start, oldest first, ending with <paramref name="Source"/>.</param>
    public sealed record Request(string Target, string Source, bool AfterSuccess, ValueList<string> Chain, DateTimeOffset At);

    /// <param name="Unstarted">Set when the run that ended is a start a completion action asked for, and the task could not be started.</param>
    public sealed record Plan(string Source, bool AfterSuccess, ValueList<string> Chain, IReadOnlyList<Decision> Decisions, Request? Unstarted);

    /// <summary>What to do now that <paramref name="finished"/> is over.</summary>
    /// <param name="actions">The completion actions of the task that ran; <see cref="CompletionActions.None"/> if nothing is to be started.</param>
    public Plan Decide(TaskRun finished, CompletionActions actions)
    {
        var unstarted = finished.Status == RunStatus.CouldNotStart ? TakeRequestFor(finished.TaskPath) : null;
        ValueList<string> chain = [.. unstarted?.Chain ?? TakeChainOf(finished.InstanceId) ?? [], finished.TaskPath];
        bool succeeded = finished.Status == RunStatus.Succeeded;

        var decisions = new List<Decision>();
        if (actions.IsActive)
        {
            var now = _time.GetUtcNow();
            foreach (string target in (succeeded ? actions.OnSuccess : actions.OnFailure).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var verdict = chain.Contains(target, StringComparer.OrdinalIgnoreCase) ? Verdict.AlreadyInChain
                    : RecentStartsOf(target, now) >= MaxStartsPerTask ? Verdict.StartedTooOften
                    : Verdict.Start;
                decisions.Add(new Decision(target, verdict));
            }
        }
        return new Plan(finished.TaskPath, succeeded, chain, decisions, unstarted);
    }

    /// <summary>Notes that Task Scheduler accepted the request to start <paramref name="target"/>. Whether the task really starts shows in the log.</summary>
    public void Requested(string target, Plan plan)
    {
        var now = _time.GetUtcNow();
        _awaited.Add(new Request(target, plan.Source, plan.AfterSuccess, plan.Chain, now));

        if (!_starts.TryGetValue(target, out var times)) _starts[target] = times = new Queue<DateTimeOffset>();
        times.Enqueue(now);
    }

    /// <summary>The request a run that just started answers; null if no completion action asked for it.</summary>
    public Request? Started(TaskRun run)
    {
        if (TakeRequestFor(run.TaskPath) is not { } request) return null;

        if (run.InstanceId is { } id)
        {
            _chains.Add((id, request.Chain));
            if (_chains.Count > ChainsRemembered) _chains.RemoveAt(0);
        }
        return request;
    }

    /// <summary>Removes and returns the requests that were not followed by a start in time.</summary>
    public IReadOnlyList<Request> TakeOverdue()
    {
        var deadline = _time.GetUtcNow() - StartSeenWithin;
        var overdue = _awaited.Where(request => request.At <= deadline).ToList();
        if (overdue.Count > 0) _awaited.RemoveAll(request => request.At <= deadline);
        return overdue;
    }

    private Request? TakeRequestFor(string target)
    {
        int index = _awaited.FindIndex(request => string.Equals(request.Target, target, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;

        var request = _awaited[index];
        _awaited.RemoveAt(index);
        return request;
    }

    private ValueList<string>? TakeChainOf(Guid? run)
    {
        int index = run is { } id ? _chains.FindIndex(entry => entry.Run == id) : -1;
        if (index < 0) return null;

        var chain = _chains[index].Chain;
        _chains.RemoveAt(index);
        return chain;
    }

    private int RecentStartsOf(string target, DateTimeOffset now)
    {
        if (!_starts.TryGetValue(target, out var times)) return 0;

        while (times.Count > 0 && now - times.Peek() >= StartsCountedFor) times.Dequeue();
        if (times.Count == 0) _starts.Remove(target);
        return times.Count;
    }
}
