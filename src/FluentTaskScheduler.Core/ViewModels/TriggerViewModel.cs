using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// One trigger in the task editor. The controls bind to its properties; <see cref="TryBuild"/>
/// turns them back into a <see cref="TriggerDraft"/>. A trigger the user did not touch builds
/// into exactly the draft it was created from, which is what lets the task be saved without
/// rewriting that trigger.
/// </summary>
public sealed partial class TriggerViewModel : ObservableObject
{
    /// <summary>The trigger types the editor offers, in the order of its type list.</summary>
    public static ValueList<TriggerType> Types { get; } =
    [
        TriggerType.Once, TriggerType.Daily, TriggerType.Weekly, TriggerType.Monthly, TriggerType.AtLogon,
        TriggerType.AtStartup, TriggerType.OnIdle, TriggerType.Event, TriggerType.SessionStateChange
    ];

    /// <summary>The session changes the editor offers, in the order of its list.</summary>
    public static ValueList<TaskSessionStateChangeType> SessionChanges { get; } =
    [
        TaskSessionStateChangeType.SessionLock, TaskSessionStateChangeType.SessionUnlock,
        TaskSessionStateChangeType.ConsoleConnect, TaskSessionStateChangeType.ConsoleDisconnect,
        TaskSessionStateChangeType.RemoteConnect, TaskSessionStateChangeType.RemoteDisconnect
    ];

    private readonly TriggerDraft _source;
    private readonly string _sourceRepetitionInterval, _sourceRepetitionDuration, _sourceRandomDelay;
    private readonly string _sourceEventLog = "", _sourceEventSource = "", _sourceEventId = "";

    /// <param name="now">
    /// The time of the editor's clock. A trigger without a start of its own, such as one at logon,
    /// offers the start a new trigger would have, should it get a time of day.
    /// </param>
    public TriggerViewModel(TriggerDraft source, DateTime now)
    {
        _source = source;
        int type = IndexOf(Types, source.Type);
        IsEditable = type >= 0;
        _typeIndex = Math.Max(type, 0);
        _enabled = source.Enabled;

        DateTime start = source.Start ?? NewTask.Trigger(now).Start!.Value;
        _startDate = new DateTimeOffset(start.Date);
        _startTime = start.TimeOfDay;
        _expires = source.End is not null;
        DateTime end = source.End ?? start.Date.AddYears(1);
        _endDate = new DateTimeOffset(end.Date);
        _endTime = end.TimeOfDay;

        _daysInterval = source.DaysInterval;
        _weeksInterval = source.WeeksInterval;
        _weekdays = (int)source.DaysOfWeek;
        _months = (int)source.Months;
        _monthlyByWeekday = source.MonthlyByWeekday;
        _daysOfMonth = (int)source.DaysOfMonth;
        _lastDayOfMonth = source.LastDayOfMonth;
        _weeks = (int)source.Weeks;

        _repetitionInterval = _sourceRepetitionInterval = DurationText.Format(source.RepetitionInterval);
        _repetitionDuration = _sourceRepetitionDuration = DurationText.Format(source.RepetitionDuration);
        _randomDelay = _sourceRandomDelay = DurationText.Format(source.RandomDelay);

        // A subscription that is more than "this log, this source, this event id" is edited as XML.
        if (EventQuery.TryParse(source.EventSubscription, out string log, out string eventSource, out int? eventId))
        {
            _eventLog = _sourceEventLog = log;
            _eventSource = _sourceEventSource = eventSource;
            _eventId = _sourceEventId = eventId?.ToString(CultureInfo.InvariantCulture) ?? "";
        }
        else
        {
            _customEventQuery = source.EventSubscription.Length > 0;
        }
        _eventQueryXml = source.EventSubscription;
        _sessionChangeIndex = Math.Max(IndexOf(SessionChanges, source.SessionChange), 0);
    }

    /// <summary>False for a trigger type the editor only shows; it is saved back unchanged.</summary>
    public bool IsEditable { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Type), nameof(HasSchedule), nameof(IsDaily), nameof(IsWeekly), nameof(IsMonthly), nameof(IsMonthlyByDay), nameof(IsMonthlyByWeekday),
        nameof(IsEvent), nameof(IsSessionChange))]
    private int _typeIndex;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private DateTimeOffset _startDate;
    [ObservableProperty] private TimeSpan _startTime;
    [ObservableProperty] private bool _expires;
    [ObservableProperty] private DateTimeOffset _endDate;
    [ObservableProperty] private TimeSpan _endTime;

    // Number boxes work with doubles and report "not a number" while they are empty.
    [ObservableProperty] private double _daysInterval;
    [ObservableProperty] private double _weeksInterval;

    /// <summary>Selected weekdays as <see cref="DaysOfTheWeek"/> flags.</summary>
    [ObservableProperty] private int _weekdays;

    /// <summary>Selected months as <see cref="MonthsOfTheYear"/> flags.</summary>
    [ObservableProperty] private int _months;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthlyByDay), nameof(IsMonthlyByWeekday), nameof(MonthlyModeIndex))]
    private bool _monthlyByWeekday;

    /// <summary><see cref="MonthlyByWeekday"/> as the position in a two-way choice: 0 for days of the month, 1 for weekdays.</summary>
    public int MonthlyModeIndex
    {
        get => MonthlyByWeekday ? 1 : 0;
        set
        {
            if (value is 0 or 1) MonthlyByWeekday = value == 1;
        }
    }

    /// <summary>Selected days of the month; bit 0 is the 1st.</summary>
    [ObservableProperty] private int _daysOfMonth;

    [ObservableProperty] private bool _lastDayOfMonth;

    /// <summary>Selected weeks of the month as <see cref="WhichWeek"/> flags.</summary>
    [ObservableProperty] private int _weeks;

    /// <summary>Durations as the user types them ("15m", "1h 30m"); empty for none.</summary>
    [ObservableProperty] private string _repetitionInterval;
    [ObservableProperty] private string _repetitionDuration;
    [ObservableProperty] private string _randomDelay;

    [ObservableProperty] private bool _customEventQuery;
    [ObservableProperty] private string _eventLog = "";
    [ObservableProperty] private string _eventSource = "";
    [ObservableProperty] private string _eventId = "";

    /// <summary>The event subscription as query XML, for subscriptions the three fields cannot express.</summary>
    [ObservableProperty] private string _eventQueryXml;

    [ObservableProperty] private int _sessionChangeIndex;

    public TriggerType Type => IsEditable ? Types[TypeIndex] : _source.Type;

    // A list control reports -1 while it has nothing selected. That is not a choice.
    partial void OnTypeIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= Types.Count) TypeIndex = oldValue;
    }

    partial void OnSessionChangeIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= SessionChanges.Count) SessionChangeIndex = oldValue;
    }

    /// <summary>True for types that fire at a time of day.</summary>
    public bool HasSchedule => Type is TriggerType.Once or TriggerType.Daily or TriggerType.Weekly or TriggerType.Monthly;
    public bool IsDaily => Type == TriggerType.Daily;
    public bool IsWeekly => Type == TriggerType.Weekly;
    public bool IsMonthly => Type == TriggerType.Monthly;
    public bool IsMonthlyByDay => IsMonthly && !MonthlyByWeekday;
    public bool IsMonthlyByWeekday => IsMonthly && MonthlyByWeekday;
    public bool IsEvent => Type == TriggerType.Event;
    public bool IsSessionChange => Type == TriggerType.SessionStateChange;

    /// <summary>The trigger in a few words, for the list of triggers. Falls back to the stored trigger while the input is incomplete.</summary>
    public string Summary => TriggerText.Describe(TryBuild(out TriggerDraft built, out _) ? built : _source);

    // Everything except these is input, and any input can change the summary.
    private static readonly HashSet<string> Derived =
    [
        nameof(Summary), nameof(Type), nameof(HasSchedule), nameof(IsDaily), nameof(IsWeekly), nameof(IsMonthly),
        nameof(IsMonthlyByDay), nameof(IsMonthlyByWeekday), nameof(IsEvent), nameof(IsSessionChange), nameof(MonthlyModeIndex)
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && !Derived.Contains(name)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    /// <summary>Builds the trigger from what was entered.</summary>
    /// <param name="problem">Resource name of the message for the first value that could not be understood.</param>
    public bool TryBuild(out TriggerDraft draft, out string? problem)
    {
        draft = _source;
        problem = null;
        if (!IsEditable) return true;

        if (!DurationInput.TryRead(RepetitionInterval, _sourceRepetitionInterval, _source.RepetitionInterval, out TimeSpan interval)
            || !DurationInput.TryRead(RepetitionDuration, _sourceRepetitionDuration, _source.RepetitionDuration, out TimeSpan duration))
        {
            problem = "Editor.Error.RepetitionText";
            return false;
        }
        if (!DurationInput.TryRead(RandomDelay, _sourceRandomDelay, _source.RandomDelay, out TimeSpan delay))
        {
            problem = "Editor.Error.RandomDelayText";
            return false;
        }

        string subscription = _source.EventSubscription;
        if (Type == TriggerType.Event)
        {
            if (CustomEventQuery)
            {
                subscription = EventQueryXml;
            }
            else if (EventFieldsChanged || _source.Type != TriggerType.Event)
            {
                if (!TryReadEventFields(out string log, out string source, out int? id, out problem)) return false;
                subscription = log.Length == 0 ? "" : EventQuery.Build(log, source, id);
            }
        }

        // Without a time of day the start is only when the trigger becomes active. A time of day
        // left over from the type the trigger had before would keep it idle until then.
        DateTime? start = HasSchedule ? Moment(StartDate, StartTime, _source.Start) : _source.IsCalendarBased ? null : _source.Start;

        draft = _source with
        {
            Type = Type,
            Enabled = Enabled,
            Start = start,
            End = Expires ? Moment(EndDate, EndTime, _source.End) : null,
            DaysInterval = Count(DaysInterval, _source.DaysInterval),
            WeeksInterval = Count(WeeksInterval, _source.WeeksInterval),
            DaysOfWeek = (DaysOfTheWeek)Weekdays,
            Months = (MonthsOfTheYear)Months,
            MonthlyByWeekday = MonthlyByWeekday,
            DaysOfMonth = (uint)DaysOfMonth,
            LastDayOfMonth = LastDayOfMonth,
            Weeks = (WhichWeek)Weeks,
            RepetitionInterval = interval,
            RepetitionDuration = duration,
            RandomDelay = delay,
            EventSubscription = subscription,
            SessionChange = SessionChanges[SessionChangeIndex]
        };
        return true;
    }

    // Fields left as they were stand for the stored subscription, which is kept as Windows wrote it.
    private bool EventFieldsChanged => EventLog != _sourceEventLog || EventSource != _sourceEventSource || EventId != _sourceEventId;

    // The query starts out as what the fields say, so nothing typed there is lost, and an empty
    // box starts out as a query that works (the log defaults to Application).
    partial void OnCustomEventQueryChanged(bool value)
    {
        if (value && (EventFieldsChanged || EventQueryXml.Length == 0) && TryReadEventFields(out string log, out string source, out int? id, out _))
            EventQueryXml = EventQuery.Build(log, source, id);
    }

    /// <summary>The log, source and event ID fields as an event query takes them.</summary>
    /// <param name="problem">Resource name of the message for a field that cannot go into a query.</param>
    private bool TryReadEventFields(out string log, out string source, out int? id, out string? problem)
    {
        log = EventLog.Trim();
        source = EventSource.Trim();
        id = null;
        problem = null;

        // A name pasted from a table can bring a tab along, which no query can hold.
        if (log.Any(char.IsControl) || source.Any(char.IsControl))
        {
            problem = "Editor.Error.EventLogText";
            return false;
        }
        if (EventId.Trim() is { Length: > 0 } text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                problem = "Editor.Error.EventIdText";
                return false;
            }
            id = parsed;
        }
        return true;
    }

    // The pickers know dates and minutes. If what they show is still the stored moment, keep that
    // one, seconds and all. A moment that was kept with its time zone keeps it when it is changed.
    private static DateTime Moment(DateTimeOffset date, TimeSpan time, DateTime? source)
    {
        var picked = date.Date + new TimeSpan(time.Hours, time.Minutes, 0);
        if (source is not { } stored) return picked;
        return stored.Date + new TimeSpan(stored.Hour, stored.Minute, 0) == picked ? stored : DateTime.SpecifyKind(picked, stored.Kind);
    }

    private static short Count(double value, short source) =>
        double.IsNaN(value) ? source : (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);

    private static int IndexOf<T>(ValueList<T> items, T value)
    {
        for (int i = 0; i < items.Count; i++)
            if (EqualityComparer<T>.Default.Equals(items[i], value)) return i;
        return -1;
    }
}
