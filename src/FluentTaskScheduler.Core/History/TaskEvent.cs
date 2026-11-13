using System.Diagnostics.Tracing;
using System.Globalization;

namespace FluentTaskScheduler.History;

/// <summary>What an event in Task Scheduler's log says happened, as far as the app draws conclusions from it.</summary>
public enum TaskEventKind
{
    /// <summary>Shown in a task's history, but nothing follows from it: triggers, refused or queued start requests and the like.</summary>
    Other,
    Registered,
    /// <summary>The task's definition was changed, or the task was enabled or disabled.</summary>
    Updated,
    Deleted,
    /// <summary>An instance of the task began to run.</summary>
    RunStarted,
    /// <summary>The task could not be started: its account could not be logged on, or its first action could not be launched.</summary>
    StartFailed,
    ActionStarted,
    /// <summary>An action ran to its end. <see cref="TaskEvent.ResultCode"/> is its exit code.</summary>
    ActionCompleted,
    /// <summary>An action could not be launched, or Task Scheduler could not carry it out.</summary>
    ActionFailed,
    /// <summary>Task Scheduler is done with the run. Logged whatever the actions returned, and for a run that was stopped as well.</summary>
    RunCompleted,
    /// <summary>Task Scheduler is ending the run early: on request, for a new instance, on battery power or because the computer is no longer idle.</summary>
    Stopping,
    /// <summary>Task Scheduler ended the run because it exceeded the task's time limit.</summary>
    TimedOut,
    /// <summary>Task Scheduler ended the run.</summary>
    Terminated
}

/// <summary>One event of Task Scheduler's log about one task.</summary>
public sealed record TaskEvent
{
    public required TaskEventKind Kind { get; init; }

    /// <summary>The ID Task Scheduler logs the event under, e.g. 102.</summary>
    public required int EventId { get; init; }

    public required DateTimeOffset Time { get; init; }

    /// <summary>Full path of the task, e.g. \Backups\Nightly.</summary>
    public required string TaskPath { get; init; }

    /// <summary>Position in the log: a later event has a higher number.</summary>
    public long RecordId { get; init; }

    public EventLevel Level { get; init; } = EventLevel.Informational;

    /// <summary>The run the event belongs to. Null for events that are not about a run, and for a failed start, which Task Scheduler logs without it.</summary>
    public Guid? InstanceId { get; init; }

    /// <summary>
    /// For a completed action its exit code, for a failure the Windows error. An exit code above
    /// 65535 arrives cut down to its lower 16 bits: Task Scheduler logs it that way, and only the
    /// task's last run result keeps the full value.
    /// </summary>
    public uint? ResultCode { get; init; }

    /// <summary>The action the event is about, as Task Scheduler names it: usually the program's path.</summary>
    public string Action { get; init; } = "";

    /// <summary>The account the task ran as, or the user who caused the event.</summary>
    public string User { get; init; } = "";

    /// <summary>Reads the event out of a log record. Null for a record that is not about a task.</summary>
    public static TaskEvent? From(LogRecord record)
    {
        if (record.Provider != TaskSchedulerLog.Provider) return null;
        if (!record.Data.TryGetValue("TaskName", out string? taskPath) || taskPath.Length == 0) return null;

        var kind = TaskSchedulerLog.KindOf(record.Id);
        uint? code = record.Data.TryGetValue("ResultCode", out string? text) && uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : null;

        return new TaskEvent
        {
            Kind = kind,
            EventId = record.Id,
            Time = record.Time,
            TaskPath = taskPath,
            RecordId = record.RecordId,
            Level = record.Level,
            InstanceId = InstanceOf(record),
            ResultCode = kind == TaskEventKind.ActionCompleted && code is { } logged ? ToExitCode(logged) : code,
            // 129 (process created) names the program in "Path".
            Action = record.Data.GetValueOrDefault("ActionName") ?? record.Data.GetValueOrDefault("Path") ?? "",
            User = record.Data.GetValueOrDefault("UserContext") ?? record.Data.GetValueOrDefault("UserName") ?? ""
        };
    }

    /// <summary>
    /// Task Scheduler does not log an action's exit code as it is: a code from 1 to 65535 is
    /// wrapped as a Windows error (3 is logged as 0x80070003), and anything with the top bit set
    /// is logged unchanged. This undoes the wrapping.
    /// </summary>
    internal static uint ToExitCode(uint logged) =>
        (logged & 0xFFFF0000) == 0x80070000 ? logged & 0xFFFF : logged;

    private static Guid? InstanceOf(LogRecord record)
    {
        string field = record.Id switch
        {
            // 322 turns a start request down and names the instance that keeps running: not its own.
            322 => "",
            323 => "StoppedTaskInstanceId",
            324 or 325 => "QueuedTaskInstanceId",
            _ => record.Data.ContainsKey("InstanceId") ? "InstanceId" : "TaskInstanceId"
        };
        return record.Data.TryGetValue(field, out string? text) && Guid.TryParse(text, out Guid id) ? id : null;
    }
}
