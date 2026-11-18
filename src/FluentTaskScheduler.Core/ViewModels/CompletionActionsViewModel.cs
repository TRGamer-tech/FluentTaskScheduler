using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.ViewModels;

/// <summary>The "when this task finishes, start these" part of the task editor.</summary>
public sealed partial class CompletionActionsViewModel : ObservableObject
{
    public CompletionActionsViewModel(CompletionActions source, IEnumerable<string> otherTasks)
    {
        _enabled = source.Enabled;
        OnSuccess = new ObservableCollection<string>(source.OnSuccess);
        OnFailure = new ObservableCollection<string>(source.OnFailure);
        OtherTasks = otherTasks.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        OnSuccess.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StartsSameTaskEitherWay));
        OnFailure.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StartsSameTaskEitherWay));
    }

    [ObservableProperty] private bool _enabled;

    /// <summary>Paths of the tasks to start after a successful run.</summary>
    public ObservableCollection<string> OnSuccess { get; }

    /// <summary>Paths of the tasks to start after a failed run.</summary>
    public ObservableCollection<string> OnFailure { get; }

    /// <summary>The tasks that can be chosen: every task except the one being edited.</summary>
    public IReadOnlyList<string> OtherTasks { get; }

    /// <summary>True when a task is started whatever the outcome, which is allowed but usually a mistake.</summary>
    public bool StartsSameTaskEitherWay => OnSuccess.Intersect(OnFailure, StringComparer.OrdinalIgnoreCase).Any();

    [RelayCommand]
    private void AddOnSuccess(string? taskPath) => Add(OnSuccess, taskPath);

    [RelayCommand]
    private void AddOnFailure(string? taskPath) => Add(OnFailure, taskPath);

    [RelayCommand]
    private void RemoveOnSuccess(string taskPath) => OnSuccess.Remove(taskPath);

    [RelayCommand]
    private void RemoveOnFailure(string taskPath) => OnFailure.Remove(taskPath);

    public CompletionActions Build() => new()
    {
        Enabled = Enabled,
        OnSuccess = OnSuccess.ToValueList(),
        OnFailure = OnFailure.ToValueList()
    };

    private static void Add(ObservableCollection<string> list, string? taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath)) return;
        taskPath = taskPath.Trim();
        if (!list.Contains(taskPath, StringComparer.OrdinalIgnoreCase)) list.Add(taskPath);
    }
}
