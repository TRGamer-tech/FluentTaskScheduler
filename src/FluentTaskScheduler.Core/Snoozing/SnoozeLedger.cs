using System.Text.Json.Serialization;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Snoozing;

/// <summary>
/// A task this app has disabled, or is about to disable, and therefore has to re-enable. It
/// stays on the books until it is known to be enabled again, or gone.
/// </summary>
internal sealed record OwnedTask(string Path)
{
    /// <summary>The task's own snooze, if it has one.</summary>
    public SnoozeWindow? Window { get; init; }

    /// <summary>The snooze of all tasks covers this task.</summary>
    public bool ByGlobal { get; init; }

    /// <summary>
    /// False while the task still has to be disabled. A task is entered here first and disabled
    /// afterwards, so after a crash in between the entry may name a task that never was
    /// disabled; re-enabling that one later changes nothing.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>Set when the last attempt to re-enable the task failed.</summary>
    public PendingRestore? FailedRestore { get; init; }

    /// <summary>A snooze wants the task disabled, and that has not been done yet.</summary>
    [JsonIgnore]
    public bool IsToDisable => IsHeld && !Disabled;

    /// <summary>No snooze wants the task disabled any more, and re-enabling it has not failed since it was last due.</summary>
    [JsonIgnore]
    public bool IsToRestore => !IsHeld && FailedRestore is null;

    private bool IsHeld => Window is not null || ByGlobal;
}

/// <summary>
/// The books behind <see cref="SnoozeState"/>: which snoozes are in effect, and every task this
/// app has disabled and still has to re-enable. A command changes this record; making Task
/// Scheduler agree with it is the service's part. Nothing here has side effects.
/// </summary>
internal sealed record SnoozeLedger
{
    /// <summary>
    /// The Windows session in which the latest snooze was started. An "until restart" window
    /// lasts for as long as this is still the current session.
    /// </summary>
    public string Session { get; init; } = "";

    /// <summary>The window of the snooze of all tasks; null while there is none.</summary>
    public SnoozeWindow? Global { get; init; }

    public bool SuspendsTriggers { get; init; }

    public ValueList<OwnedTask> Tasks { get; init; } = [];

    public OwnedTask? Find(string path) => Tasks.FirstOrDefault(task => SamePath(task.Path, path));

    /// <summary>The earliest moment at which a window runs out by the clock; null if none does.</summary>
    public DateTimeOffset? NextEnd() => Tasks.Select(task => task.Window).Append(Global).Min(window => window?.Until);

    /// <summary>
    /// Ends every window that has run out: a timed one by the clock, an "until restart" one when
    /// <paramref name="session"/> is no longer the session it was started in. Afterwards no task
    /// is held by a snooze of all tasks that is not there, whatever the file said.
    /// </summary>
    public SnoozeLedger EndElapsed(DateTimeOffset now, string session)
    {
        bool sessionOver = Session != session;
        bool IsOver(SnoozeWindow window) => window.Until is { } until ? until <= now : sessionOver;

        SnoozeLedger current = Global is null || IsOver(Global) ? WithoutGlobal() : this;
        return current with
        {
            Tasks = current.Tasks
                .Select(task => task.Window is { } window && IsOver(window) ? task with { Window = null } : task)
                .ToValueList()
        };
    }

    /// <summary>Starts the snooze of all tasks, or replaces the one in effect.</summary>
    /// <param name="tasksToDisable">The tasks that are enabled now and that this snooze disables; none if it does not suspend triggers.</param>
    /// <param name="covers">
    /// Whether the snooze extends to a task that this app keeps disabled already. Such a task
    /// joins it, so that the end of its own snooze does not switch it on in the middle of this one.
    /// </param>
    /// <param name="session">The current Windows session. All windows that are left when a snooze starts belong to it.</param>
    public SnoozeLedger WithGlobal(SnoozeWindow window, bool suspendsTriggers, IEnumerable<string> tasksToDisable, Func<string, bool> covers, string session)
    {
        var toDisable = new HashSet<string>(tasksToDisable, StringComparer.OrdinalIgnoreCase);
        var tasks = new List<OwnedTask>(Tasks.Count + toDisable.Count);
        foreach (OwnedTask task in Tasks)
        {
            bool enabledNow = toDisable.Remove(task.Path);
            tasks.Add(suspendsTriggers && covers(task.Path)
                ? task with { ByGlobal = true, Disabled = task.Disabled && !enabledNow, FailedRestore = null }
                : task with { ByGlobal = false });
        }
        tasks.AddRange(toDisable.Select(path => new OwnedTask(path) { ByGlobal = true }));

        return this with { Session = session, Global = window, SuspendsTriggers = suspendsTriggers, Tasks = tasks };
    }

    public SnoozeLedger WithoutGlobal() => this with
    {
        Global = null,
        SuspendsTriggers = false,
        Tasks = Tasks.Select(task => task with { ByGlobal = false }).ToValueList()
    };

    /// <summary>Gives a task a snooze of its own, or changes the one it has.</summary>
    /// <param name="enabledNow">Whether the task is enabled at this moment and so has to be disabled.</param>
    /// <param name="session">As for <see cref="WithGlobal"/>.</param>
    public SnoozeLedger WithTaskWindow(string path, SnoozeWindow window, bool enabledNow, string session)
    {
        OwnedTask task = Find(path) ?? new OwnedTask(path);
        task = task with { Window = window, Disabled = task.Disabled && !enabledNow, FailedRestore = null };
        return With(task) with { Session = session };
    }

    public SnoozeLedger WithoutTaskWindow(string path) =>
        Find(path) is { } task ? With(task with { Window = null }) : this;

    /// <summary>
    /// Carries the entry of a task that was renamed or moved over to its new path. An earlier
    /// failure to re-enable it is forgotten with the old path, which makes it due again.
    /// </summary>
    public SnoozeLedger WithTaskMoved(string from, string to)
    {
        if (Find(from) is not { } moved) return this;
        return this with
        {
            Tasks = Tasks
                .Where(task => task == moved || !SamePath(task.Path, to))
                .Select(task => task == moved ? moved with { Path = to, FailedRestore = null } : task)
                .ToValueList()
        };
    }

    /// <summary>As <see cref="WithTaskMoved"/>, for every task below a folder that was renamed or moved.</summary>
    public SnoozeLedger WithFolderMoved(string from, string to)
    {
        string below = from.TrimEnd('\\') + "\\";
        return this with
        {
            Tasks = Tasks
                .Select(task => task.Path.StartsWith(below, StringComparison.OrdinalIgnoreCase)
                    ? task with { Path = TaskPaths.Combine(to, task.Path[below.Length..]), FailedRestore = null }
                    : task)
                .ToValueList()
        };
    }

    /// <summary>Makes the tasks that could not be re-enabled due for another attempt.</summary>
    public SnoozeLedger WithRestoresDueAgain() => this with
    {
        Tasks = Tasks.Select(task => task with { FailedRestore = null }).ToValueList()
    };

    public SnoozeState ToState(ValueList<SuppressedRun> suppressedRuns, string? unreadableStateFile) => new()
    {
        Global = Global is null
            ? null
            : new GlobalSnooze(Global, SuspendsTriggers, Tasks.Where(task => task.ByGlobal).Select(task => task.Path).ToValueList()),
        Tasks = Tasks.Where(task => task.Window is not null).Select(task => new TaskSnooze(task.Path, task.Window!)).ToValueList(),
        SuppressedRuns = suppressedRuns,
        PendingRestores = Tasks.Select(task => task.FailedRestore).OfType<PendingRestore>().ToValueList(),
        UnreadableStateFile = unreadableStateFile
    };

    private SnoozeLedger With(OwnedTask task)
    {
        var tasks = Tasks.ToList();
        int index = tasks.FindIndex(other => SamePath(other.Path, task.Path));
        if (index < 0) tasks.Add(task);
        else tasks[index] = task;
        return this with { Tasks = tasks };
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
