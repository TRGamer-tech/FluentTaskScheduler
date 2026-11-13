using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Snoozing;

/// <summary>A ready-made end for a snooze, as the menus offer it next to a moment of the user's choosing.</summary>
public sealed record SnoozeChoice(SnoozeEnd End, string LabelKey)
{
    public static SnoozeChoice HalfAnHour { get; } = new(SnoozeEnd.After(TimeSpan.FromMinutes(30)), "Snooze.Duration.30m");
    public static SnoozeChoice OneHour { get; } = new(SnoozeEnd.After(TimeSpan.FromHours(1)), "Snooze.Duration.1h");
    public static SnoozeChoice ThreeHours { get; } = new(SnoozeEnd.After(TimeSpan.FromHours(3)), "Snooze.Duration.3h");
    public static SnoozeChoice UntilRestart { get; } = new(SnoozeEnd.AtRestart, "Snooze.Duration.Reboot");

    /// <summary>All of them, in the order the menus list them.</summary>
    public static ValueList<SnoozeChoice> All { get; } = [HalfAnHour, OneHour, ThreeHours, UntilRestart];

    public string Label => Loc.Get(LabelKey);
}
