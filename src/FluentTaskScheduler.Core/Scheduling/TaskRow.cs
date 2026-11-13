using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentTaskScheduler.Scheduling;

/// <summary>One task as the task list shows it.</summary>
public sealed partial class TaskRow : ObservableObject
{
    public TaskRow(string path)
    {
        Path = path;
        int separator = path.LastIndexOf('\\');
        Name = path[(separator + 1)..];
        FolderPath = separator <= 0 ? "\\" : path[..separator];
    }

    /// <summary>Full path in Task Scheduler, e.g. \Backups\Nightly. Identifies the task.</summary>
    public string Path { get; }
    public string Name { get; }
    /// <summary>Folder the task lives in; "\" for the root.</summary>
    public string FolderPath { get; }

    [ObservableProperty] private TaskRowState _state = TaskRowState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnabledSwitch))]
    private bool _isEnabled;

    /// <summary>
    /// For the switch in the task's row. It shows <see cref="IsEnabled"/>; setting it changes
    /// nothing here but asks, through <see cref="EnabledChangeRequested"/>, for the task to be
    /// enabled or disabled. If nobody does that, the switch goes back.
    /// </summary>
    /// <remarks>
    /// A list reuses its rows for other tasks, and a switch that reported "toggled" while that
    /// happens could be taken for the user's wish to change whichever task it shows next. Bound
    /// both ways to this property, the switch can only ever speak for its own task.
    /// </remarks>
    public bool EnabledSwitch
    {
        get => IsEnabled;
        set
        {
            if (value == IsEnabled) return;
            EnabledChangeRequested?.Invoke(this, value);
            if (value != IsEnabled) OnPropertyChanged();
        }
    }

    /// <summary>The user asked for the task to be enabled (true) or disabled.</summary>
    public event Action<TaskRow, bool>? EnabledChangeRequested;

    /// <summary>True from the moment the task is started here until it is seen to have finished, so the row can show progress without waiting for a refresh.</summary>
    [ObservableProperty] private bool _isRunning;

    /// <summary>A snooze keeps the task disabled: one of its own, or the snooze of all tasks. This is the app's, so <see cref="UpdateFrom"/> leaves it alone.</summary>
    [ObservableProperty] private bool _isSnoozed;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _category = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagsText))]
    private IReadOnlyList<string> _tags = Array.Empty<string>();

    [ObservableProperty] private DateTime? _lastRunTime;
    [ObservableProperty] private DateTime? _nextRunTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompletionActions))]
    private CompletionActions _completionActions = CompletionActions.None;

    /// <summary>Why the task's definition could not be read, if it could not. The task is still listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    public string TagsText => string.Join(", ", Tags);

    /// <summary>The name: what a list shows for an item it has no template for, and what a screen reader announces for the row.</summary>
    public override string ToString() => Name;
    public bool HasCompletionActions => CompletionActions.IsActive;
    public bool HasLoadError => LoadError is not null;

    /// <summary>Takes over the state of a freshly read row for the same task. Selection and the running marker are kept.</summary>
    public void UpdateFrom(TaskRow fresh)
    {
        State = fresh.State;
        IsEnabled = fresh.IsEnabled;
        IsHidden = fresh.IsHidden;
        Description = fresh.Description;
        Author = fresh.Author;
        Category = fresh.Category;
        if (!SameItems(Tags, fresh.Tags)) Tags = fresh.Tags;
        LastRunTime = fresh.LastRunTime;
        NextRunTime = fresh.NextRunTime;
        CompletionActions = fresh.CompletionActions;
        LoadError = fresh.LoadError;
    }

    private static bool SameItems(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
