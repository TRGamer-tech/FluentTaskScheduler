using FluentTaskScheduler.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>Shows the state of a task as an icon, and as a progress ring while it runs.</summary>
public sealed partial class TaskStateIcon : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(TaskRowState), typeof(TaskStateIcon), new PropertyMetadata(TaskRowState.Unknown, (d, _) => ((TaskStateIcon)d).Show()));

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(TaskStateIcon), new PropertyMetadata(false, (d, _) => ((TaskStateIcon)d).Show()));

    public TaskStateIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => Show();
    }

    public TaskRowState State
    {
        get => (TaskRowState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>True from the moment the task was started here, before Task Scheduler reports it as running.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private void Show() =>
        VisualStateManager.GoToState(this, IsRunning ? nameof(TaskRowState.Running) : State.ToString(), useTransitions: false);
}
