using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The dashboard of one window: how the tasks are doing, over the last seven days.
/// </summary>
/// <remarks>
/// The history is read when the page first comes on screen. From then on the dashboard follows
/// what Task Scheduler reports: a run that starts or ends is added to what it has, and its task
/// is read again shortly after; when a task is registered, changed or deleted, all of them are.
/// Off screen it only takes note of the runs; all tasks are read again when it comes back.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the dashboard waits after a report before it reads the tasks again, so that reports that come together cost one read.</summary>
    internal static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(500);

    private readonly ITaskStore _store;
    private readonly ITaskHistory _history;
    private readonly ITaskEventStream _stream;
    private readonly ISnoozeService _snooze;
    private readonly IShellNavigator _navigator;
    private readonly IUiThread _ui;
    private readonly TimeProvider _time;
    private readonly ITimer _refreshTimer;
    private readonly CancellationTokenSource _closing = new();

    private IReadOnlyList<TaskRow> _tasks = [];

    // When a read of all tasks last saw each task idle, with no instance of it running.
    private readonly Dictionary<string, DateTimeOffset> _seenIdle = new(StringComparer.OrdinalIgnoreCase);

    // The runs of the history as it was last read, and what the stream reported since, by run. A
    // run that was going on when the history was read is in both; what was reported is the newer
    // word on it.
    private Dictionary<string, TaskRun> _recorded = [];
    private readonly Dictionary<string, TaskRun> _reported = [];

    // What the next refresh reads again: the tasks whose runs were reported, or all tasks once
    // one was registered, changed or deleted.
    private readonly HashSet<string> _tasksThatRan = new(StringComparer.OrdinalIgnoreCase);
    private bool _tasksChanged;

    private readonly ReadQueue _reads = new();
    private bool _isShown, _hasRead, _refreshPlanned, _choosing;

    public DashboardViewModel(ITaskStore store, ITaskHistory history, ITaskEventStream stream, ISnoozeService snooze,
        IShellNavigator navigator, IUiThread ui, TimeProvider time)
    {
        _store = store;
        _history = history;
        _stream = stream;
        _snooze = snooze;
        _navigator = navigator;
        _ui = ui;
        _time = time;
        _refreshTimer = time.CreateTimer(_ => ui.Post(() => PendingRefresh = RefreshAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Categories.Add(Loc.Get("Dashboard.AllCategories"));
        Tags.Add(Loc.Get("Dashboard.AllTags"));
        _stream.RunStarted += OnRunReported;
        _stream.RunCompleted += OnRunReported;
        _stream.TaskChanged += OnTaskChanged;
        _snooze.Changed += OnSnoozeChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFigures))]
    private DashboardFigures _figures = DashboardFigures.None;

    /// <summary>False until the first figures are worked out; zeros are not shown in their place.</summary>
    public bool HasFigures => !ReferenceEquals(Figures, DashboardFigures.None);

    /// <summary>True while everything is being read anew. What is shown stays until the new figures are there.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>Windows is not recording history at the moment, so the figures about runs stand still.</summary>
    [ObservableProperty] private bool _isLogOff;

    /// <summary>Why the tasks could not be read the last time they were.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem))]
    private string? _tasksProblem;

    /// <summary>Why the history could not be read, or not be switched on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem))]
    private string? _historyProblem;

    /// <summary>What went wrong, if anything. Without the tasks the history is of little use, so their problem comes first.</summary>
    public string? Problem => TasksProblem ?? HistoryProblem;

    public bool HasProblem => Problem is not null;

    /// <summary>The categories to narrow the dashboard to. The first entry stands for all of them.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The tags to narrow the dashboard to. The first entry stands for all of them.</summary>
    public ObservableCollection<string> Tags { get; } = [];

    [ObservableProperty] private int _categoryIndex;
    [ObservableProperty] private int _tagIndex;

    /// <summary>Which entries the log shows.</summary>
    [ObservableProperty] private LogFilter _filter;

    // The lists of the figures, kept up to date entry by entry: a list control then keeps its
    // scroll position, focus and tooltips while the dashboard follows the runs.
    public ObservableCollection<HourCell> Hours { get; } = [];
    public ObservableCollection<TaskLine> Running { get; } = [];
    public ObservableCollection<TaskLine> Upcoming { get; } = [];
    public ObservableCollection<LogEntry> Log { get; } = [];

    partial void OnCategoryIndexChanged(int value) => OnChoiceChanged(value, index => CategoryIndex = index);
    partial void OnTagIndexChanged(int value) => OnChoiceChanged(value, index => TagIndex = index);
    partial void OnFilterChanged(LogFilter value) => Present();

    /// <summary>Leads to a task of one of the dashboard's lists.</summary>
    public void ShowTask(string taskPath) => _navigator.ShowTask(taskPath);

    /// <summary>
    /// Tells the dashboard that its page came on screen. The first time it reads everything;
    /// after that all tasks, which change unreported while it is off screen or while Windows
    /// records no history.
    /// </summary>
    public Task ShowAsync()
    {
        _isShown = true;
        if (!_hasRead) return LoadAsync();
        _tasksChanged = true;
        return RefreshAsync();
    }

    /// <summary>Tells the dashboard that its page left the screen.</summary>
    public void Hide() => _isShown = false;

    /// <summary>Reads the tasks and the history of the last seven days anew, and shows what they add up to.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        _hasRead = true;
        try
        {
            DateTimeOffset since = _time.GetUtcNow() - DashboardFigures.Period;

            // The history about to be read holds the runs reported so far, and has the newer word
            // on them. A run reported from here on may be in it or not yet; it is kept.
            List<KeyValuePair<string, TaskRun>> reportedBefore = [.. _reported];
            Task tasks = _reads.Enqueue(ReadAllTasksAsync);
            try
            {
                IReadOnlyList<TaskRun> runs = await Task.Run(() => _history.GetRuns(since, _closing.Token), _closing.Token);
                _recorded = [];
                foreach (TaskRun run in runs) _recorded[DashboardFigures.KeyOf(run)] = run;
                foreach (var (key, run) in reportedBefore)
                    if (_reported.TryGetValue(key, out TaskRun? latest) && ReferenceEquals(latest, run)) _reported.Remove(key);
                HistoryProblem = null;
            }
            catch (HistoryUnavailableException ex)
            {
                _recorded = [];
                HistoryProblem = Loc.Format("Details.History.Unavailable", ex.InnerException?.Message ?? ex.Message);
            }
            await tasks;
        }
        catch (OperationCanceledException)
        {
            // The window closed while the history was being read.
            return;
        }
        finally
        {
            IsLoading = false;
        }
        Present();
    }

    /// <summary>Asks Windows to record history from now on.</summary>
    [RelayCommand]
    private async Task SwitchLogOnAsync()
    {
        EnableLogResult result = await Task.Run(_history.EnableLog);
        IsLogOff = result != EnableLogResult.Enabled;
        HistoryProblem = result switch
        {
            EnableLogResult.Enabled => null,
            EnableLogResult.AccessDenied => Loc.Get("Details.History.SwitchOnDenied"),
            _ => Loc.Get("Details.History.SwitchOnFailed")
        };
    }

    public void Dispose()
    {
        _stream.RunStarted -= OnRunReported;
        _stream.RunCompleted -= OnRunReported;
        _stream.TaskChanged -= OnTaskChanged;
        _snooze.Changed -= OnSnoozeChanged;
        _refreshTimer.Dispose();
        _closing.Cancel();
    }

    // The stream and the snooze report on threads of their own, and what the dashboard holds
    // belongs to the UI thread. What was posted there before the window closed can still arrive
    // after it.
    private void OnRunReported(object? sender, TaskRun run) => _ui.Post(() =>
    {
        if (_closing.IsCancellationRequested) return;
        Remember(run);
        if (!_isShown) return;
        _tasksThatRan.Add(run.TaskPath);
        Present();
        PlanRefresh();
    });

    private void OnTaskChanged(object? sender, TaskChange change) => _ui.Post(() =>
    {
        if (_closing.IsCancellationRequested || !_isShown) return;
        _tasksChanged = true;
        PlanRefresh();
    });

    private void OnSnoozeChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        if (!_closing.IsCancellationRequested && _isShown) Present();
    });

    private void Remember(TaskRun run)
    {
        _reported[DashboardFigures.KeyOf(run)] = run;

        // A window can stay open for weeks; what has left the period is of no use any more.
        DateTimeOffset since = _time.GetUtcNow() - DashboardFigures.Period;
        foreach (var (key, old) in _reported.ToList())
            if (!DashboardFigures.EndsAfter(old, since) || EndedUnlogged(old)) _reported.Remove(key);
    }

    /// <summary>
    /// True for a run that has no end and is over all the same: its task was seen idle after it
    /// began. Task Scheduler stopped logging while it ran, or the computer lost power. How it
    /// ended is not known, and it is left out rather than taken as going on for good.
    /// </summary>
    /// <remarks>
    /// Only a read of all tasks counts. The read of a task that a run's start set off can find
    /// a short run over already, a moment before its end is reported.
    /// </remarks>
    private bool EndedUnlogged(TaskRun run) =>
        run.Ended is null
        && _seenIdle.TryGetValue(run.TaskPath, out DateTimeOffset idle)
        && (run.Started ?? DateTimeOffset.MinValue) < idle;

    private void PlanRefresh()
    {
        if (_refreshPlanned) return;
        _refreshPlanned = true;
        _refreshTimer.Change(RefreshDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The read of the tasks that the last report set off. Tests wait on this instead of on the clock.</summary>
    internal Task PendingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads again what the reports since the last refresh concern: the tasks that ran, whose
    /// states and run times have moved on, or all tasks once one was registered, changed or
    /// deleted. The runs themselves are known already.
    /// </summary>
    private async Task RefreshAsync()
    {
        _refreshPlanned = false;

        // A refresh planned just before the page left the screen is done when it comes back.
        if (_closing.IsCancellationRequested || !_isShown) return;

        List<string> ran = [.. _tasksThatRan];
        bool changed = _tasksChanged;
        _tasksThatRan.Clear();
        _tasksChanged = false;

        // The figures are worked out in turn with the reads, so that figures worked out from these
        // tasks cannot replace those of a later read.
        await _reads.Enqueue(async () =>
        {
            try
            {
                await (changed ? ReadAllTasksAsync() : ReadTasksAsync(ran));
                Present();
            }
            catch (Exception ex)
            {
                // The reads deal with Task Scheduler's failures. Whatever went wrong in showing what
                // they found: the dashboard keeps what it shows, and the reads after this one go on.
                Serilog.Log.Warning(ex, "The dashboard could not show the tasks it read again");
            }
        });
    }

    /// <summary>Reads all tasks, and on the way whether Windows records history. Never throws.</summary>
    private async Task ReadAllTasksAsync()
    {
        DateTimeOffset readAt = _time.GetUtcNow();
        IReadOnlyList<TaskRow> tasks;
        LogState state;
        try
        {
            (tasks, state) = await Task.Run(() => (_store.GetTasks(), _history.GetLogState()));
        }
        catch (Exception ex)
        {
            // Whatever Task Scheduler answered: the dashboard keeps the tasks it had and says why.
            Serilog.Log.Error(ex, "The tasks could not be read for the dashboard");
            TasksProblem = Loc.Format("Dashboard.Tasks.Unavailable", ex.Message);
            return;
        }

        TasksProblem = null;
        _tasks = tasks;
        foreach (TaskRow task in tasks)
            if (task.State is TaskRowState.Ready or TaskRowState.Disabled) _seenIdle[task.Path] = readAt;
        IsLogOff = state == LogState.Disabled;
        ShowChoices();
    }

    /// <summary>Reads those of the given tasks again that the dashboard has, and drops one that is gone. Never throws.</summary>
    private async Task ReadTasksAsync(IReadOnlyCollection<string> paths)
    {
        // A run of a task the dashboard does not have is one of a task this user cannot see, or
        // of a new task, which the report of its registration brings in.
        HashSet<string> had = _tasks.Select(task => task.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> known = paths.Where(had.Contains).ToList();
        if (known.Count == 0) return;

        Dictionary<string, TaskRow?> fresh;
        try
        {
            fresh = await Task.Run(() => known.ToDictionary(path => path, _store.GetTask, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            // Whatever went wrong: the dashboard keeps what it has until the next report or refresh.
            Serilog.Log.Warning(ex, "The tasks that ran could not be read again for the dashboard");
            return;
        }

        _tasks = _tasks.Select(task => fresh.TryGetValue(task.Path, out TaskRow? row) ? row : task).OfType<TaskRow>().ToList();
        ShowChoices();
    }

    /// <summary>Works the figures out anew from what the dashboard holds, for the chosen category and tag.</summary>
    /// <remarks>
    /// Not while everything is read anew: figures worked out then would leave out what is still
    /// being read. The load works them out once it has it all, with whatever came in meanwhile.
    /// </remarks>
    private void Present()
    {
        if (IsLoading) return;

        string? category = Chosen(Categories, CategoryIndex), tag = Chosen(Tags, TagIndex);
        List<TaskRow> tasks = _tasks
            .Where(task => category is null || string.Equals(task.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(task => tag is null || task.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();

        IEnumerable<TaskRun> runs = _reported.Values
            .Concat(_recorded.Where(entry => !_reported.ContainsKey(entry.Key)).Select(entry => entry.Value))
            .Where(run => !EndedUnlogged(run));
        IEnumerable<SuppressedRun> refused = _snooze.State.SuppressedRuns;

        // Narrowed to a category or a tag, only what concerns those tasks counts. Otherwise all
        // of it does, also what concerns a task that has been deleted since or that this user
        // cannot see.
        if (category is not null || tag is not null)
        {
            var paths = tasks.Select(task => task.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            runs = runs.Where(run => paths.Contains(run.TaskPath));
            refused = refused.Where(start => paths.Contains(start.TaskPath));
        }

        Figures = DashboardFigures.From(tasks, runs, refused, Filter, _time.GetUtcNow(), _time.LocalTimeZone);
        Hours.SyncWith(Figures.Hours);
        Running.SyncWith(Figures.Running);
        Upcoming.SyncWith(Figures.Upcoming);
        Log.SyncWith(Figures.Log);
    }

    /// <summary>
    /// Brings the choices in line with the categories and tags the tasks have. A choice that no
    /// task has any more falls back to "all".
    /// </summary>
    private void ShowChoices()
    {
        string? category = Chosen(Categories, CategoryIndex), tag = Chosen(Tags, TagIndex);
        _choosing = true;
        try
        {
            Categories.SyncWith([Categories[0], .. Sorted(_tasks.Select(task => task.Category))]);
            Tags.SyncWith([Tags[0], .. Sorted(_tasks.SelectMany(task => task.Tags))]);
            CategoryIndex = PositionOf(Categories, category);
            TagIndex = PositionOf(Tags, tag);
        }
        finally
        {
            _choosing = false;
        }
    }

    private void OnChoiceChanged(int index, Action<int> choose)
    {
        if (_choosing) return;

        // A list control reports -1 when the entry it had selected is gone.
        if (index < 0) choose(0);
        else Present();
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> names) =>
        names.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase);

    private static string? Chosen(ObservableCollection<string> choices, int index) =>
        index > 0 && index < choices.Count ? choices[index] : null;

    /// <summary>The position of a choice; 0, which stands for all, when it is not among them.</summary>
    private static int PositionOf(ObservableCollection<string> choices, string? value)
    {
        for (int i = 1; i < choices.Count; i++)
            if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }
}
