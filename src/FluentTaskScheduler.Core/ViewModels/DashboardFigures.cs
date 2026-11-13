using System.Globalization;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>Which entries the dashboard's log shows.</summary>
public enum LogFilter
{
    All,
    Succeeded,

    /// <summary>Runs that failed, could not start or timed out.</summary>
    Failed,

    /// <summary>Starts the app refused because tasks were snoozed.</summary>
    Snoozed
}

/// <summary>How an entry of the dashboard's log turned out, as far as its icon tells.</summary>
public enum LogOutcome
{
    Running,
    Succeeded,

    /// <summary>The run failed, could not start or timed out.</summary>
    Failed,

    /// <summary>Task Scheduler ended the run early, for another reason than its time limit.</summary>
    Stopped,

    /// <summary>The app did not start the task because tasks were snoozed.</summary>
    Snoozed
}

/// <summary>How the health score reads at a glance.</summary>
public enum HealthLevel
{
    /// <summary>There is no score: no run finished.</summary>
    None,
    Good,
    Fair,
    Poor
}

/// <summary>One line of a dashboard list that leads to a task.</summary>
/// <param name="Text">What the line says next to the task's name.</param>
public sealed record TaskLine(string TaskPath, string Text)
{
    public string TaskName => TaskPaths.NameOf(TaskPath);

    /// <summary>The name: what a screen reader announces for the line.</summary>
    public override string ToString() => TaskName;
}

/// <summary>One entry of the dashboard's log: a run, or a start the app refused because tasks were snoozed.</summary>
/// <param name="Key">Tells apart two entries that read the same.</param>
/// <param name="Time">When the run ended, or began while it is going on; when the start was refused.</param>
/// <param name="Status">How it turned out, in words.</param>
/// <param name="Detail">How long the run took or what it returned; for a refused start, who asked for it.</param>
public sealed record LogEntry(string Key, string TaskPath, LogOutcome Outcome, string Time, string Status, string Detail)
{
    public string TaskName => TaskPaths.NameOf(TaskPath);

    /// <summary>What a screen reader announces for the entry.</summary>
    public override string ToString() => $"{TaskName}, {Status}, {Time}";
}

/// <summary>The runs that began in one hour of the day, over the dashboard's period.</summary>
/// <param name="Shade">How strongly the cell is coloured: 0 for an hour without a run, 1 for the busiest hour.</param>
/// <param name="Description">The hour and its runs in words.</param>
public sealed record HourCell(int Hour, int Runs, double Shade, string Description);

/// <summary>
/// Everything the dashboard shows, worked out from the tasks, the runs and the starts a snooze
/// refused. It is one unchangeable whole, so the page never shows figures that belong to two
/// different moments.
/// </summary>
/// <remarks>
/// A run belongs to a period, the last seven days or the last day, when it ended in it or is
/// still going on.
/// </remarks>
public sealed record DashboardFigures
{
    /// <summary>How far back the dashboard looks.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(7);

    /// <summary>How many entries the log holds at most: the newest ones.</summary>
    public const int MostLogEntries = 50;

    private const int MostUpcoming = 5;

    /// <summary>How strongly an hour with a single run among many is still coloured, so that it cannot pass for one without a run.</summary>
    private const double LightestShade = 0.2;

    public static DashboardFigures None { get; } = new();

    public int TotalTasks { get; private init; }
    public int EnabledTasks { get; private init; }
    public int RunningTasks { get; private init; }

    public string TasksNote => Loc.Format("Dashboard.Tasks.Note", EnabledTasks, RunningTasks);

    /// <summary>The share of finished runs that succeeded, in percent; null without a finished run, which says nothing either way.</summary>
    public int? HealthScore { get; private init; }

    public HealthLevel Health => HealthScore switch
    {
        null => HealthLevel.None,
        >= 90 => HealthLevel.Good,
        >= 70 => HealthLevel.Fair,
        _ => HealthLevel.Poor
    };

    public double HealthValue => HealthScore ?? 0;
    public string HealthText => HealthScore is { } score ? (score / 100.0).ToString("P0", CultureInfo.CurrentCulture) : "-";

    public int SucceededRuns { get; private init; }

    /// <summary>Runs that failed, could not start or timed out. A run somebody stopped counts neither way.</summary>
    public int FailedRuns { get; private init; }

    public string HealthNote => Loc.Format("Dashboard.Health.Note", SucceededRuns, FailedRuns);

    public int RunsInPeriod { get; private init; }
    public int RunsLastDay { get; private init; }

    public string RunsNote => Loc.Format("Dashboard.Runs.Note", RunsLastDay);

    public string AverageDuration { get; private init; } = "-";
    public string LongestDuration { get; private init; } = "-";

    /// <summary>The name of the task with the longest run; empty without one.</summary>
    public string LongestTask { get; private init; } = "";

    public string LongestRun => LongestTask.Length == 0 ? "" : Loc.Format("Dashboard.Duration.Note", LongestDuration, LongestTask);

    public IReadOnlyList<HourCell> Hours { get; private init; } = [];

    /// <summary>The hour of the day in which the most runs began, e.g. "03:00"; a dash without runs.</summary>
    public string BusiestHour { get; private init; } = "-";

    /// <summary>The running tasks, the one that started last first.</summary>
    public IReadOnlyList<TaskLine> Running { get; private init; } = [];

    /// <summary>The next tasks to run, soonest first. A start time that has gone by is not upcoming any more.</summary>
    public IReadOnlyList<TaskLine> Upcoming { get; private init; } = [];

    /// <summary>The runs and refused starts that pass the log's filter, newest first.</summary>
    public IReadOnlyList<LogEntry> Log { get; private init; } = [];

    public bool NothingRunning => Running.Count == 0;
    public bool NothingUpcoming => Upcoming.Count == 0;
    public bool LogIsEmpty => Log.Count == 0;

    /// <param name="tasks">The tasks to count.</param>
    /// <param name="runs">Runs in any order, of any time; those of the period count.</param>
    /// <param name="refused">Starts a snooze refused, of any time; those of the period are logged.</param>
    /// <param name="filter">Which entries the log shows.</param>
    public static DashboardFigures From(IReadOnlyCollection<TaskRow> tasks, IEnumerable<TaskRun> runs, IEnumerable<SuppressedRun> refused,
        LogFilter filter, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTime Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, zone).DateTime;
        string TimeText(DateTime local) => local.ToString(local.Date == today ? "t" : "g", CultureInfo.CurrentCulture);

        List<TaskRun> ofPeriod = runs.Where(run => EndsAfter(run, now - Period)).ToList();
        List<TaskRun> finished = ofPeriod.Where(run => run.IsFinished).ToList();
        int succeeded = finished.Count(run => run.Status == RunStatus.Succeeded);
        int failed = finished.Count(run => WentWrong(run.Status));

        int[] perHour = new int[24];
        foreach (TaskRun run in ofPeriod) perHour[Local(run.Started ?? MomentOf(run)).Hour]++;
        int busiest = perHour.Max();

        List<TaskRun> timed = finished.Where(run => run.Duration >= TimeSpan.Zero).ToList();
        TaskRun? longest = timed.MaxBy(run => run.Duration);

        IEnumerable<(DateTimeOffset At, LogEntry Entry)> logged = ofPeriod
            .Where(run => Passes(filter, run.Status))
            .Select(run => (MomentOf(run), new LogEntry(
                KeyOf(run), run.TaskPath, OutcomeOf(run.Status), TimeText(Local(MomentOf(run))), HistoryText.Status(run.Status), DetailOf(run))));
        if (filter is LogFilter.All or LogFilter.Snoozed)
        {
            logged = logged.Concat(refused.Where(start => start.Time >= now - Period).Select(start => (start.Time, new LogEntry(
                $"Refused|{start.TaskPath}|{start.Time.UtcTicks}", start.TaskPath, LogOutcome.Snoozed, TimeText(Local(start.Time)),
                Loc.Get("Dashboard.Status.Snoozed"),
                Loc.Get(start.Origin == RunOrigin.Manual ? "Dashboard.Refused.Manual" : "Dashboard.Refused.CompletionAction")))));
        }

        return new DashboardFigures
        {
            TotalTasks = tasks.Count,
            EnabledTasks = tasks.Count(task => task.IsEnabled),
            RunningTasks = tasks.Count(task => task.State == TaskRowState.Running),

            HealthScore = succeeded + failed == 0 ? null : (int)Math.Round(100.0 * succeeded / (succeeded + failed), MidpointRounding.AwayFromZero),
            SucceededRuns = succeeded,
            FailedRuns = failed,

            RunsInPeriod = ofPeriod.Count,
            RunsLastDay = ofPeriod.Count(run => EndsAfter(run, now - TimeSpan.FromDays(1))),
            AverageDuration = timed.Count == 0 ? "-" : Duration(TimeSpan.FromTicks((long)timed.Average(run => run.Duration!.Value.Ticks))),
            LongestDuration = longest is null ? "-" : Duration(longest.Duration!.Value),
            LongestTask = longest is null ? "" : TaskPaths.NameOf(longest.TaskPath),

            Hours = perHour.Select((count, hour) => new HourCell(
                hour,
                count,
                count == 0 ? 0 : LightestShade + (1 - LightestShade) * count / busiest,
                Loc.Format("Dashboard.Heatmap.CellTooltip", hour.ToString("00", CultureInfo.InvariantCulture), count))).ToList(),
            BusiestHour = busiest == 0 ? "-" : Array.IndexOf(perHour, busiest).ToString("00", CultureInfo.InvariantCulture) + ":00",

            Running = tasks.Where(task => task.State == TaskRowState.Running)
                .OrderByDescending(task => task.LastRunTime)
                .Select(task => new TaskLine(task.Path, task.LastRunTime is { } start ? Loc.Format("Dashboard.Running.Since", TimeText(start)) : ""))
                .ToList(),
            Upcoming = tasks.Where(task => NextStartOf(task, zone) > now)
                .OrderBy(task => task.NextRunTime)
                .Take(MostUpcoming)
                .Select(task => new TaskLine(task.Path, TimeText(task.NextRunTime!.Value)))
                .ToList(),
            Log = logged.OrderByDescending(entry => entry.At).Take(MostLogEntries).Select(entry => entry.Entry).ToList()
        };
    }

    /// <summary>What tells one run from another: its instance, or for a start that failed without one, the task and the moment.</summary>
    internal static string KeyOf(TaskRun run) =>
        run.InstanceId?.ToString("N") ?? $"{run.TaskPath.ToUpperInvariant()}|{run.Ended?.UtcTicks}";

    /// <summary>True for a run that ended after <paramref name="moment"/>, and for one that is still going on.</summary>
    internal static bool EndsAfter(TaskRun run, DateTimeOffset moment) => run.Ended is not { } ended || ended >= moment;

    /// <summary>When a task is to run next; null for one that is disabled or not going to run again.</summary>
    /// <remarks>Task Scheduler gives the time of day in the user's time zone, without an offset.</remarks>
    internal static DateTimeOffset? NextStartOf(TaskRow task, TimeZoneInfo zone)
    {
        if (!task.IsEnabled || task.NextRunTime is not { } next) return null;
        next = DateTime.SpecifyKind(next, DateTimeKind.Unspecified);
        return new DateTimeOffset(next, zone.GetUtcOffset(next));
    }

    /// <summary>Where a run stands in time: at its end, or at its start while it is going on.</summary>
    private static DateTimeOffset MomentOf(TaskRun run) => run.Ended ?? run.Started ?? run.Events[0].Time;

    private static bool WentWrong(RunStatus status) => status is RunStatus.Failed or RunStatus.CouldNotStart or RunStatus.TimedOut;

    private static bool Passes(LogFilter filter, RunStatus status) => filter switch
    {
        LogFilter.All => true,
        LogFilter.Succeeded => status == RunStatus.Succeeded,
        LogFilter.Failed => WentWrong(status),
        _ => false
    };

    private static LogOutcome OutcomeOf(RunStatus status) => status switch
    {
        RunStatus.Running => LogOutcome.Running,
        RunStatus.Succeeded => LogOutcome.Succeeded,
        RunStatus.Stopped => LogOutcome.Stopped,
        _ => LogOutcome.Failed
    };

    private static string DetailOf(TaskRun run) => run switch
    {
        { ResultCode: { } code and not 0 } => Loc.Format("Details.History.ResultFormat", HistoryText.Code(code)),
        { IsFinished: true, Duration: { } duration } when duration >= TimeSpan.Zero => Duration(duration),
        _ => ""
    };

    /// <summary>A duration in its two largest units, e.g. "1m 35s", "2h 5m" or "1d 6h": the seconds of a run that took hours say nothing.</summary>
    private static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1)) return "< 1s";

        TimeSpan unit = duration < TimeSpan.FromHours(1) ? TimeSpan.FromSeconds(1)
            : duration < TimeSpan.FromDays(1) ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromHours(1);
        return DurationText.Format(unit * Math.Round(duration / unit, MidpointRounding.AwayFromZero));
    }
}
