using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Controls;

/// <summary>The input fields for one trigger of a task.</summary>
public sealed partial class TriggerEditor : UserControl
{
    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger), typeof(TriggerViewModel), typeof(TriggerEditor), new PropertyMetadata(null));

    public TriggerEditor()
    {
        InitializeComponent();

        // Filled here rather than in XAML so that the order is the one the view model defines.
        TriggerTypeBox.ItemsSource = TriggerViewModel.Types.Select(type => Loc.Get(TypeKey(type))).ToList();
        TriggerSessionChange.ItemsSource = TriggerViewModel.SessionChanges.Select(change => Loc.Get(TriggerText.SessionChangeKey(change))).ToList();
    }

    /// <summary>The trigger being edited.</summary>
    public TriggerViewModel? Trigger
    {
        get => (TriggerViewModel?)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public IReadOnlyList<FlagOption> Weekdays { get; } =
    [
        Day("Dialog.Day.Mon", DaysOfTheWeek.Monday), Day("Dialog.Day.Tue", DaysOfTheWeek.Tuesday), Day("Dialog.Day.Wed", DaysOfTheWeek.Wednesday),
        Day("Dialog.Day.Thu", DaysOfTheWeek.Thursday), Day("Dialog.Day.Fri", DaysOfTheWeek.Friday), Day("Dialog.Day.Sat", DaysOfTheWeek.Saturday),
        Day("Dialog.Day.Sun", DaysOfTheWeek.Sunday)
    ];

    public IReadOnlyList<FlagOption> WeeksOfMonth { get; } =
    [
        new(Loc.Get("Dialog.Week.First"), (int)WhichWeek.FirstWeek), new(Loc.Get("Dialog.Week.Second"), (int)WhichWeek.SecondWeek),
        new(Loc.Get("Dialog.Week.Third"), (int)WhichWeek.ThirdWeek), new(Loc.Get("Dialog.Week.Fourth"), (int)WhichWeek.FourthWeek),
        new(Loc.Get("Dialog.Week.Last"), (int)WhichWeek.LastWeek)
    ];

    public IReadOnlyList<FlagOption> Months { get; } =
        new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }
            .Select((month, index) => new FlagOption(Loc.Get("Dialog.Month." + month), 1 << index))
            .ToList();

    public IReadOnlyList<FlagOption> DaysOfMonth { get; } =
        Enumerable.Range(1, 31).Select(day => new FlagOption(day.ToString(), 1 << (day - 1))).ToList();

    /// <summary>For showing something exactly when a condition does not hold.</summary>
    public static Visibility CollapsedIf(bool condition) => condition ? Visibility.Collapsed : Visibility.Visible;

    private static FlagOption Day(string key, DaysOfTheWeek day) => new(Loc.Get(key), (int)day);

    private static string TypeKey(TriggerType type) => type switch
    {
        TriggerType.Once => "Dialog.Trigger.Once",
        TriggerType.Daily => "Dialog.Trigger.Daily",
        TriggerType.Weekly => "Dialog.Trigger.Weekly",
        TriggerType.Monthly => "Dialog.Trigger.Monthly",
        TriggerType.AtLogon => "Trigger.AtLogon",
        TriggerType.AtStartup => "Trigger.AtStartup",
        TriggerType.OnIdle => "Trigger.OnIdle",
        TriggerType.Event => "Dialog.Trigger.Event",
        TriggerType.SessionStateChange => "Dialog.Trigger.Session",
        _ => "Dialog.Trigger.__Unsupported"
    };
}
