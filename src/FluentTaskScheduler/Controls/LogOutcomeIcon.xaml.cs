using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>Shows how an entry of the dashboard's log turned out, as an icon in the colour of its kind.</summary>
public sealed partial class LogOutcomeIcon : UserControl
{
    public static readonly DependencyProperty OutcomeProperty = DependencyProperty.Register(
        nameof(Outcome), typeof(LogOutcome), typeof(LogOutcomeIcon), new PropertyMetadata(LogOutcome.Succeeded, (d, _) => ((LogOutcomeIcon)d).Show()));

    public LogOutcomeIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => Show();
    }

    public LogOutcome Outcome
    {
        get => (LogOutcome)GetValue(OutcomeProperty);
        set => SetValue(OutcomeProperty, value);
    }

    private void Show() => VisualStateManager.GoToState(this, Outcome.ToString(), useTransitions: false);
}
