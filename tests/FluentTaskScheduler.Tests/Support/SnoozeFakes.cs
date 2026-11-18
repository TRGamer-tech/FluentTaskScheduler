using System.Globalization;
using FluentTaskScheduler.Snoozing;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>
/// A snooze service that only records what it is asked to do. Its state is whatever the test
/// gives it with <see cref="Publish"/>.
/// </summary>
public sealed class FakeSnoozeService : ISnoozeService
{
    public SnoozeState State { get; private set; } = new();

    public event EventHandler? Changed;

    /// <summary>
    /// Every request, in order, e.g. "SnoozeAll for 00:30:00", "SnoozeAll until 2031-05-06 18:00 +02:00",
    /// "SnoozeTask \Backup until restart" or "TaskMoved \A -> \B\A".
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, every request fails with this.</summary>
    public Exception? Fails { get; set; }

    /// <summary>When set, a request stays unanswered until the test completes this.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>What <see cref="SnoozeTaskAsync"/> answers.</summary>
    public SnoozeTaskOutcome TaskOutcome { get; set; } = SnoozeTaskOutcome.Snoozed;

    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Makes <paramref name="state"/> the current state and reports the change on the calling thread, as the service does on whichever thread made it.</summary>
    public void Publish(SnoozeState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task SnoozeAllAsync(SnoozeEnd end) => RecordAsync($"SnoozeAll {Describe(end)}");

    /// <summary>When set, resuming also takes the snooze out of <see cref="State"/>, as the service does when Windows lets it.</summary>
    public bool Resumes { get; set; }

    public async Task ResumeAllAsync()
    {
        await RecordAsync("ResumeAll");
        if (Resumes) Publish(State with { Global = null });
    }

    public async Task<SnoozeTaskOutcome> SnoozeTaskAsync(string taskPath, SnoozeEnd end)
    {
        await RecordAsync($"SnoozeTask {taskPath} {Describe(end)}");
        return TaskOutcome;
    }

    public async Task ResumeTaskAsync(string taskPath)
    {
        await RecordAsync($"ResumeTask {taskPath}");
        if (Resumes) Publish(State with { Tasks = [.. State.Tasks.Where(task => task.TaskPath != taskPath)] });
    }

    public Task TaskMovedAsync(string fromPath, string toPath) => RecordAsync($"TaskMoved {fromPath} -> {toPath}");

    public Task FolderMovedAsync(string fromPath, string toPath) => RecordAsync($"FolderMoved {fromPath} -> {toPath}");

    public Task RetryRestoresAsync() => RecordAsync("RetryRestores");

    private async Task RecordAsync(string call)
    {
        Calls.Add(call);
        if (Hold is not null) await Hold.Task;
        if (Fails is { } failure) throw failure;
    }

    private static string Describe(SnoozeEnd end) =>
        end.Duration is { } duration ? $"for {duration}"
        : end.Moment is { } moment ? $"until {moment.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}"
        : "until restart";
}

/// <summary>The Windows session as a test sets it; give it a new <see cref="Id"/> to sign out or restart.</summary>
public sealed class FakeWindowsSession : IWindowsSession
{
    public string Id { get; set; } = "session-1";
}

/// <summary>
/// Fake time whose clock can be moved while the timers keep their pace: what happens when
/// Windows corrects the time, or when the computer sleeps and the timers stand still.
/// </summary>
public sealed class AdjustableClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private TimeSpan _shift;

    public void ShiftClock(TimeSpan by) => _shift += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _shift;
}
