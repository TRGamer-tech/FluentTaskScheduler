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

    /// <summary>Every request, in order, e.g. "SnoozeAll for 00:30:00", "SnoozeTask \Backup until restart" or "TaskMoved \A -> \B\A".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, every request that may fail fails with this.</summary>
    public Exception? Fails { get; set; }

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

    public Task ResumeAllAsync() => RecordAsync("ResumeAll");

    public async Task<SnoozeTaskOutcome> SnoozeTaskAsync(string taskPath, SnoozeEnd end)
    {
        await RecordAsync($"SnoozeTask {taskPath} {Describe(end)}");
        return TaskOutcome;
    }

    public Task ResumeTaskAsync(string taskPath) => RecordAsync($"ResumeTask {taskPath}");

    public Task TaskMovedAsync(string fromPath, string toPath) => RecordAsync($"TaskMoved {fromPath} -> {toPath}");

    public Task FolderMovedAsync(string fromPath, string toPath) => RecordAsync($"FolderMoved {fromPath} -> {toPath}");

    public Task RetryRestoresAsync() => RecordAsync("RetryRestores");

    private Task RecordAsync(string call)
    {
        Calls.Add(call);
        return Fails is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    private static string Describe(SnoozeEnd end) =>
        end.Duration is { } duration ? $"for {duration}"
        : end.TimeOfDay is { } time ? $"until {time.ToString("HH:mm", CultureInfo.InvariantCulture)}"
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
