namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Tasks to start when another task finishes. Windows has no such link between tasks, so this
/// app keeps it with the task (see <see cref="TaskMetadata"/>) and acts on it while it is running.
/// </summary>
public sealed record CompletionActions
{
    public static CompletionActions None { get; } = new();

    public bool Enabled { get; init; }

    /// <summary>Paths of the tasks to start after a successful run.</summary>
    public ValueList<string> OnSuccess { get; init; } = [];

    /// <summary>Paths of the tasks to start after a failed run.</summary>
    public ValueList<string> OnFailure { get; init; } = [];

    public bool HasTargets => OnSuccess.Count > 0 || OnFailure.Count > 0;

    /// <summary>True when finishing the task actually starts something.</summary>
    public bool IsActive => Enabled && HasTargets;
}
