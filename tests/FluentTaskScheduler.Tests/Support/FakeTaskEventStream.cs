using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>A stream whose notifications a test raises by hand, on the calling thread.</summary>
public sealed class FakeTaskEventStream : ITaskEventStream
{
    public event EventHandler<TaskRun>? RunStarted;
    public event EventHandler<TaskRun>? RunCompleted;
    public event EventHandler<TaskChange>? TaskChanged;

    /// <summary>How many handlers are subscribed to the three events together.</summary>
    public int Subscribers =>
        (RunStarted?.GetInvocationList().Length ?? 0) + (RunCompleted?.GetInvocationList().Length ?? 0) + (TaskChanged?.GetInvocationList().Length ?? 0);

    public void Start(TaskRun run) => RunStarted?.Invoke(this, run);
    public void Complete(TaskRun run) => RunCompleted?.Invoke(this, run);
    public void Change(TaskChange change) => TaskChanged?.Invoke(this, change);

    /// <summary>A run of the task that is going on.</summary>
    public static TaskRun Running(string taskPath, Guid? instanceId = null, DateTimeOffset? started = null) => new()
    {
        TaskPath = taskPath,
        Status = RunStatus.Running,
        InstanceId = instanceId ?? Guid.NewGuid(),
        Started = started ?? DateTimeOffset.UnixEpoch
    };

    /// <summary>A run of the task that is over.</summary>
    public static TaskRun Finished(string taskPath, RunStatus status, Guid? instanceId = null, uint? resultCode = null, DateTimeOffset? ended = null) => new()
    {
        TaskPath = taskPath,
        Status = status,
        InstanceId = instanceId ?? Guid.NewGuid(),
        Started = status == RunStatus.CouldNotStart ? null : (ended ?? DateTimeOffset.UnixEpoch).AddSeconds(-5),
        Ended = ended ?? DateTimeOffset.UnixEpoch,
        ResultCode = resultCode ?? (status == RunStatus.Succeeded ? 0u : (uint?)null)
    };
}
