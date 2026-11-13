using System.Diagnostics.Tracing;
using System.Globalization;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.History;

/// <summary>The words for what a task's history shows, in the app's language.</summary>
public static class HistoryText
{
    /// <summary>The short name of an event, e.g. "Task Completed"; "Event 319" for one the app has no name for.</summary>
    public static string Label(TaskEvent e) =>
        LabelKey(e.EventId) is { } key ? Loc.Get(key) : Loc.Format("EventResult.Unknown", e.EventId);

    public static string Level(EventLevel level) => Loc.Get(level switch
    {
        EventLevel.Critical => "EventLevel.Critical",
        EventLevel.Error => "EventLevel.Error",
        EventLevel.Warning => "EventLevel.Warning",
        EventLevel.Verbose => "EventLevel.Verbose",
        _ => "EventLevel.Information"
    });

    public static string Status(RunStatus status) => Loc.Get("RunStatus." + status);

    public static string Label(SystemEventKind kind) => Loc.Get("SystemEvent." + kind);

    /// <summary>
    /// A result code the way people look it up: a number up to 65535 is an exit code and stays
    /// decimal ("3"), anything larger is a Windows error and is written in hex ("0x80070002").
    /// </summary>
    public static string Code(uint code) =>
        code <= 0xFFFF
            ? code.ToString(CultureInfo.InvariantCulture)
            : "0x" + code.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>
    /// The resource key of an event's name. Events that mean the same to a user share one: every
    /// kind of trigger is "triggered", every launch condition that was not met is "skipped".
    /// </summary>
    internal static string? LabelKey(int eventId) => eventId switch
    {
        100 or 101 or 102 or 106 or 107 or 110 or 111 or 118 or 119 or 129 or 140 or 141 or 142 or 153
            or 200 or 201 or 202 or 203 or 322 or 323 or 324 or 327 or 328 or 329 or 330 => "EventResult." + eventId.ToString(CultureInfo.InvariantCulture),
        103 => "EventResult.101",
        108 or 109 or 114 or 117 or (>= 120 and <= 125) => "EventResult.107",
        325 => "EventResult.324",
        112 or 128 or 135 or 326 or 332 or 333 or 334 => "EventResult.332",
        _ => null
    };
}
