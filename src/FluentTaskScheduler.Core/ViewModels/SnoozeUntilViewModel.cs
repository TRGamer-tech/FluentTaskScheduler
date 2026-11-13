using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The snooze dialog: a snooze that lasts until a moment the user picks as a date and a time of
/// day. For a snooze of all tasks it also offers the options of that snooze.
/// </summary>
public sealed partial class SnoozeUntilViewModel : ObservableObject
{
    private readonly ISettingsStore? _settings;
    private readonly TimeProvider _time;
    private readonly Func<SnoozeEnd, Task<string?>> _snooze;

    /// <param name="untilText">What the date and the time stand for, e.g. "Disable this task until:".</param>
    /// <param name="settings">Where the options of a snooze of all tasks are kept; null for a snooze of one task, which has none.</param>
    /// <param name="snooze">Starts the snooze, and answers why it did not start, or null when it did.</param>
    internal SnoozeUntilViewModel(string title, string untilText, ISettingsStore? settings, TimeProvider time, Func<SnoozeEnd, Task<string?>> snooze)
    {
        Title = title;
        UntilText = untilText;
        _settings = settings;
        _time = time;
        _snooze = snooze;

        // An hour from now, to the minute, is where the pickers start.
        DateTime start = time.GetLocalNow().DateTime.AddHours(1);
        _date = new DateTimeOffset(start.Date);
        _timeOfDay = new TimeSpan(start.Hour, start.Minute, 0);

        if (settings is not null)
        {
            _suspendsTriggers = settings.Current.SnoozeSuspendsScheduledTasks;
            _includesMicrosoftTasks = settings.Current.SnoozeIncludeMicrosoftTasks;
        }
    }

    public string Title { get; }
    public string UntilText { get; }
    public bool IsForAllTasks => _settings is not null;

    [ObservableProperty] private DateTimeOffset _date;
    [ObservableProperty] private TimeSpan _timeOfDay;

    /// <summary>
    /// The snooze of all tasks also disables them in Task Scheduler, so that Windows does not start
    /// them either. Switching this off also switches off <see cref="IncludesMicrosoftTasks"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMicrosoftTasksWarning))]
    private bool _suspendsTriggers;

    /// <summary>Suspending triggers reaches the tasks Windows has under \Microsoft\ as well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMicrosoftTasksWarning))]
    private bool _includesMicrosoftTasks;

    /// <summary>Disabling the tasks of Windows can keep it from scanning for malware and updating itself until the snooze ends.</summary>
    public bool ShowsMicrosoftTasksWarning => SuspendsTriggers && IncludesMicrosoftTasks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string? _problem;

    public bool HasProblem => Problem is not null;

    /// <summary>
    /// True from the moment the snooze is asked for until it has started or failed to. Suspending
    /// the triggers of a few hundred tasks takes a while.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSnooze))]
    private bool _isSnoozing;

    /// <summary>False while a snooze is being started: asking again would only queue a second one behind it.</summary>
    public bool CanSnooze => !IsSnoozing;

    // The tasks of Windows are only ever included because that was chosen while the warning was in view.
    partial void OnSuspendsTriggersChanged(bool value)
    {
        if (!value) IncludesMicrosoftTasks = false;
    }

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

        // The options become the settings only now, so that a dialog that is cancelled leaves them
        // as they were. The snooze reads them when it starts.
        _settings?.Update(s => s with
        {
            SnoozeSuspendsScheduledTasks = SuspendsTriggers,
            SnoozeIncludeMicrosoftTasks = SuspendsTriggers && IncludesMicrosoftTasks
        });

        Problem = null;
        IsSnoozing = true;
        try
        {
            Problem = await _snooze(SnoozeEnd.Until(moment));
        }
        finally
        {
            IsSnoozing = false;
        }
        return Problem is null;
    }
}
