using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>How far back a task's history is shown.</summary>
public enum HistoryPeriod
{
    Today,
    Yesterday,
    LastSevenDays,
    Everything
}

/// <summary>Narrows a task's history to the events of runs that ended one way.</summary>
public enum HistoryFilter
{
    All,
    Succeeded,
    Failed
}

/// <summary>One event of a task's history, worded for the list.</summary>
/// <param name="Run">How the run the event belongs to ended; null for an event outside of any run.</param>
public sealed record HistoryEntry(TaskEvent Event, RunStatus? Run, string Time, string Label, string Details);

public sealed class TaskDetailsFactory(ITaskStore store, ITaskHistory history, ISnoozeService snooze, IFilePicker files, IClipboard clipboard,
    IAppLifetime app, IUiThread ui, TimeProvider time)
{
    public TaskDetailsViewModel Create(TaskRow task) => new(task, store, history, snooze, files, clipboard, app, ui, time);
}

/// <summary>
/// What the app knows about one task beyond its row: what starts it, what it runs, what became of
/// its runs, and whether it is snoozed. Dispose of it when the details close.
/// </summary>
public sealed partial class TaskDetailsViewModel : ObservableObject, IDisposable
{
    /// <summary>The newest events of the chosen period; more would only slow the list down.</summary>
    internal const int MaxEvents = 500;

    private static readonly TimeSpan Surroundings = TimeSpan.FromMinutes(5);

    private readonly ITaskStore _store;
    private readonly ITaskHistory _history;
    private readonly ISnoozeService _snooze;
    private readonly IFilePicker _files;
    private readonly IClipboard _clipboard;
    private readonly IAppLifetime _app;
    private readonly IUiThread _ui;
    private readonly TimeProvider _time;

    private IReadOnlyList<HistoryEntry> _all = [];
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _lookingAround;
    private SnoozeWindow? _snoozeWindow;
    private bool _disposed;

    internal TaskDetailsViewModel(TaskRow task, ITaskStore store, ITaskHistory history, ISnoozeService snooze, IFilePicker files, IClipboard clipboard,
        IAppLifetime app, IUiThread ui, TimeProvider time)
    {
        Row = task;
        _store = store;
        _history = history;
        _snooze = snooze;
        _files = files;
        _clipboard = clipboard;
        _app = app;
        _ui = ui;
        _time = time;
        _snoozeWindow = snooze.State.WindowOf(task.Path);
        _snooze.Changed += OnSnoozeChanged;
    }

    public TaskRow Row { get; }

    /// <summary>True while the task has a snooze of its own.</summary>
    public bool IsSnoozed => _snoozeWindow is not null;

    /// <summary>Until when the task's own snooze lasts, e.g. "Snoozed until 14:30"; empty while it has none.</summary>
    public string SnoozeStatus => _snoozeWindow is { } window ? SnoozeText.OneTask(window, _time) : "";

    /// <summary>Why the task was not snoozed, when that was asked for last; null when it was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnoozeProblem))]
    private string? _snoozeProblem;

    public bool HasSnoozeProblem => SnoozeProblem is not null;

    /// <summary>What starts the task, one line per trigger. Empty until loaded, and for a task that cannot be read.</summary>
    [ObservableProperty] private IReadOnlyList<string> _triggers = [];

    /// <summary>What the task runs, one line per action.</summary>
    [ObservableProperty] private IReadOnlyList<string> _actions = [];

    /// <summary>The events of the chosen period that pass the filter, newest first.</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoHistory))]
    private bool _isLoading;

    /// <summary>Index into <see cref="HistoryPeriod"/>; changing it reads the history anew.</summary>
    [ObservableProperty] private int _periodIndex = (int)HistoryPeriod.LastSevenDays;

    [ObservableProperty] private HistoryFilter _filter;

    /// <summary>Windows is not recording history at the moment. What was recorded earlier is still shown.</summary>
    [ObservableProperty] private bool _isLogOff;

    /// <summary>Why the history could not be read, switched on, exported or copied, in the user's language.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public bool HasProblem => Problem is not null;

    [ObservableProperty] private int _totalRuns;
    [ObservableProperty] private int _succeededRuns;
    [ObservableProperty] private int _failedRuns;

    /// <summary>How the newest finished run ended, with its result code; a dash while there is none.</summary>
    [ObservableProperty] private string _lastResult = "-";

    public bool ShowsNoHistory => !IsLoading && Entries.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private HistoryEntry? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>Everything the log says about the selected event, one fact per line.</summary>
    [ObservableProperty] private string _selectedFacts = "";

    /// <summary>What happened to the computer around the selected event: restarts, sleep, logons. One line each.</summary>
    [ObservableProperty] private IReadOnlyList<string> _selectedSurroundings = [];

    /// <summary>Reads the task's summary and its history. Call once when the details open.</summary>
    public async Task LoadAsync()
    {
        Task summary = LoadSummaryAsync();
        await LoadHistoryAsync();
        await summary;
    }

    private async Task LoadSummaryAsync()
    {
        try
        {
            TaskDraft draft = await Task.Run(() => _store.Load(Row.Path));
            Triggers = draft.Triggers.Select(TriggerText.Describe).ToList();
            Actions = draft.Actions.Select(ActionText.Describe).ToList();
        }
        catch (Exception ex)
        {
            // Any task on the computer can be opened here, including ones this user may not read
            // and ones the scheduler library cannot interpret. The row already says when a task
            // cannot be read, and its history may still be there.
            Serilog.Log.Information(ex, "The definition of {Path} could not be read for its details", Row.Path);
        }
    }

    partial void OnPeriodIndexChanged(int oldValue, int newValue)
    {
        // A list control reports -1 while its items are being replaced.
        if (newValue < 0 || newValue > (int)HistoryPeriod.Everything) PeriodIndex = oldValue;
        else _ = LoadHistoryAsync();
    }

    partial void OnFilterChanged(HistoryFilter value) => ApplyFilter();

    [RelayCommand]
    private void Show(HistoryFilter filter) => Filter = filter;

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        // The user's days and times are those of the time zone Windows uses now. .NET keeps the
        // zone it read first until its cache is cleared.
        TimeZoneInfo.ClearCachedData();

        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        IsLoading = true;
        Problem = null;
        try
        {
            (DateTimeOffset? from, DateTimeOffset? to) = Bounds((HistoryPeriod)PeriodIndex);
            var (state, events) = await Task.Run(() =>
                (_history.GetLogState(), _history.GetEvents(Row.Path, MaxEvents, from, to, loading.Token)), loading.Token);
            if (loading.IsCancellationRequested) return;

            IsLogOff = state == LogState.Disabled;
            Present(events);
        }
        catch (OperationCanceledException)
        {
            return; // a newer request is on its way
        }
        catch (HistoryUnavailableException ex)
        {
            Present([]);
            Problem = Loc.Format("Details.History.Unavailable", ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (_loading == loading) IsLoading = false;
        }
    }

    private void Present(IReadOnlyList<TaskEvent> events)
    {
        IReadOnlyList<TaskRun> runs = TaskRun.FromEvents(events);
        var runOfRecord = new Dictionary<long, RunStatus>();
        foreach (TaskRun run in runs)
            foreach (TaskEvent e in run.Events) runOfRecord[e.RecordId] = run.Status;

        TimeZoneInfo zone = _time.LocalTimeZone;
        _all = events.Select(e => new HistoryEntry(
            e,
            runOfRecord.TryGetValue(e.RecordId, out RunStatus status) ? status : null,
            TimeZoneInfo.ConvertTime(e.Time, zone).ToString("G", CultureInfo.CurrentCulture),
            HistoryText.Label(e),
            Summarize(e))).ToList();

        TotalRuns = runs.Count;
        SucceededRuns = runs.Count(run => run.Status == RunStatus.Succeeded);
        FailedRuns = runs.Count(run => IsFailure(run.Status));
        LastResult = runs.FirstOrDefault(run => run.IsFinished) is { } last
            ? last.ResultCode is { } code ? $"{HistoryText.Status(last.Status)} ({HistoryText.Code(code)})" : HistoryText.Status(last.Status)
            : "-";
        ApplyFilter();
    }

    private static bool IsFailure(RunStatus status) => status is RunStatus.Failed or RunStatus.CouldNotStart or RunStatus.TimedOut;

    private void ApplyFilter()
    {
        Selected = null;
        Entries.Clear();
        foreach (HistoryEntry entry in _all.Where(Passes)) Entries.Add(entry);
        OnPropertyChanged(nameof(ShowsNoHistory));
    }

    private bool Passes(HistoryEntry entry) => Filter switch
    {
        HistoryFilter.Succeeded => entry.Run == RunStatus.Succeeded,
        HistoryFilter.Failed => entry.Run is { } status && IsFailure(status),
        _ => true
    };

    private static string Summarize(TaskEvent e)
    {
        var parts = new List<string>(3);
        if (e.ResultCode is { } code) parts.Add(Loc.Format("Details.History.ResultFormat", HistoryText.Code(code)));
        if (e.Action.Length > 0) parts.Add(e.Action);
        if (e.User.Length > 0) parts.Add(e.User);
        return string.Join(" · ", parts);
    }

    // Days are the user's days: they begin at local midnight. On a day daylight saving time begins
    // or ends, midnight has another offset than the rest of the day.
    private (DateTimeOffset? From, DateTimeOffset? To) Bounds(HistoryPeriod period)
    {
        DateTime today = _time.GetLocalNow().Date;
        DateTimeOffset Midnight(DateTime day) => new(day, _time.LocalTimeZone.GetUtcOffset(day));
        return period switch
        {
            HistoryPeriod.Today => (Midnight(today), null),
            HistoryPeriod.Yesterday => (Midnight(today.AddDays(-1)), Midnight(today)),
            HistoryPeriod.LastSevenDays => (Midnight(today.AddDays(-6)), null),
            _ => (null, null)
        };
    }

    partial void OnSelectedChanged(HistoryEntry? value)
    {
        _lookingAround?.Cancel();
        SelectedSurroundings = [];
        SelectedFacts = value is null ? "" : Facts(value);
        if (value is not null) _ = LookAroundAsync(value);
    }

    private static string Facts(HistoryEntry entry)
    {
        TaskEvent e = entry.Event;
        var facts = new StringBuilder();
        facts.AppendLine(CultureInfo.CurrentCulture, $"{entry.Time}  {entry.Label}");
        facts.AppendLine(Loc.Format("Details.History.EventFormat", e.EventId, HistoryText.Level(e.Level)));
        if (e.ResultCode is { } code) facts.AppendLine(Loc.Format("Details.History.ResultFormat", HistoryText.Code(code)));
        if (e.Action.Length > 0) facts.AppendLine(Loc.Format("Details.History.ActionFormat", e.Action));
        if (e.User.Length > 0) facts.AppendLine(Loc.Format("Details.History.UserFormat", e.User));
        if (e.InstanceId is { } instance) facts.AppendLine(Loc.Format("Details.History.RunFormat", instance));
        return facts.ToString().TrimEnd();
    }

    private async Task LookAroundAsync(HistoryEntry entry)
    {
        var looking = _lookingAround = new CancellationTokenSource();
        try
        {
            IReadOnlyList<SystemEvent> events = await Task.Run(
                () => _history.GetSystemEvents(entry.Event.Time, Surroundings, looking.Token), looking.Token);
            if (looking.IsCancellationRequested || Selected != entry) return;

            TimeZoneInfo zone = _time.LocalTimeZone;
            SelectedSurroundings = events
                .Select(e => $"{TimeZoneInfo.ConvertTime(e.Time, zone).ToString("T", CultureInfo.CurrentCulture)}  {HistoryText.Label(e.Kind)}")
                .ToList();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HistoryUnavailableException)
        {
            // What surrounded the event is a help, not something to report as missing.
        }
    }

    /// <summary>Asks Windows to record history from now on.</summary>
    [RelayCommand]
    private async Task SwitchLogOnAsync()
    {
        EnableLogResult result = await Task.Run(_history.EnableLog);
        IsLogOff = result != EnableLogResult.Enabled;
        Problem = result switch
        {
            EnableLogResult.Enabled => null,
            EnableLogResult.AccessDenied => Loc.Get("Details.History.SwitchOnDenied"),
            _ => Loc.Get("Details.History.SwitchOnFailed")
        };
    }

    /// <summary>Saves the events that are listed as a CSV file the user chooses.</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        string? file = await _files.PickSaveFileAsync(Loc.Get("Details.History.ExportTitle"), "CSV", "csv", Row.Name + "_history");
        if (file is null) return;
        try
        {
            byte[] csv = HistoryCsv.Write(Entries.Select(entry => entry.Event), _time.LocalTimeZone);
            await File.WriteAllBytesAsync(file, csv);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = Loc.Format("Details.History.ExportFailed", ex.Message);
        }
    }

    /// <summary>Puts the listed events on the clipboard, one line each, columns separated by tabs.</summary>
    [RelayCommand]
    private void Copy()
    {
        if (!_clipboard.SetText(string.Join(Environment.NewLine, Entries.Select(entry => $"{entry.Time}\t{entry.Label}\t{entry.Details}"))))
            Problem = Loc.Get("Clipboard.Busy");
    }

    [RelayCommand]
    private async Task SnoozeAsync(SnoozeChoice choice) => SnoozeProblem = await TrySnoozeAsync(choice.End);

    [RelayCommand]
    private Task ResumeSnoozeAsync()
    {
        SnoozeProblem = null;
        return _snooze.ResumeTaskAsync(Row.Path);
    }

    /// <summary>For the dialog that snoozes the task until a moment of the user's choosing.</summary>
    public SnoozeUntilViewModel PickSnoozeMoment() => new(Row.Name, Loc.Get("Task.Snooze.CustomIntro"), null, _time, TrySnoozeAsync);

    public void Dispose()
    {
        _disposed = true;
        _snooze.Changed -= OnSnoozeChanged;
        // What is still being read, of the history or of what surrounded an event, is of no use now.
        _loading?.Cancel();
        _lookingAround?.Cancel();
    }

    /// <returns>Why the task was not snoozed; null when it was.</returns>
    private async Task<string?> TrySnoozeAsync(SnoozeEnd end)
    {
        try
        {
            return await _snooze.SnoozeTaskAsync(Row.Path, end) == SnoozeTaskOutcome.AlreadyDisabled ? Loc.Get("Task.Snooze.AlreadyDisabled") : null;
        }
        catch (TaskNotFoundException)
        {
            return Loc.Format("Tasks.Error.Gone", Row.Name);
        }
        catch (TaskAccessDeniedException)
        {
            return Loc.Format(_app.IsElevated ? "Tasks.Error.Denied" : "Tasks.Error.DeniedNotElevated", Row.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Format("Snooze.Error.NotRecorded", ex.Message);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Any other answer of Task Scheduler, worded as the task list words it.
            return Loc.Format("Tasks.Error.OtherFormat", Row.Name, ex.Message);
        }
    }

    // The service reports on whatever thread made the change, which can be after the details closed.
    private void OnSnoozeChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        if (_disposed) return;
        _snoozeWindow = _snooze.State.WindowOf(Row.Path);
        OnPropertyChanged(nameof(IsSnoozed));
        OnPropertyChanged(nameof(SnoozeStatus));
    });
}
