using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Something that keeps a task from being saved.</summary>
/// <param name="Key">Resource name of the message.</param>
/// <param name="Argument">Value for the message's placeholder, e.g. the number of the trigger concerned.</param>
public sealed record DraftProblem(string Key, string Argument = "")
{
    public string Message => Loc.Format(Key, Argument);
}

/// <summary>
/// Checks a draft against what Task Scheduler accepts, so the user gets a sentence about the
/// field in question instead of an error code from the registration.
/// </summary>
public static class TaskDraftValidator
{
    private static readonly char[] NotAllowedInNames = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LongestInterval = TimeSpan.FromDays(31);

    public static IReadOnlyList<DraftProblem> Validate(TaskDraft draft)
    {
        var problems = new List<DraftProblem>();

        if (CheckName(draft.Name) is { } name) problems.Add(name);

        if (draft.Actions.Count == 0) problems.Add(new("Editor.Error.NoAction"));
        for (int i = 0; i < draft.Actions.Count; i++)
        {
            ActionDraft action = draft.Actions[i];
            if (action.IsEditable && string.IsNullOrWhiteSpace(action.Path))
                problems.Add(new("Editor.Error.ActionWithoutProgram", Number(i)));
        }

        for (int i = 0; i < draft.Triggers.Count; i++)
        {
            if (Check(draft.Triggers[i]) is { } key) problems.Add(new(key, Number(i)));
        }

        if (draft.Account is { Kind: TaskAccountKind.User or TaskAccountKind.Group } && string.IsNullOrWhiteSpace(draft.Account.Name))
            problems.Add(new("Editor.Error.UserNameMissing"));

        if (draft.RunOnlyIfIdle && draft.IdleDuration < Minute) problems.Add(new("Editor.Error.IdleDuration"));

        if (draft.RestartCount > 0 && (draft.RestartInterval < Minute || draft.RestartInterval > LongestInterval))
            problems.Add(new("Editor.Error.RestartInterval"));

        // Windows deletes an expired task some time after its last trigger has expired, so at
        // least one trigger has to expire, and it refuses the registration otherwise.
        if (draft.DeleteWhenExpired && !draft.Triggers.Any(trigger => trigger.End is not null))
            problems.Add(new("Editor.Error.DeleteNeedsExpiry"));

        return problems;
    }

    /// <summary>What keeps <paramref name="name"/> from being the name of a task, if anything.</summary>
    public static DraftProblem? CheckName(string name) =>
        string.IsNullOrWhiteSpace(name) ? new("Dialog.Error.EmptyName")
        : name.IndexOfAny(NotAllowedInNames) >= 0 ? new("Editor.Error.NameCharacters")
        : null;

    private static string? Check(TriggerDraft trigger)
    {
        switch (trigger.Type)
        {
            case TriggerType.Daily when trigger.DaysInterval < 1:
            case TriggerType.Weekly when trigger.WeeksInterval < 1:
                return "Editor.Error.TriggerInterval";
            case TriggerType.Weekly when trigger.DaysOfWeek == 0:
                return "Editor.Error.TriggerNeedsWeekday";
            case TriggerType.Monthly when trigger.Months == 0:
                return "Editor.Error.TriggerNeedsMonth";
            case TriggerType.Monthly when trigger.MonthlyByWeekday && (trigger.Weeks == 0 || trigger.DaysOfWeek == 0):
                return "Editor.Error.TriggerNeedsWeekAndWeekday";
            case TriggerType.Monthly when !trigger.MonthlyByWeekday && trigger.DaysOfMonth == 0 && !trigger.LastDayOfMonth:
                return "Editor.Error.TriggerNeedsDayOfMonth";
            case TriggerType.Event when string.IsNullOrWhiteSpace(trigger.EventSubscription):
                return "Editor.Error.EventNeedsLog";
        }

        if (trigger.IsCalendarBased && trigger.Start is null) return "Editor.Error.TriggerNeedsStart";
        if (trigger.End is { } end && trigger.Start is { } start && end <= start) return "Editor.Error.TriggerEndsBeforeStart";

        if (trigger.RepetitionInterval != TimeSpan.Zero)
        {
            if (trigger.RepetitionInterval < Minute || trigger.RepetitionInterval > LongestInterval) return "Editor.Error.RepetitionInterval";
            if (trigger.RepetitionDuration != TimeSpan.Zero && trigger.RepetitionDuration < trigger.RepetitionInterval)
                return "Editor.Error.RepetitionDuration";
        }
        return null;
    }

    private static string Number(int index) => (index + 1).ToString();
}
