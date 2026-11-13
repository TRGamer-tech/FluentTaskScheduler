using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;
using TaskAction = Microsoft.Win32.TaskScheduler.Action;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Reads a registered task into a <see cref="TaskDraft"/>.</summary>
internal static class TaskDraftReader
{
    public static TaskDraft Read(ScheduledTask task) => Read(task.Definition) with { Name = task.Name };

    public static TaskDraft Read(TaskDefinition definition)
    {
        var metadata = TaskMetadata.Split(definition.RegistrationInfo.Description);
        var settings = definition.Settings;
        var principal = definition.Principal;

        return new TaskDraft
        {
            Description = metadata.Description,
            Category = metadata.Category,
            Tags = metadata.Tags,
            CompletionActions = metadata.CompletionActions,
            Author = definition.RegistrationInfo.Author ?? "",
            Enabled = settings.Enabled,
            Hidden = settings.Hidden,

            Account = TaskAccounts.FromPrincipal(principal),
            HighestPrivileges = principal.RunLevel == TaskRunLevel.Highest,
            StoresPassword = principal.LogonType is TaskLogonType.Password or TaskLogonType.InteractiveTokenOrPassword,

            RunOnlyIfIdle = settings.RunOnlyIfIdle,
            IdleDuration = settings.IdleSettings.IdleDuration,
            StopOnIdleEnd = settings.IdleSettings.StopOnIdleEnd,
            RunOnlyOnAcPower = settings.DisallowStartIfOnBatteries,
            StopOnBattery = settings.StopIfGoingOnBatteries,
            RunOnlyIfNetwork = settings.RunOnlyIfNetworkAvailable,
            NetworkId = settings.NetworkSettings.Id,
            NetworkName = settings.NetworkSettings.Name ?? "",
            WakeToRun = settings.WakeToRun,

            ExecutionTimeLimit = settings.ExecutionTimeLimit,
            RunIfMissed = settings.StartWhenAvailable,
            MultipleInstances = settings.MultipleInstances,
            Priority = settings.Priority,
            DeleteWhenExpired = settings.DeleteExpiredTaskAfter != TimeSpan.Zero,
            AllowHardTerminate = settings.AllowHardTerminate,
            RestartCount = settings.RestartCount,
            RestartInterval = settings.RestartInterval,

            Triggers = definition.Triggers.Select(Read).ToArray(),
            Actions = definition.Actions.Select(Read).ToArray()
        };
    }

    public static TriggerDraft Read(Trigger trigger, int index)
    {
        var draft = new TriggerDraft
        {
            SourceIndex = index,
            Enabled = trigger.Enabled,
            Start = trigger.StartBoundary == DateTime.MinValue ? null : LocalTime(trigger.StartBoundary),
            End = trigger.EndBoundary == DateTime.MaxValue ? null : LocalTime(trigger.EndBoundary),
            RepetitionInterval = trigger.Repetition.Interval,
            RepetitionDuration = trigger.Repetition.Duration
        };

        return trigger switch
        {
            DailyTrigger t => draft with { Type = TriggerType.Daily, DaysInterval = t.DaysInterval, RandomDelay = t.RandomDelay },
            WeeklyTrigger t => draft with { Type = TriggerType.Weekly, WeeksInterval = t.WeeksInterval, DaysOfWeek = t.DaysOfWeek, RandomDelay = t.RandomDelay },
            MonthlyTrigger t => draft with
            {
                Type = TriggerType.Monthly,
                MonthlyByWeekday = false,
                Months = t.MonthsOfYear,
                DaysOfMonth = DaysOfMonthMask(t.DaysOfMonth),
                LastDayOfMonth = t.RunOnLastDayOfMonth,
                RandomDelay = t.RandomDelay
            },
            MonthlyDOWTrigger t => draft with
            {
                Type = TriggerType.Monthly,
                MonthlyByWeekday = true,
                Months = t.MonthsOfYear,
                DaysOfWeek = t.DaysOfWeek,
                Weeks = t.WeeksOfMonth,
                RandomDelay = t.RandomDelay
            },
            TimeTrigger t => draft with { Type = TriggerType.Once, RandomDelay = t.RandomDelay },
            LogonTrigger => draft with { Type = TriggerType.AtLogon },
            BootTrigger => draft with { Type = TriggerType.AtStartup },
            IdleTrigger => draft with { Type = TriggerType.OnIdle },
            EventTrigger t => draft with { Type = TriggerType.Event, EventSubscription = t.Subscription ?? "" },
            SessionStateChangeTrigger t => draft with { Type = TriggerType.SessionStateChange, SessionChange = t.StateChange },
            _ => draft with { Type = TriggerType.Unsupported, UnsupportedSummary = trigger.TriggerType.ToString() }
        };
    }

    public static ActionDraft Read(TaskAction action, int index) => action switch
    {
        ExecAction exec => new ActionDraft
        {
            SourceIndex = index,
            Path = exec.Path ?? "",
            Arguments = exec.Arguments ?? "",
            WorkingDirectory = exec.WorkingDirectory ?? ""
        },
        ComHandlerAction com => new ActionDraft { SourceIndex = index, UnsupportedSummary = $"{action.ActionType} {com.ClassId:B}" },
        _ => new ActionDraft { SourceIndex = index, UnsupportedSummary = action.ActionType.ToString() }
    };

    /// <summary>
    /// Task Scheduler keeps a boundary as it was written: without a time zone, with an offset, or
    /// in UTC. The library hands out the first as it is, the second in local time and a start in
    /// UTC as UTC. The editor and its texts work with the local clock, so that one is converted.
    /// </summary>
    private static DateTime LocalTime(DateTime boundary) => boundary.Kind == DateTimeKind.Utc ? boundary.ToLocalTime() : boundary;

    /// <summary>Days 1 to 31 as a bit mask: bit 0 is the 1st.</summary>
    public static uint DaysOfMonthMask(IEnumerable<int> days) =>
        days.Where(day => day is >= 1 and <= 31).Aggregate(0u, (mask, day) => mask | 1u << (day - 1));

    public static int[] DaysOfMonthFromMask(uint mask) =>
        Enumerable.Range(1, 31).Where(day => (mask & 1u << (day - 1)) != 0).ToArray();
}
