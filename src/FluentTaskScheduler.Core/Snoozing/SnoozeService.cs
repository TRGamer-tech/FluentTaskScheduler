using System.Runtime.ExceptionServices;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using Serilog;

namespace FluentTaskScheduler.Snoozing;

/// <inheritdoc cref="ISnoozeService"/>
/// <remarks>
/// All snooze state has one owner, this class, and one home, a single file. Every change is a
/// transition: a command, a snooze running out, or the recovery at startup. Transitions run one
/// at a time, and each works in the same order: it writes what is to be, makes Task Scheduler
/// agree with that, and writes what came of it. Whatever interrupts it, the file tells the next
/// start what is left to do. A task that this app disabled leaves the record only once it is
/// known to be enabled again, or gone.
/// </remarks>
public sealed class SnoozeService : ISnoozeService, ISnoozeGate, IDisposable
{
    internal const string StateFileName = "snooze.json";

    /// <summary>The refused runs are kept to be shown, not as an archive.</summary>
    internal const int MaxSuppressedRuns = 300;

    /// <summary>
    /// A timer stands still while the computer sleeps and knows nothing of a corrected clock, so a
    /// long wait is cut into steps of this length. After waking up, a snooze that has run out is
    /// then noticed within one step instead of hours late.
    /// </summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(1);

    /// <summary>Refused runs are written after a short pause, so that a batch of them costs one write.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly ITaskStore _store;
    private readonly ISettingsStore _settings;
    private readonly INotifier _notifier;
    private readonly IWindowsSession _session;
    private readonly TimeProvider _time;
    private readonly SnoozeStateFile _file;
    private readonly ITimer _expiryTimer;
    private readonly ITimer _saveTimer;

    /// <summary>Held by the transition that is running; the others wait for it.</summary>
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly object _publishing = new();

    /// <summary>Read and written only by the transition that holds <see cref="_turn"/>.</summary>
    private SnoozeLedger _ledger;
    private volatile SnoozeState _state;

    /// <summary>True while something in memory is not in the file yet.</summary>
    private volatile bool _unsaved;

    /// <summary>
    /// Reads the stored state, or takes over the state of version 1.9 if there is none yet. That
    /// must happen before the settings are saved for the first time, because saving them drops
    /// the old snooze fields from settings.json.
    /// </summary>
    public SnoozeService(ITaskStore store, ISettingsStore settings, INotifier notifier, IWindowsSession session, AppPaths paths, TimeProvider? time = null)
    {
        _store = store;
        _settings = settings;
        _notifier = notifier;
        _session = session;
        _time = time ?? TimeProvider.System;
        _file = new SnoozeStateFile(paths.File(StateFileName));

        DateTimeOffset now = _time.GetUtcNow();
        SnoozeFileContent? stored = _file.ReadForOwner(now, out string? unreadableFile);
        if (stored is null && unreadableFile is null) stored = TakeOverFromVersion19(paths, out unreadableFile);

        // A snooze that ran out while the app was closed is over from the first moment on. Its
        // tasks are re-enabled by StartAsync: a constructor does not talk to Task Scheduler.
        _ledger = (stored?.Ledger ?? new SnoozeLedger()).EndElapsed(now, _session.Id);
        _state = _ledger.ToState(stored?.SuppressedRuns ?? [], unreadableFile);

        // Every transition begins by ending what has run out and finishes by writing what is
        // unsaved, so the timers only have to start an empty one.
        _expiryTimer = _time.CreateTimer(_ => StartFromTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveTimer = _time.CreateTimer(_ => StartFromTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public SnoozeState State => _state;

    public event EventHandler? Changed;

    /// <summary>
    /// Finishes what an earlier run of the app left undone: re-enables the tasks of snoozes that
    /// have run out meanwhile, completes an interrupted snooze, and tries again what could not be
    /// re-enabled last time. Call it once at startup.
    /// </summary>
    public Task StartAsync() => RunAsync(RestoreWhatIsDue);

    public Task RetryRestoresAsync() => RunAsync(RestoreWhatIsDue);

    public Task SnoozeAllAsync(SnoozeEnd end) => RunAsync(() => SnoozeAll(end));

    public Task ResumeAllAsync() => RunAsync(() =>
    {
        if (_ledger.Global is null) return;

        Apply(_ledger.WithoutGlobal(), mustBeRecorded: false);
        Log.Information("The snooze of all tasks was ended by the user");
    });

    public async Task<SnoozeTaskOutcome> SnoozeTaskAsync(string taskPath, SnoozeEnd end)
    {
        var outcome = SnoozeTaskOutcome.Snoozed;
        await RunAsync(() => outcome = SnoozeTask(taskPath, end)).ConfigureAwait(false);
        return outcome;
    }

    public Task ResumeTaskAsync(string taskPath) => RunAsync(() =>
    {
        if (_ledger.Find(taskPath)?.Window is null) return;

        Apply(_ledger.WithoutTaskWindow(taskPath), mustBeRecorded: false);
        Log.Information("The snooze of task {Path} was ended by the user", taskPath);
    });

    public Task TaskMovedAsync(string fromPath, string toPath) => RunAsync(() => FollowMove(_ledger.WithTaskMoved(fromPath, toPath)));

    public Task FolderMovedAsync(string fromPath, string toPath) => RunAsync(() => FollowMove(_ledger.WithFolderMoved(fromPath, toPath)));

    public bool BlocksRun(string taskPath) => _state.BlocksRun(taskPath);

    /// <summary>
    /// Costs the caller no file access and no waiting for a transition: the run is on show at
    /// once and written a moment later.
    /// </summary>
    public void RecordSuppressedRun(string taskPath, RunOrigin origin)
    {
        var run = new SuppressedRun(_time.GetUtcNow(), taskPath, origin);
        lock (_publishing)
        {
            _state = _state with { SuppressedRuns = [run, .. _state.SuppressedRuns.Take(MaxSuppressedRuns - 1)] };
            _unsaved = true;
        }
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

        Log.Information("A run of {Path} was refused because of a snooze ({Origin})", taskPath, origin);
        RaiseChanged();
    }

    /// <summary>
    /// Stops the timers and writes refused runs that are not on disk yet. A transition that is
    /// still at work is not waited for: it saves as it goes, and what it does not get done before
    /// the process ends is finished by the next <see cref="StartAsync"/>.
    /// </summary>
    public void Dispose()
    {
        _expiryTimer.Dispose();
        _saveTimer.Dispose();

        // Zero: take the turn if it is free, do not wait for it.
        if (!_turn.Wait(0)) return;
        try
        {
            if (_unsaved) TrySave(_ledger);
        }
        finally
        {
            _turn.Release();
        }
    }

    private void SnoozeAll(SnoozeEnd end)
    {
        AppSettings settings = _settings.Current;
        bool suspend = settings.SnoozeSuspendsScheduledTasks;
        bool Covers(string path) => settings.SnoozeIncludeMicrosoftTasks || !WindowsTasks.Contains(path);

        // Listing every task is the slow part. It is only done when tasks are going to be disabled.
        IEnumerable<string> toDisable = suspend
            ? _store.GetTasks().Where(task => task.IsEnabled && Covers(task.Path)).Select(task => task.Path)
            : [];

        SnoozeWindow window = WindowUntil(end);
        Apply(_ledger.WithGlobal(window, suspend, toDisable, Covers, _session.Id), mustBeRecorded: true);

        Log.Information("All tasks are snoozed until {Until}; {Count} are disabled for it",
            window.Until, _ledger.Tasks.Count(task => task.ByGlobal));
        _notifier.SnoozeStarted(SnoozeText.AllTasks(window, _time));
    }

    private SnoozeTaskOutcome SnoozeTask(string taskPath, SnoozeEnd end)
    {
        TaskRow task = _store.GetTask(taskPath) ?? throw new TaskNotFoundException(taskPath);

        // A disabled task is only snoozed if this app disabled it: only then is enabling it afterwards right.
        if (!task.IsEnabled && _ledger.Find(taskPath) is null) return SnoozeTaskOutcome.AlreadyDisabled;

        SnoozeWindow window = WindowUntil(end);
        var notDisabled = Apply(_ledger.WithTaskWindow(taskPath, window, task.IsEnabled, _session.Id), mustBeRecorded: true);
        if (notDisabled.TryGetValue(taskPath, out Exception? failure)) ExceptionDispatchInfo.Throw(failure);

        Log.Information("Task {Path} is snoozed until {Until}", taskPath, window.Until);
        return SnoozeTaskOutcome.Snoozed;
    }

    private void RestoreWhatIsDue() => Apply(_ledger.WithRestoresDueAgain(), mustBeRecorded: false);

    private void FollowMove(SnoozeLedger moved)
    {
        if (moved != _ledger) Apply(moved, mustBeRecorded: false);
    }

    private SnoozeWindow WindowUntil(SnoozeEnd end)
    {
        if (end.Duration is { } duration) return new SnoozeWindow(_time.GetUtcNow() + duration);
        if (end.TimeOfDay is not { } timeOfDay) return SnoozeWindow.UntilRestart;

        DateTime now = _time.GetLocalNow().DateTime;
        DateTime local = now.Date + timeOfDay.ToTimeSpan();
        if (local <= now) local = local.AddDays(1);
        return new SnoozeWindow(new DateTimeOffset(local, _time.LocalTimeZone.GetUtcOffset(local)).ToUniversalTime());
    }

    /// <summary>
    /// Runs a transition when it is its turn. One that a caller asked for is moved to a background
    /// thread; one started by a timer is on such a thread already.
    /// </summary>
    private async Task RunAsync(Action transition, bool offload = true)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            if (offload) await Task.Run(() => Run(transition)).ConfigureAwait(false);
            else Run(transition);
        }
        finally
        {
            _turn.Release();
        }
    }

    private void Run(Action transition)
    {
        try
        {
            EndElapsed();
            transition();
        }
        finally
        {
            if (_unsaved) TrySave(_ledger);
            ScheduleNextEnd();
        }
    }

    /// <summary>For a transition nobody awaits: if it fails, saying so in the log is all that is left to do.</summary>
    private void StartFromTimer()
    {
        _ = Logged();

        async Task Logged()
        {
            try
            {
                await RunAsync(static () => { }, offload: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ending the snoozes that have run out failed");
            }
        }
    }

    private void EndElapsed()
    {
        SnoozeLedger next = _ledger.EndElapsed(_time.GetUtcNow(), _session.Id);
        if (next == _ledger) return;

        bool globalEnded = _ledger.Global is not null && next.Global is null;
        Apply(next, mustBeRecorded: false);
        if (!globalEnded) return;

        Log.Information("The snooze of all tasks has run out");
        _notifier.SnoozeEnded();
    }

    private void ScheduleNextEnd()
    {
        if (_ledger.NextEnd() is not { } end)
        {
            _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan wait = end - _time.GetUtcNow();
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        _expiryTimer.Change(wait < LongestWait ? wait : LongestWait, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Makes <paramref name="next"/> the state of affairs: records it, brings Task Scheduler in
    /// line with it, and records what came of that.
    /// </summary>
    /// <param name="mustBeRecorded">
    /// True for a change that disables tasks: if it cannot be written to disk it does not happen,
    /// because a task disabled without a record might stay disabled for good. The end of a snooze
    /// goes ahead either way; tasks are not left disabled because a file cannot be written.
    /// </param>
    /// <returns>The tasks that were to be disabled but still are enabled, with the error that prevented it.</returns>
    private Dictionary<string, Exception> Apply(SnoozeLedger next, bool mustBeRecorded)
    {
        if (next != _ledger || _unsaved)
        {
            if (mustBeRecorded) Save(next);
            else TrySave(next);
        }
        _ledger = next;

        var notDisabled = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
        bool tasksToChange = next.Tasks.Any(task => task.IsToDisable || task.IsToRestore);
        Publish(busy: tasksToChange);
        if (!tasksToChange) return notDisabled;

        var tasks = new List<OwnedTask>(next.Tasks.Count);
        foreach (OwnedTask task in next.Tasks)
        {
            OwnedTask? outcome = task.IsToDisable ? Disable(task, notDisabled) : task.IsToRestore ? Restore(task) : task;
            if (outcome is not null) tasks.Add(outcome);
        }

        _ledger = next with { Tasks = tasks };
        TrySave(_ledger);
        Publish(busy: false);
        return notDisabled;
    }

    /// <returns>The task as it is on record now; null if it is not this app's to re-enable.</returns>
    private OwnedTask? Disable(OwnedTask task, Dictionary<string, Exception> notDisabled)
    {
        try
        {
            _store.SetEnabled(task.Path, false);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the task itself tells whose it is: one that is still enabled was
            // never disabled by this app. Protected tasks end up here with "access denied".
            if (!IsDisabledNow(task.Path))
            {
                notDisabled[task.Path] = ex;
                Log.Information("Task {Path} is not disabled for the snooze: {Reason}", task.Path, ex.Message);
                return null;
            }
            Log.Warning(ex, "Task {Path} could not be disabled but is disabled; it stays on record", task.Path);
        }
        return task with { Disabled = true };
    }

    /// <returns>Null when nothing is left to do for the task; otherwise the task with the reason it is still disabled.</returns>
    private OwnedTask? Restore(OwnedTask task)
    {
        try
        {
            _store.SetEnabled(task.Path, true);
            return null;
        }
        catch (Exception ex)
        {
            // Deleted meanwhile, or enabled by someone with the rights this app lacks.
            if (!IsDisabledNow(task.Path)) return null;

            Log.Warning(ex, "Task {Path} could not be re-enabled; it stays on record and is tried again", task.Path);
            var reason = ex is TaskAccessDeniedException ? RestoreFailure.AccessDenied : RestoreFailure.Error;
            return task with { FailedRestore = new PendingRestore(task.Path, reason, ex.Message) };
        }
    }

    /// <summary>
    /// Asked after a change to a task failed. A task that is enabled, or gone, needs nothing more
    /// from this app. One whose state cannot be read is taken to be disabled, so it is not forgotten.
    /// </summary>
    private bool IsDisabledNow(string path)
    {
        try
        {
            return _store.GetTask(path) is { IsEnabled: false };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The state of task {Path} could not be read", path);
            return true;
        }
    }

    private SnoozeFileContent? TakeOverFromVersion19(AppPaths paths, out string? unreadableFile)
    {
        LegacySnoozeState legacy = LegacySnoozeState.Read(paths);
        unreadableFile = legacy.UnreadableFile;
        try
        {
            if (legacy.Content is { } content)
            {
                _file.Write(content);
                Log.Information("Took over the snooze state of version 1.9: {Tasks} disabled tasks, {Runs} refused runs",
                    content.Ledger.Tasks.Count, content.SuppressedRuns.Count);
            }
            legacy.RemoveFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old files stay, and the first transition tries the write again.
            Log.Error(ex, "The snooze state of version 1.9 could not be saved in its new place");
            _unsaved = true;
        }
        return legacy.Content;
    }

    private void Save(SnoozeLedger ledger)
    {
        // Cleared before the content is read: a run refused during the write sets it again.
        _unsaved = false;
        try
        {
            _file.Write(new SnoozeFileContent(ledger, _state.SuppressedRuns));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unsaved = true;
            throw;
        }
    }

    private void TrySave(SnoozeLedger ledger)
    {
        try
        {
            Save(ledger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "The snooze state could not be saved; the next change writes it again");
        }
    }

    /// <param name="busy">Whether tasks are yet to be disabled or re-enabled for what the ledger says now.</param>
    private void Publish(bool busy)
    {
        SnoozeState before, after;
        lock (_publishing)
        {
            before = _state;
            _state = after = _ledger.ToState(before.SuppressedRuns, before.UnreadableStateFile) with { IsBusy = busy };
        }
        if (after != before) RaiseChanged();
    }

    /// <summary>A subscriber that throws must not stop a snooze half way, nor keep the others from hearing of it.</summary>
    private void RaiseChanged()
    {
        foreach (EventHandler subscriber in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "A subscriber to snooze changes failed");
            }
        }
    }
}
