using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Library;

/// <summary>
/// A task in outline: what it runs, when, and under which conditions. A template is not tied to
/// a point in time; <see cref="ToDraft"/> turns it into a task that could be saved right now.
/// </summary>
public sealed record TaskTemplate
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    public required string Command { get; init; }
    public string Arguments { get; init; } = "";

    /// <summary>When the task runs. A trigger that fires at a time of day takes that time from <see cref="At"/>.</summary>
    public TriggerDraft Trigger { get; init; } = new() { Type = TriggerType.Daily };

    /// <summary>
    /// The time of day for a trigger that needs one. Null when the template stands for no
    /// particular time: the task then starts out at the next full hour, for the user to change.
    /// </summary>
    public TimeOnly? At { get; init; }

    public bool RunElevated { get; init; }
    public bool RunIfMissed { get; init; }
    public bool OnlyWhenIdle { get; init; }
    public bool OnlyOnAcPower { get; init; }

    public string Category { get; init; } = "";
    public ValueList<string> Tags { get; init; } = [];

    /// <summary>
    /// The task this template stands for, as the task editor takes it. Everything the template
    /// does not decide is as in a task newly created in the app.
    /// </summary>
    /// <param name="now">The current local time; a trigger with a time of day first fires at its next occurrence after this.</param>
    public TaskDraft ToDraft(DateTime now) => new()
    {
        Name = AsTaskName(Name),
        Description = Description,
        Author = Environment.UserName,
        Category = Category,
        Tags = Tags,
        HighestPrivileges = RunElevated,
        RunIfMissed = RunIfMissed,
        RunOnlyIfIdle = OnlyWhenIdle,
        RunOnlyOnAcPower = OnlyOnAcPower,
        Triggers = [Trigger.IsCalendarBased ? Trigger with { Start = FirstStart(now) } : Trigger],
        Actions = [new ActionDraft { Path = Command, Arguments = Arguments }]
    };

    private DateTime FirstStart(DateTime now)
    {
        // A start time with a time zone pins the task to that zone's clock, and it would shift by
        // an hour against the local time whenever daylight saving starts or ends.
        now = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);

        if (At is not { } time) return now.Date.AddHours(now.Hour + 1);

        DateTime today = now.Date + time.ToTimeSpan();
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>Task Scheduler keeps every task as a file, so a task name has to be a valid file name.</summary>
    private static string AsTaskName(string name)
    {
        char[] characters = name.Trim().ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] < ' ' || @"\/:*?""<>|".Contains(characters[i])) characters[i] = '-';
        }
        return new string(characters).TrimEnd('.', ' ');
    }
}
