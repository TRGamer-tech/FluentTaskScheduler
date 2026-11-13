using FluentTaskScheduler.History;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>A history that holds whatever events a test puts into it.</summary>
public sealed class FakeTaskHistory : ITaskHistory
{
    public LogState State { get; set; } = LogState.Enabled;
    public EnableLogResult EnableResult { get; set; } = EnableLogResult.Enabled;
    public List<TaskEvent> Events { get; } = [];
    public List<SystemEvent> SystemEvents { get; } = [];

    /// <summary>When true, reading task events and runs fails the way an unreadable log does.</summary>
    public bool Unreadable { get; set; }

    /// <summary>The period each call of <see cref="GetEvents"/> asked for.</summary>
    public List<(DateTimeOffset? From, DateTimeOffset? To)> Requests { get; } = [];

    public LogState GetLogState() => State;

    public EnableLogResult EnableLog()
    {
        if (EnableResult == EnableLogResult.Enabled) State = LogState.Enabled;
        return EnableResult;
    }

    public IReadOnlyList<TaskEvent> GetEvents(string taskPath, int maxCount, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellation = default)
    {
        Requests.Add((from, to));
        if (Unreadable) throw new HistoryUnavailableException("Microsoft-Windows-TaskScheduler/Operational", new UnauthorizedAccessException("Access is denied."));
        return Events.Where(e => (from is null || e.Time >= from) && (to is null || e.Time <= to))
                     .OrderByDescending(e => e.Time).ThenByDescending(e => e.RecordId).Take(maxCount).ToList();
    }

    public IReadOnlyList<TaskRun> GetRuns(DateTimeOffset since, CancellationToken cancellation = default) => Unreadable
        ? throw new HistoryUnavailableException("Microsoft-Windows-TaskScheduler/Operational", new UnauthorizedAccessException("Access is denied."))
        : TaskRun.FromEvents(Events.Where(e => e.Time >= since));

    public IReadOnlyList<SystemEvent> GetSystemEvents(DateTimeOffset around, TimeSpan margin, CancellationToken cancellation = default) =>
        SystemEvents.Where(e => (e.Time - around).Duration() <= margin).OrderBy(e => e.Time).ToList();
}

public sealed class FakeClipboard : IClipboard
{
    public string? Text { get; private set; }
    public void SetText(string text) => Text = text;
}
