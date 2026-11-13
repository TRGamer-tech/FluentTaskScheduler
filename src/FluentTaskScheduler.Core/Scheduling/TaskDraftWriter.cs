using Microsoft.Win32.TaskScheduler;
using TaskAction = Microsoft.Win32.TaskScheduler.Action;

namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Writes a <see cref="TaskDraft"/> onto a task definition.
///
/// When the definition belongs to an existing task, only what differs from the draft the
/// editor started with (the baseline) is written. A task definition holds far more than the
/// editor shows, and several of the values it does show are simplified on the way in; writing
/// everything back would quietly change settings nobody touched.
/// </summary>
internal static class TaskDraftWriter
{
    /// <summary>How long an expired task is kept when "delete when expired" is switched on here. Matches Task Scheduler's own default.</summary>
    private static readonly TimeSpan DeleteExpiredAfter = TimeSpan.FromDays(30);

    /// <param name="draft">The task as it should be.</param>
    /// <param name="baseline">The task as it was read from <paramref name="definition"/>; null for a new task.</param>
    /// <param name="definition">The definition to change: the existing task's, or an empty one.</param>
    public static void Apply(TaskDraft draft, TaskDraft? baseline, TaskDefinition definition)
    {
        if (baseline is null
            || draft.Description != baseline.Description
            || draft.Category != baseline.Category
            || !draft.Tags.SequenceEqual(baseline.Tags)
            || draft.CompletionActions != baseline.CompletionActions)
        {
            definition.RegistrationInfo.Description =
                TaskMetadata.Combine(draft.Description, draft.Category, draft.Tags, draft.CompletionActions);
        }

        if (draft.Author != baseline?.Author) definition.RegistrationInfo.Author = draft.Author;
        if (draft.HighestPrivileges != baseline?.HighestPrivileges)
            definition.Principal.RunLevel = draft.HighestPrivileges ? TaskRunLevel.Highest : TaskRunLevel.LUA;

        var settings = definition.Settings;
        if (draft.Enabled != baseline?.Enabled) settings.Enabled = draft.Enabled;
        if (draft.Hidden != baseline?.Hidden) settings.Hidden = draft.Hidden;

        if (draft.RunOnlyIfIdle != baseline?.RunOnlyIfIdle) settings.RunOnlyIfIdle = draft.RunOnlyIfIdle;
        if (draft.IdleDuration != baseline?.IdleDuration) settings.IdleSettings.IdleDuration = draft.IdleDuration;
        if (draft.StopOnIdleEnd != baseline?.StopOnIdleEnd) settings.IdleSettings.StopOnIdleEnd = draft.StopOnIdleEnd;
        if (draft.RunOnlyOnAcPower != baseline?.RunOnlyOnAcPower) settings.DisallowStartIfOnBatteries = draft.RunOnlyOnAcPower;
        if (draft.StopOnBattery != baseline?.StopOnBattery) settings.StopIfGoingOnBatteries = draft.StopOnBattery;
        if (draft.RunOnlyIfNetwork != baseline?.RunOnlyIfNetwork) settings.RunOnlyIfNetworkAvailable = draft.RunOnlyIfNetwork;
        if (draft.NetworkId != baseline?.NetworkId || draft.NetworkName != baseline?.NetworkName)
        {
            settings.NetworkSettings.Id = draft.NetworkId;
            settings.NetworkSettings.Name = draft.NetworkId == Guid.Empty ? null : draft.NetworkName;
        }
        if (draft.WakeToRun != baseline?.WakeToRun) settings.WakeToRun = draft.WakeToRun;

        if (draft.ExecutionTimeLimit != baseline?.ExecutionTimeLimit) settings.ExecutionTimeLimit = draft.ExecutionTimeLimit;
        if (draft.RunIfMissed != baseline?.RunIfMissed) settings.StartWhenAvailable = draft.RunIfMissed;
        if (draft.MultipleInstances != baseline?.MultipleInstances) settings.MultipleInstances = draft.MultipleInstances;
        if (draft.Priority != baseline?.Priority) settings.Priority = draft.Priority;
        if (draft.DeleteWhenExpired != baseline?.DeleteWhenExpired)
            settings.DeleteExpiredTaskAfter = draft.DeleteWhenExpired ? DeleteExpiredAfter : TimeSpan.Zero;
        if (draft.AllowHardTerminate != baseline?.AllowHardTerminate) settings.AllowHardTerminate = draft.AllowHardTerminate;

        if (draft.RestartCount != baseline?.RestartCount || draft.RestartInterval != baseline?.RestartInterval)
        {
            // Task Scheduler only accepts the two together.
            settings.RestartCount = draft.RestartCount;
            settings.RestartInterval = draft.RestartCount > 0 ? draft.RestartInterval : TimeSpan.Zero;
        }

        ApplyTriggers(draft, baseline, definition);
        ApplyActions(draft.Actions, baseline?.Actions ?? Array.Empty<ActionDraft>(), definition.Actions);
    }


    private static void ApplyTriggers(TaskDraft draft, TaskDraft? baseline, TaskDefinition definition)
    {
        var drafts = draft.Triggers;
        var before = baseline?.Triggers ?? Array.Empty<TriggerDraft>();
        var triggers = definition.Triggers;

        // The usual edit leaves the list as it is and changes a trigger or two: do that in place.
        bool sameList = drafts.Count == triggers.Count
            && drafts.Select((d, i) => d.SourceIndex == i && SameKind(d, before[i])).All(same => same);
        if (sameList)
        {
            for (int i = 0; i < drafts.Count; i++)
                if (drafts[i] != before[i]) Configure(triggers[i], drafts[i], before[i]);
            return;
        }

        // Triggers were added, removed, reordered or changed type: rebuild the list, carrying the
        // originals over as copies so everything about them the editor does not show survives.
        var rebuilt = new List<Trigger>(drafts.Count);
        foreach (var item in drafts)
        {
            if (item.SourceIndex is int source && source < before.Count && SameKind(item, before[source]))
            {
                var copy = (Trigger)triggers[source].Clone();
                if (item != before[source]) Configure(copy, item, before[source]);
                rebuilt.Add(copy);
            }
            else
            {
                rebuilt.Add(Create(item, draft.Account));
            }
        }

        triggers.Clear();
        triggers.AddRange(rebuilt);
    }

    /// <summary>True if <paramref name="a"/> can be written onto the trigger object <paramref name="b"/> was read from.</summary>
    private static bool SameKind(TriggerDraft a, TriggerDraft b) =>
        a.Type == b.Type && (a.Type != TriggerType.Monthly || a.MonthlyByWeekday == b.MonthlyByWeekday);

    private static Trigger Create(TriggerDraft draft, TaskAccount account)
    {
        Trigger trigger = draft.Type switch
        {
            TriggerType.Daily => new DailyTrigger(),
            TriggerType.Weekly => new WeeklyTrigger(),
            TriggerType.Monthly => draft.MonthlyByWeekday ? new MonthlyDOWTrigger() : new MonthlyTrigger(),
            TriggerType.Once => new TimeTrigger(),
            // Without a user these fire for every user's logon or session, which only an
            // administrator may register; a task that runs as one user wants that user anyway.
            TriggerType.AtLogon => new LogonTrigger { UserId = TaskAccounts.TriggerUser(account) },
            TriggerType.SessionStateChange => new SessionStateChangeTrigger { UserId = TaskAccounts.TriggerUser(account) },
            TriggerType.AtStartup => new BootTrigger(),
            TriggerType.OnIdle => new IdleTrigger(),
            TriggerType.Event => new EventTrigger(),
            _ => throw new InvalidOperationException($"A trigger of type {draft.Type} cannot be created here.")
        };
        Configure(trigger, draft, null);
        return trigger;
    }

    /// <summary>Writes the properties of <paramref name="draft"/> that differ from <paramref name="before"/> (all of them for a new trigger).</summary>
    private static void Configure(Trigger trigger, TriggerDraft draft, TriggerDraft? before)
    {
        if (draft.Enabled != before?.Enabled) trigger.Enabled = draft.Enabled;
        if (draft.Start != before?.Start) trigger.StartBoundary = draft.Start ?? DateTime.MinValue;
        if (draft.End != before?.End) trigger.EndBoundary = draft.End ?? DateTime.MaxValue;

        if (draft.RepetitionInterval != before?.RepetitionInterval || draft.RepetitionDuration != before?.RepetitionDuration)
        {
            trigger.Repetition.Interval = draft.RepetitionInterval;
            trigger.Repetition.Duration = draft.RepetitionInterval > TimeSpan.Zero ? draft.RepetitionDuration : TimeSpan.Zero;
        }

        bool delayChanged = draft.RandomDelay != before?.RandomDelay;
        switch (trigger)
        {
            case DailyTrigger daily:
                if (draft.DaysInterval != before?.DaysInterval) daily.DaysInterval = draft.DaysInterval;
                if (delayChanged) daily.RandomDelay = draft.RandomDelay;
                break;

            case WeeklyTrigger weekly:
                if (draft.WeeksInterval != before?.WeeksInterval) weekly.WeeksInterval = draft.WeeksInterval;
                if (draft.DaysOfWeek != before?.DaysOfWeek) weekly.DaysOfWeek = draft.DaysOfWeek;
                if (delayChanged) weekly.RandomDelay = draft.RandomDelay;
                break;

            case MonthlyTrigger monthly:
                if (draft.Months != before?.Months) monthly.MonthsOfYear = draft.Months;
                if (draft.DaysOfMonth != before?.DaysOfMonth) monthly.DaysOfMonth = TaskDraftReader.DaysOfMonthFromMask(draft.DaysOfMonth);
                if (draft.LastDayOfMonth != before?.LastDayOfMonth) monthly.RunOnLastDayOfMonth = draft.LastDayOfMonth;
                if (delayChanged) monthly.RandomDelay = draft.RandomDelay;
                break;

            case MonthlyDOWTrigger monthlyByWeekday:
                if (draft.Months != before?.Months) monthlyByWeekday.MonthsOfYear = draft.Months;
                if (draft.DaysOfWeek != before?.DaysOfWeek) monthlyByWeekday.DaysOfWeek = draft.DaysOfWeek;
                if (draft.Weeks != before?.Weeks) monthlyByWeekday.WeeksOfMonth = draft.Weeks;
                if (delayChanged) monthlyByWeekday.RandomDelay = draft.RandomDelay;
                break;

            case TimeTrigger once:
                if (delayChanged) once.RandomDelay = draft.RandomDelay;
                break;

            case EventTrigger onEvent:
                if (draft.EventSubscription != before?.EventSubscription) onEvent.Subscription = draft.EventSubscription;
                break;

            case SessionStateChangeTrigger session:
                if (draft.SessionChange != before?.SessionChange) session.StateChange = draft.SessionChange;
                break;
        }
    }


    private static void ApplyActions(IReadOnlyList<ActionDraft> drafts, IReadOnlyList<ActionDraft> before, ActionCollection actions)
    {
        bool sameList = drafts.Count == actions.Count
            && drafts.Select((d, i) => d.SourceIndex == i).All(same => same);
        if (sameList)
        {
            for (int i = 0; i < drafts.Count; i++)
                if (drafts[i] != before[i] && actions[i] is ExecAction exec) Configure(exec, drafts[i]);
            return;
        }

        var rebuilt = new List<TaskAction>(drafts.Count);
        foreach (var item in drafts)
        {
            if (item.SourceIndex is int source && source < before.Count)
            {
                var copy = (TaskAction)actions[source].Clone();
                if (item != before[source] && copy is ExecAction exec) Configure(exec, item);
                rebuilt.Add(copy);
            }
            else
            {
                var exec = new ExecAction();
                Configure(exec, item);
                rebuilt.Add(exec);
            }
        }

        actions.Clear();
        actions.AddRange(rebuilt);
    }

    private static void Configure(ExecAction action, ActionDraft draft)
    {
        action.Path = draft.Path;
        action.Arguments = draft.Arguments;
        action.WorkingDirectory = draft.WorkingDirectory;
    }
}
