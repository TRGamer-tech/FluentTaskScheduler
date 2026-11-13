using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using Serilog;

namespace FluentTaskScheduler.Reminders;

/// <summary>
/// Announces a task shortly before it runs.
/// <para>
/// Only tasks a user would call their own are announced: enabled, not hidden, and not among the
/// several hundred Windows keeps for itself. Nothing is announced while all tasks are snoozed,
/// and nothing for a run that follows the task's last run within the lead time: what repeats
/// every few minutes is not news.
/// </para>
/// <para>
/// Listing the tasks is slow, so it is done as rarely as the job allows: at the moment the next
/// announcement is due, and otherwise every few minutes, to notice tasks that were added or
/// changed. It always happens on a timer thread.
/// </para>
/// </summary>
public sealed class ReminderService : IDisposable
{
    /// <summary>How long a new or changed task can go unnoticed.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>While the app starts, the window is loading the task list; the first look waits until that is out of the way.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    private readonly ITaskStore _store;
    private readonly ISettingsStore _settings;
    private readonly ISnoozeService _snooze;
    private readonly INotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly object _looking = new();

    /// <summary>The runs that have been announced and are still ahead, by task path.</summary>
    private Dictionary<string, DateTimeOffset> _announced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether, at the last change of the snooze state, nothing was snoozed for all tasks and no task was being switched.</summary>
    private volatile bool _snoozeAtRest;

    public ReminderService(ITaskStore store, ISettingsStore settings, ISnoozeService snooze, INotifier notifier, TimeProvider? time = null)
    {
        _store = store;
        _settings = settings;
        _snooze = snooze;
        _notifier = notifier;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => Look(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        _snoozeAtRest = IsAtRest(_snooze.State);
        _settings.Changed += OnSettingsChanged;
        _snooze.Changed += OnSnoozeChanged;
        _timer.Change(StartDelay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _snooze.Changed -= OnSnoozeChanged;
        _timer.Dispose();
    }

    /// <summary>
    /// The notifier would drop the announcements anyway while notifications are off. Asking here
    /// as well saves listing the tasks for nothing.
    /// </summary>
    private static bool Wanted(AppSettings settings) => settings.ShowNotifications && settings.EnableUpcomingReminders;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (Wanted(e.Previous) != Wanted(e.Current) || e.Previous.ReminderLeadMinutes != e.Current.ReminderLeadMinutes) LookNow();
    }

    /// <summary>
    /// When a snooze is over and its tasks are enabled again, their next runs count from that
    /// moment, not from the next regular look.
    /// </summary>
    private void OnSnoozeChanged(object? sender, EventArgs e)
    {
        bool atRest = IsAtRest(_snooze.State);
        if (atRest == _snoozeAtRest) return;

        _snoozeAtRest = atRest;
        if (atRest) LookNow();
    }

    private static bool IsAtRest(SnoozeState state) => state is { Global: null, IsBusy: false };

    /// <summary>Hands the look to the timer thread, so that whoever reported the change does not wait for it.</summary>
    private void LookNow() => _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

    private void Look()
    {
        lock (_looking)
        {
            try
            {
                // Without a next look the timer stays off; a change of the settings or the end of
                // the snooze starts it again.
                if (Wanted(_settings.Current) && _snooze.State.Global is null)
                    _timer.Change(AnnounceWhatIsDue(), Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex)
            {
                // An exception that leaves a timer callback ends the process.
                Log.Error(ex, "Looking for tasks that run soon failed");
                _timer.Change(RefreshInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <returns>How long to wait until the next look.</returns>
    private TimeSpan AnnounceWhatIsDue()
    {
        // Task Scheduler gives run times in the time zone Windows uses now, but .NET keeps the zone
        // it read first until its cache is cleared. On a laptop taken to another zone, reminders
        // would otherwise come hours early or late.
        TimeZoneInfo.ClearCachedData();

        TimeSpan lead = TimeSpan.FromMinutes(_settings.Current.ReminderLeadMinutes);
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset nextLook = now + RefreshInterval;
        var announced = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        foreach (TaskRow task in _store.GetTasks())
        {
            // A task that is snoozed on its own is disabled, so the first test covers it.
            if (!task.IsEnabled || task.IsHidden || WindowsTasks.Contains(task.Path) || task.NextRunTime is not { } nextRun) continue;

            DateTimeOffset run = ToInstant(nextRun);
            if (run <= now) continue;
            if (task.LastRunTime is { } lastRun && run - ToInstant(lastRun) <= lead) continue;

            if (run - now > lead)
            {
                if (run - lead < nextLook) nextLook = run - lead;
                continue;
            }

            if (!_announced.TryGetValue(task.Path, out DateTimeOffset announcedRun) || announcedRun != run)
                _notifier.TaskRunsSoon(task.Name, (int)Math.Ceiling((run - now).TotalMinutes));
            announced[task.Path] = run;
        }

        _announced = announced;
        return nextLook - now;
    }

    /// <summary>Task Scheduler reports run times as local time of day without an offset.</summary>
    private DateTimeOffset ToInstant(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _time.LocalTimeZone.GetUtcOffset(local));
    }
}
