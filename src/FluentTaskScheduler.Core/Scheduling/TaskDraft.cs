using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Which kind of account a task runs as.</summary>
public enum TaskAccountKind
{
    /// <summary>The user running this app.</summary>
    CurrentUser,
    /// <summary>Another user account, named in <see cref="TaskAccount.Name"/>.</summary>
    User,
    /// <summary>A group; the task runs for whichever member is logged on.</summary>
    Group,
    System,
    LocalService,
    NetworkService
}

/// <summary>The account a task runs as.</summary>
public sealed record TaskAccount(TaskAccountKind Kind, string Name = "")
{
    public static TaskAccount CurrentUser { get; } = new(TaskAccountKind.CurrentUser);
    public static TaskAccount System { get; } = new(TaskAccountKind.System);
}

/// <summary>One "start a program" action of a task.</summary>
public sealed record ActionDraft
{
    public string Path { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Position of the action in the task this draft was read from; null for an action added in the editor.</summary>
    public int? SourceIndex { get; init; }

    /// <summary>
    /// Set for action types the editor cannot change (COM handler, e-mail, message box). Such an
    /// action is shown with this text and written back exactly as it was.
    /// </summary>
    public string? UnsupportedSummary { get; init; }

    public bool IsEditable => UnsupportedSummary is null;
}

/// <summary>
/// One trigger of a task. Which properties matter depends on <see cref="Type"/>; the others keep
/// their defaults and are ignored.
/// </summary>
public sealed record TriggerDraft
{
    public TriggerType Type { get; init; } = TriggerType.Daily;

    /// <summary>Position of the trigger in the task this draft was read from; null for a trigger added in the editor.</summary>
    public int? SourceIndex { get; init; }

    /// <summary>Set when <see cref="Type"/> is <see cref="TriggerType.Unsupported"/>: what the trigger is, for display.</summary>
    public string? UnsupportedSummary { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When the trigger becomes active; for calendar triggers also the time of day it fires. On the
    /// local clock: of kind <see cref="DateTimeKind.Local"/> when Task Scheduler keeps the moment
    /// with its time zone ("synchronize across time zones"), unspecified when it does not.
    /// </summary>
    public DateTime? Start { get; init; }

    /// <summary>When the trigger stops firing, on the local clock like <see cref="Start"/>; null if it never expires.</summary>
    public DateTime? End { get; init; }

    public short DaysInterval { get; init; } = 1;
    public short WeeksInterval { get; init; } = 1;

    /// <summary>Weekdays of a weekly trigger, or of a monthly trigger that runs by weekday.</summary>
    public DaysOfTheWeek DaysOfWeek { get; init; } = DaysOfTheWeek.Monday;

    public MonthsOfTheYear Months { get; init; } = MonthsOfTheYear.AllMonths;

    /// <summary>Monthly trigger: true to run on the nth weekday, false to run on days of the month.</summary>
    public bool MonthlyByWeekday { get; init; }

    /// <summary>Monthly trigger by day: bit 0 is the 1st of the month, bit 30 the 31st.</summary>
    public uint DaysOfMonth { get; init; } = 1;

    public bool LastDayOfMonth { get; init; }

    /// <summary>Monthly trigger by weekday: which weeks of the month.</summary>
    public WhichWeek Weeks { get; init; } = WhichWeek.FirstWeek;

    /// <summary>Upper bound of the random wait added before the task starts; zero for none.</summary>
    public TimeSpan RandomDelay { get; init; }

    /// <summary>How often the task is started again after the trigger fired; zero for no repetition.</summary>
    public TimeSpan RepetitionInterval { get; init; }

    /// <summary>How long the repetition goes on; zero for indefinitely.</summary>
    public TimeSpan RepetitionDuration { get; init; }

    /// <summary>Event trigger: the event log subscription as query XML.</summary>
    public string EventSubscription { get; init; } = "";

    public TaskSessionStateChangeType SessionChange { get; init; } = TaskSessionStateChangeType.SessionLock;

    /// <summary>True for trigger types that fire at a time of day and therefore need <see cref="Start"/>.</summary>
    public bool IsCalendarBased => Type is TriggerType.Daily or TriggerType.Weekly or TriggerType.Monthly or TriggerType.Once;
}

/// <summary>
/// Everything the task editor can show and change about a task, as an immutable snapshot.
/// A draft is read from a task (<see cref="TaskDraftReader"/>), edited through copies, and written
/// back by comparing it with the snapshot it started from (<see cref="TaskDraftWriter"/>), so
/// anything the user did not touch stays exactly as Task Scheduler had it.
/// The defaults are those of a task newly created in this app.
/// </summary>
public sealed record TaskDraft
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string Category { get; init; } = "";
    public ValueList<string> Tags { get; init; } = [];
    public CompletionActions CompletionActions { get; init; } = CompletionActions.None;
    public bool Enabled { get; init; } = true;
    public bool Hidden { get; init; }

    public TaskAccount Account { get; init; } = TaskAccount.CurrentUser;
    public bool HighestPrivileges { get; init; }

    /// <summary>
    /// True when Windows stores a password for the task's account. Such a task can only be
    /// saved again together with that password.
    /// </summary>
    public bool StoresPassword { get; init; }

    public bool RunOnlyIfIdle { get; init; }
    public TimeSpan IdleDuration { get; init; } = TimeSpan.FromMinutes(10);
    public bool StopOnIdleEnd { get; init; }
    public bool RunOnlyOnAcPower { get; init; }
    public bool StopOnBattery { get; init; }
    public bool RunOnlyIfNetwork { get; init; }
    public Guid NetworkId { get; init; }
    public string NetworkName { get; init; } = "";
    public bool WakeToRun { get; init; }

    /// <summary>The task is stopped after running this long; zero for no limit.</summary>
    public TimeSpan ExecutionTimeLimit { get; init; }

    public bool RunIfMissed { get; init; }
    public TaskInstancesPolicy MultipleInstances { get; init; } = TaskInstancesPolicy.IgnoreNew;
    public ProcessPriorityClass Priority { get; init; } = ProcessPriorityClass.BelowNormal;
    public bool DeleteWhenExpired { get; init; }
    public bool AllowHardTerminate { get; init; } = true;

    /// <summary>How often a failed task is started again; zero for never.</summary>
    public int RestartCount { get; init; }
    public TimeSpan RestartInterval { get; init; } = TimeSpan.FromMinutes(1);

    public ValueList<TriggerDraft> Triggers { get; init; } = [];
    public ValueList<ActionDraft> Actions { get; init; } = [];
}
