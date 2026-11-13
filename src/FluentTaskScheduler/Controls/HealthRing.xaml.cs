using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>The health score as a ring filled up to it, in the colour of how good it is.</summary>
public sealed partial class HealthRing : UserControl
{
    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(double), typeof(HealthRing), new PropertyMetadata(0.0));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(HealthLevel), typeof(HealthRing), new PropertyMetadata(HealthLevel.None, (d, _) => ((HealthRing)d).Show()));

    public HealthRing()
    {
        InitializeComponent();
        Loaded += (_, _) => Show();
    }

    /// <summary>From 0 to 100.</summary>
    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public HealthLevel Level
    {
        get => (HealthLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private void Show() => VisualStateManager.GoToState(this, Level.ToString(), useTransitions: false);
}
