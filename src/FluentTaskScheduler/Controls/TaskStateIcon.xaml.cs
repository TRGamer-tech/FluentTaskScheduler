using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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

    private void Show()
    {
        TaskRowState shown = IsRunning ? TaskRowState.Running : State;
        VisualStateManager.GoToState(this, shown.ToString(), useTransitions: false);
        // Glyph and colour are all that tell the states apart on screen; screen readers get the word.
        AutomationProperties.SetName(this, Loc.Get(NameKey(shown)));
    }

    private static string NameKey(TaskRowState state) => state switch
    {
        TaskRowState.Ready => "Main.Status.Ready",
        TaskRowState.Running => "Main.Status.Running",
        TaskRowState.Queued => "Main.Status.Queued",
        TaskRowState.Disabled => "Main.Status.Disabled",
        _ => "Main.Status.Unknown"
    };

    // A user control has no automation peer of its own, so without one its name would reach no one.
    protected override AutomationPeer OnCreateAutomationPeer() => new StateIconAutomationPeer(this);

    private sealed class StateIconAutomationPeer(TaskStateIcon owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(TaskStateIcon);
    }
}
