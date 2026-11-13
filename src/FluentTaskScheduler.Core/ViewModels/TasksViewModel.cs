using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;

namespace FluentTaskScheduler.ViewModels;

/// <summary>How the task list is ordered.</summary>
public enum TaskSort
{
    /// <summary>The order Task Scheduler lists them in: folder by folder.</summary>
    None,
    Name,
    Status,
    NextRun,
    LastRun
}

/// <summary>Which tasks the list shows, by state. The order is that of the filter's list.</summary>
public enum TaskStatusFilter
{
    All,
    Running,
    Enabled,
    Disabled
}

/// <summary>
/// The task list of one window: which tasks there are, which of them are shown and in what
/// order, what is selected, and everything that can be done to tasks from the list.
/// </summary>
public sealed partial class TasksViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LongestWatch = TimeSpan.FromMinutes(10);

    private readonly ITaskStore _store;
    private readonly ITaskRunner _runner;
    private readonly ISettingsStore _settings;
    private readonly IPrompts _prompts;
    private readonly IFilePicker _files;
    private readonly IAppLifetime _app;
    private readonly IUiThread _ui;
    private readonly TimeProvider _time;
    private readonly TaskEditorFactory _editors;
    private readonly ITaskFolders _folders;

    // Every task, by path. The rows in here are the ones shown: a reload updates them in place,
    // so selection and scroll position survive it.
    private readonly Dictionary<string, TaskRow> _all = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _storeOrder = [];
    private readonly Dictionary<string, DateTimeOffset> _watched = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITimer _watchTimer;
    private IReadOnlyList<TaskRow> _selection = [];

    public TasksViewModel(ITaskStore store, ITaskRunner runner, ISettingsStore settings, IPrompts prompts, IFilePicker files,
        IAppLifetime app, IUiThread ui, TimeProvider time, TaskEditorFactory editors, ITaskFolders folders)
    {
        _folders = folders;
        _store = store;
        _runner = runner;
        _settings = settings;
        _prompts = prompts;
        _files = files;
        _app = app;
        _ui = ui;
        _time = time;
        _editors = editors;
        _watchTimer = time.CreateTimer(_ => ui.Post(() => _ = CheckWatchedAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>The tasks to show, in the order to show them.</summary>
    public ObservableCollection<TaskRow> Tasks { get; } = [];

    [ObservableProperty] private bool _isLoading;

    /// <summary>Why the tasks could not be loaded, if they could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    public bool HasLoadError => LoadError is not null;

    [ObservableProperty] private string _searchText = "";

    /// <summary>Position of the selected <see cref="TaskStatusFilter"/> in the filter's list.</summary>
    [ObservableProperty] private int _statusFilterIndex;

    /// <summary>The folder whose tasks are shown; null for the tasks of all folders.</summary>
    [ObservableProperty] private string? _folderPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortText))]
    private TaskSort _sort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortText))]
    private bool _sortAscending = true;

    /// <summary>The caption of the sort button: what the list is sorted by, if anything.</summary>
    public string SortText => Sort == TaskSort.None
        ? Loc.Get("Main.Toolbar.SortButton")
        : Loc.Format("Main.Toolbar.SortActiveFormat", SortAscending ? "▲" : "▼", Loc.Get("Main.Sort." + Sort));

    /// <summary>
    /// False for a process without administrator rights: Windows then only lists the tasks this
    /// user may read, and the list says so instead of looking complete.
    /// </summary>
    public bool ShowsEveryTask => _app.IsElevated;

    /// <summary>The task that single-task commands apply to: the only selected one.</summary>
    public TaskRow? SelectedTask => _selection.Count == 1 ? _selection[0] : null;

    public bool HasSelection => _selection.Count > 0;
    public bool HasSeveralSelected => _selection.Count > 1;
    public string SelectionText => Loc.Format("Main.Batch.SelectedCountFormat", _selection.Count);

    /// <summary>Paths of all tasks, for pickers.</summary>
    public IReadOnlyCollection<string> AllTaskPaths => _all.Keys;

    partial void OnSearchTextChanged(string value) => Refilter();
    partial void OnStatusFilterIndexChanged(int value) => Refilter();
    partial void OnFolderPathChanged(string? value) => Refilter();
    partial void OnSortChanged(TaskSort value) => Refilter();
    partial void OnSortAscendingChanged(bool value) => Refilter();

    /// <summary>Tells the view model what the list control has selected.</summary>
    public void Select(IReadOnlyList<TaskRow> rows)
    {
        foreach (TaskRow row in _selection.Except(rows)) row.IsSelected = false;
        foreach (TaskRow row in rows) row.IsSelected = true;
        _selection = rows;

        OnPropertyChanged(nameof(SelectedTask));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSeveralSelected));
        OnPropertyChanged(nameof(SelectionText));
    }

    /// <summary>Sorts by <paramref name="by"/>; choosing the current order again reverses it.</summary>
    [RelayCommand]
    private void SortBy(TaskSort by)
    {
        if (by == Sort && by != TaskSort.None) SortAscending = !SortAscending;
        else (Sort, SortAscending) = (by, true);
    }

    /// <summary>Reads the tasks from Task Scheduler and brings the list up to date.</summary>
    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            IReadOnlyList<TaskRow> fresh = await Task.Run(_store.GetTasks);
            Merge(fresh);
            LoadError = null;
        }
        catch (Exception ex)
        {
            // Whatever Task Scheduler answered: the list keeps what it had and says that it is stale.
            Serilog.Log.Error(ex, "The tasks could not be loaded");
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Starts <paramref name="row"/>, or without one every selected task.</summary>
    [RelayCommand]
    private async Task RunAsync(TaskRow? row)
    {
        var failures = new List<string>();
        int suppressed = 0;
        foreach (TaskRow task in Targets(row))
        {
            try
            {
                if (await Task.Run(() => _runner.Run(task.Path)) == RunOutcome.Suppressed)
                {
                    suppressed++;
                    continue;
                }
                task.State = TaskRowState.Running;
                task.IsRunning = true;
                Watch(task.Path);
            }
            catch (Exception ex) when (IsTaskFailure(ex))
            {
                failures.Add(Describe(task, ex));
            }
        }

        if (suppressed > 0) await _prompts.ShowMessageAsync(Loc.Get("Tasks.Snoozed.Title"), Loc.Format("Snooze.Error.BatchBlocked", suppressed));
        await ReportAsync(failures);
    }

    [RelayCommand]
    private async Task StopAsync(TaskRow? row)
    {
        var failures = new List<string>();
        foreach (TaskRow task in Targets(row))
        {
            try
            {
                await Task.Run(() => _store.Stop(task.Path));
                task.IsRunning = false;
                Watch(task.Path); // picks up the state it settles in
            }
            catch (Exception ex) when (IsTaskFailure(ex))
            {
                failures.Add(Describe(task, ex));
            }
        }
        await ReportAsync(failures);
    }

    /// <summary>Enables or disables one task. If Windows refuses, the row goes back to what it was.</summary>
    public async Task SetEnabledAsync(TaskRow row, bool enabled)
    {
        if (row.IsEnabled == enabled) return;
        row.IsEnabled = enabled;
        try
        {
            await Task.Run(() => _store.SetEnabled(row.Path, enabled));
            row.State = enabled ? TaskRowState.Ready : TaskRowState.Disabled;
        }
        catch (Exception ex) when (IsTaskFailure(ex))
        {
            row.IsEnabled = !enabled;
            await ReportAsync([Describe(row, ex)]);
        }
        Refilter();
    }

    [RelayCommand]
    private Task EnableSelectedAsync() => SetSelectedEnabledAsync(true);

    [RelayCommand]
    private Task DisableSelectedAsync() => SetSelectedEnabledAsync(false);

    /// <summary>Deletes <paramref name="row"/>, or without one every selected task, after asking if the settings say to.</summary>
    [RelayCommand]
    private async Task DeleteAsync(TaskRow? row)
    {
        List<TaskRow> targets = Targets(row);
        if (targets.Count == 0) return;

        if (_settings.Current.ConfirmDelete)
        {
            string question = targets.Count == 1
                ? Loc.Format("Dialog.DeleteTask.ContentFormat", targets[0].Name)
                : Loc.Format("Dialog.BatchDelete.ContentFormat", targets.Count);
            if (!await _prompts.ConfirmAsync(Loc.Get("Dialog.ConfirmDelete.Title"), question, Loc.Get("Dialog.Common.Delete"), isDestructive: true)) return;
        }

        var failures = new List<string>();
        foreach (TaskRow task in targets)
        {
            try
            {
                await Task.Run(() => _store.Delete(task.Path));
                Remove(task);
            }
            catch (TaskNotFoundException)
            {
                Remove(task); // someone else was faster; either way it is gone
            }
            catch (Exception ex) when (IsTaskFailure(ex))
            {
                failures.Add(Describe(task, ex));
            }
        }
        await ReportAsync(failures);
    }

    /// <summary>Saves the selected task as an XML file of the user's choosing.</summary>
    [RelayCommand]
    private async Task ExportAsync(TaskRow? row)
    {
        if ((row ?? SelectedTask) is not { } task) return;
        if (await _files.PickSaveFileAsync(Loc.Get("Main.Task.Export"), Loc.Get("Tasks.File.TaskXml"), "xml", task.Name) is not { } file) return;

        try
        {
            string xml = await Task.Run(() => _store.Export(task.Path));
            // Task XML declares UTF-16, so that is what the file has to be.
            await File.WriteAllTextAsync(file, xml, Encoding.Unicode);
        }
        catch (Exception ex) when (IsTaskFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            await ReportAsync([Describe(task, ex)]);
        }
    }

    /// <summary>
    /// Lets the user pick an exported task and prepares its import; null when the user cancels or
    /// the file is not a task, which the user is told.
    /// </summary>
    public async Task<ImportTaskViewModel?> BeginImportAsync()
    {
        if (await _files.PickOpenFileAsync(Loc.Get("Dialog.ImportTask.Title"), Loc.Get("Tasks.File.TaskXml"), "xml") is not { } file) return null;

        try
        {
            string xml = await File.ReadAllTextAsync(file);
            var (info, folders) = await Task.Run(() => (_store.Inspect(xml), _folders.GetFolders()));
            return new ImportTaskViewModel(_store, _app, xml, info, Path.GetFileNameWithoutExtension(file), folders, FolderPath);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            await _prompts.ShowErrorAsync(Loc.Format("Import.Error.NotATask", Path.GetFileName(file), ex.Message));
            return null;
        }
    }

    /// <summary>The row of the task at <paramref name="path"/>, if the list has it.</summary>
    public TaskRow? Find(string path) => _all.GetValueOrDefault(path);

    /// <summary>An editor for a new task in the folder being shown; it starts out as <paramref name="template"/>, or as an empty daily task.</summary>
    public TaskEditorViewModel EditNew(TaskDraft? template = null) =>
        _editors.Create(template ?? NewTask.Draft(_time.GetLocalNow().DateTime), FolderPath ?? "\\", originalPath: null, _all.Keys);

    /// <summary>Loads <paramref name="row"/> into an editor; null if it cannot be loaded, which the user is told.</summary>
    public async Task<TaskEditorViewModel?> EditAsync(TaskRow row)
    {
        try
        {
            TaskDraft draft = await Task.Run(() => _store.Load(row.Path));
            return _editors.Create(draft, row.FolderPath, row.Path, _all.Keys);
        }
        catch (TaskNotFoundException)
        {
            Remove(row);
            await _prompts.ShowErrorAsync(Loc.Format("Tasks.Error.Gone", row.Name));
            return null;
        }
        catch (Exception ex) when (IsTaskFailure(ex))
        {
            await ReportAsync([Describe(row, ex)]);
            return null;
        }
    }

    /// <summary>Restarts the app with administrator rights, so that it can list and change every task.</summary>
    [RelayCommand]
    private void RestartElevated() => _app.RestartElevated();

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _watchTimer.Dispose();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Current.ShowHiddenTasks != e.Previous.ShowHiddenTasks) _ui.Post(Refilter);
    }

    private async Task SetSelectedEnabledAsync(bool enabled)
    {
        var failures = new List<string>();
        foreach (TaskRow task in _selection.Where(task => task.IsEnabled != enabled).ToList())
        {
            try
            {
                await Task.Run(() => _store.SetEnabled(task.Path, enabled));
                task.IsEnabled = enabled;
                task.State = enabled ? TaskRowState.Ready : TaskRowState.Disabled;
            }
            catch (Exception ex) when (IsTaskFailure(ex))
            {
                failures.Add(Describe(task, ex));
            }
        }
        Refilter();
        await ReportAsync(failures);
    }

    private List<TaskRow> Targets(TaskRow? row) => row is not null ? [row] : [.. _selection];

    private void Merge(IReadOnlyList<TaskRow> fresh)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TaskRow row in fresh)
        {
            seen.Add(row.Path);
            if (_all.TryGetValue(row.Path, out TaskRow? existing)) existing.UpdateFrom(row);
            else _all[row.Path] = row;
        }
        foreach (string gone in _all.Keys.Where(path => !seen.Contains(path)).ToList()) _all.Remove(gone);

        _storeOrder = fresh.Select(row => row.Path).ToList();
        Refilter();
    }

    private void Remove(TaskRow row)
    {
        _all.Remove(row.Path);
        _storeOrder.Remove(row.Path);
        _watched.Remove(row.Path);
        if (_selection.Contains(row)) Select(_selection.Where(selected => selected != row).ToList());
        Tasks.Remove(row);
    }

    /// <summary>Brings <see cref="Tasks"/> in line with the filters and the sort order, moving as few rows as possible.</summary>
    private void Refilter()
    {
        List<TaskRow> wanted = Ordered(_storeOrder.Select(path => _all[path]).Where(Matches)).ToList();

        var keep = new HashSet<TaskRow>(wanted);
        for (int i = Tasks.Count - 1; i >= 0; i--)
            if (!keep.Contains(Tasks[i])) Tasks.RemoveAt(i);

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < Tasks.Count && ReferenceEquals(Tasks[i], wanted[i])) continue;
            int current = Tasks.IndexOf(wanted[i]);
            if (current < 0) Tasks.Insert(i, wanted[i]);
            else Tasks.Move(current, i);
        }
    }

    private bool Matches(TaskRow row)
    {
        if (row.IsHidden && !_settings.Current.ShowHiddenTasks) return false;
        if (FolderPath is { } folder && !string.Equals(row.FolderPath, folder, StringComparison.OrdinalIgnoreCase)) return false;

        bool statusFits = (TaskStatusFilter)StatusFilterIndex switch
        {
            TaskStatusFilter.Running => row.IsRunning || row.State == TaskRowState.Running,
            TaskStatusFilter.Enabled => row.IsEnabled,
            TaskStatusFilter.Disabled => !row.IsEnabled,
            _ => true
        };
        if (!statusFits) return false;

        string search = SearchText.Trim();
        return search.Length == 0
            || row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || row.Path.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || row.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || row.Tags.Any(tag => tag.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private IEnumerable<TaskRow> Ordered(IEnumerable<TaskRow> rows)
    {
        // A task that never ran, or will not run again, goes last whichever way the dates are sorted.
        IOrderedEnumerable<TaskRow> ByDate(Func<TaskRow, DateTime?> date) =>
            SortAscending
                ? rows.OrderBy(row => date(row) is null).ThenBy(date)
                : rows.OrderBy(row => date(row) is null).ThenByDescending(date);

        return Sort switch
        {
            TaskSort.Name => SortAscending
                ? rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                : rows.OrderByDescending(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            TaskSort.Status => SortAscending ? rows.OrderBy(row => row.State) : rows.OrderByDescending(row => row.State),
            TaskSort.NextRun => ByDate(row => row.NextRunTime),
            TaskSort.LastRun => ByDate(row => row.LastRunTime),
            _ => rows
        };
    }

    // After a start or a stop the state changes without the app doing anything more. Until it has
    // settled, the task is asked about every two seconds; all watched tasks share one timer.
    private void Watch(string path)
    {
        _watched[path] = _time.GetUtcNow();
        _watchTimer.Change(WatchInterval, WatchInterval);
    }

    /// <summary>Asks Task Scheduler about the watched tasks and stops watching those that have settled. Runs on the UI thread.</summary>
    internal async Task CheckWatchedAsync()
    {
        List<string> paths = [.. _watched.Keys];
        if (paths.Count == 0) return;

        IReadOnlyDictionary<string, TaskRowState> states;
        try
        {
            states = await Task.Run(() => _store.GetStates(paths));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "The state of the started tasks could not be read; trying again");
            return;
        }

        foreach (string path in paths)
        {
            if (!_all.TryGetValue(path, out TaskRow? row) || !states.TryGetValue(path, out TaskRowState state))
            {
                _watched.Remove(path);
                continue;
            }

            row.State = state;
            bool watchedTooLong = _watched.TryGetValue(path, out DateTimeOffset since) && _time.GetUtcNow() - since > LongestWatch;
            if (state == TaskRowState.Running && !watchedTooLong) continue;

            row.IsRunning = false;
            _watched.Remove(path);
        }

        if (_watched.Count == 0) _watchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if ((TaskStatusFilter)StatusFilterIndex != TaskStatusFilter.All || Sort == TaskSort.Status) Refilter();
    }

    private static bool IsTaskFailure(Exception ex) =>
        ex is TaskNotFoundException or TaskAccessDeniedException or TaskAccountException
            or System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException;

    private string Describe(TaskRow task, Exception ex) => ex switch
    {
        TaskAccessDeniedException => Loc.Format(_app.IsElevated ? "Tasks.Error.Denied" : "Tasks.Error.DeniedNotElevated", task.Name),
        TaskNotFoundException => Loc.Format("Tasks.Error.Gone", task.Name),
        _ => $"{task.Name}: {ex.Message}"
    };

    private Task ReportAsync(List<string> failures) =>
        failures.Count == 0 ? Task.CompletedTask : _prompts.ShowErrorAsync(string.Join(Environment.NewLine, failures));
}
