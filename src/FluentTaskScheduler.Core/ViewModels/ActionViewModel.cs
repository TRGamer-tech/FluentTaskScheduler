using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.ViewModels;

/// <summary>One action in the task editor.</summary>
public sealed partial class ActionViewModel(ActionDraft source) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _path = source.Path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _arguments = source.Arguments;

    [ObservableProperty] private string _workingDirectory = source.WorkingDirectory;

    /// <summary>False for an action type the editor only shows (it is saved back unchanged).</summary>
    public bool IsEditable => source.IsEditable;

    /// <summary>The action in one line, for the list of actions.</summary>
    public string Summary => source.UnsupportedSummary ?? $"{Path} {Arguments}".Trim();

    public ActionDraft Build() => IsEditable
        ? source with { Path = Path, Arguments = Arguments, WorkingDirectory = WorkingDirectory }
        : source;
}
