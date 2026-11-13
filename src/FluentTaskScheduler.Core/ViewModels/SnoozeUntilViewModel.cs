using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The snooze dialog: a snooze that lasts until a moment the user picks as a date and a time of
/// day. For a snooze of all tasks it also offers the options of that snooze.
/// </summary>
public sealed partial class SnoozeUntilViewModel : ObservableObject
{
    private readonly TimeProvider _time;
    private readonly Func<SnoozeEnd, Task<string?>> _snooze;

    /// <param name="untilText">What the date and the time stand for, e.g. "Disable this task until:".</param>
    /// <param name="allTasks">The snooze of all tasks, for its options; null for a snooze of one task.</param>
    /// <param name="snooze">Starts the snooze, and answers why it did not start, or null when it did.</param>
    internal SnoozeUntilViewModel(string title, string untilText, SnoozeViewModel? allTasks, TimeProvider time, Func<SnoozeEnd, Task<string?>> snooze)
    {
        Title = title;
        UntilText = untilText;
        AllTasks = allTasks;
        _time = time;
        _snooze = snooze;

        // An hour from now, to the minute, is where the pickers start.
        DateTime start = time.GetLocalNow().DateTime.AddHours(1);
        _date = new DateTimeOffset(start.Date);
        _timeOfDay = new TimeSpan(start.Hour, start.Minute, 0);
    }

    public string Title { get; }
    public string UntilText { get; }
    public SnoozeViewModel? AllTasks { get; }
    public bool IsForAllTasks => AllTasks is not null;

    [ObservableProperty] private DateTimeOffset _date;
    [ObservableProperty] private TimeSpan _timeOfDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public bool HasProblem => Problem is not null;

    /// <summary>Snoozes until the moment picked, a moment in the time of this computer.</summary>
    /// <returns>True when the snooze has started; otherwise <see cref="Problem"/> says why not.</returns>
    public async Task<bool> SnoozeAsync()
    {
        DateTime local = Date.Date + TimeOfDay;
        var moment = new DateTimeOffset(local, _time.LocalTimeZone.GetUtcOffset(local));
        if (moment <= _time.GetUtcNow())
        {
            Problem = Loc.Get("Snooze.Error.PastTime");
            return false;
        }

        Problem = await _snooze(SnoozeEnd.Until(moment));
        return Problem is null;
    }
}
