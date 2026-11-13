using System.Globalization;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Snoozing;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class DashboardFiguresTests
{
    // A Friday afternoon, in a place two hours ahead of UTC.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test");
    private static readonly DateTimeOffset Now = new(2031, 5, 9, 14, 30, 0, TimeSpan.FromHours(2));

    internal static TaskRun Run(string path, RunStatus status, DateTimeOffset started, TimeSpan lasted, uint? code = null) => new()
    {
        TaskPath = path,
        Status = status,
        InstanceId = Guid.NewGuid(),
        Started = status == RunStatus.CouldNotStart ? null : started,
        Ended = status == RunStatus.Running ? null : started + lasted,
        ResultCode = code
    };

    private static DashboardFigures Of(IEnumerable<TaskRun> runs, IEnumerable<SuppressedRun>? refused = null, LogFilter filter = LogFilter.All) =>
        DashboardFigures.From([], runs, refused ?? [], filter, Now, Zone);

    [Fact]
    public void Tasks_AreCounted_WithHowManyAreEnabledAndHowManyRun()
    {
        var tasks = new[]
        {
            new TaskRow(@"\Ready") { IsEnabled = true, State = TaskRowState.Ready },
            new TaskRow(@"\Busy") { IsEnabled = true, State = TaskRowState.Running },
            new TaskRow(@"\Off") { IsEnabled = false, State = TaskRowState.Disabled }
        };

        var figures = DashboardFigures.From(tasks, [], [], LogFilter.All, Now, Zone);

        Assert.Equal((3, 2, 1), (figures.TotalTasks, figures.EnabledTasks, figures.RunningTasks));
    }

    [Fact]
    public void Health_IsTheShareOfFinishedRunsThatSucceeded_AndStoppedRunsCountNeitherWay()
    {
        var figures = Of(
        [
            Run(@"\A", RunStatus.Succeeded, Now.AddHours(-1), TimeSpan.FromSeconds(10)),
            Run(@"\A", RunStatus.Succeeded, Now.AddHours(-2), TimeSpan.FromSeconds(10)),
            Run(@"\A", RunStatus.Succeeded, Now.AddHours(-3), TimeSpan.FromSeconds(10)),
            Run(@"\B", RunStatus.Failed, Now.AddHours(-1), TimeSpan.FromSeconds(1), 3),
            Run(@"\C", RunStatus.CouldNotStart, Now.AddHours(-1), TimeSpan.Zero, 0x80070002),
            Run(@"\D", RunStatus.TimedOut, Now.AddHours(-4), TimeSpan.FromHours(1)),
            Run(@"\E", RunStatus.Stopped, Now.AddHours(-1), TimeSpan.FromSeconds(5)),
            Run(@"\F", RunStatus.Running, Now.AddMinutes(-1), TimeSpan.Zero)
        ]);

        Assert.Equal((3, 3), (figures.SucceededRuns, figures.FailedRuns));
        Assert.Equal(50, figures.HealthScore);
        Assert.Equal(50, figures.HealthValue);
    }

    [Fact]
    public void WithoutAFinishedRun_ThereIsNoHealthScore_RatherThanAPerfectOne()
    {
        var figures = Of([Run(@"\A", RunStatus.Running, Now.AddMinutes(-1), TimeSpan.Zero)]);

        Assert.Null(figures.HealthScore);
        Assert.Equal((HealthLevel.None, "-", 0.0), (figures.Health, figures.HealthText, figures.HealthValue));
    }

    [Theory]
    [InlineData(10, 0, HealthLevel.Good)]
    [InlineData(9, 1, HealthLevel.Good)]
    [InlineData(8, 2, HealthLevel.Fair)]
    [InlineData(7, 3, HealthLevel.Fair)]
    [InlineData(6, 4, HealthLevel.Poor)]
    public void Health_IsGoodFromNinetyPercent_FairFromSeventy_AndPoorBelow(int succeeded, int failed, HealthLevel level)
    {
        var runs = Enumerable.Range(1, succeeded).Select(hour => Run(@"\A", RunStatus.Succeeded, Now.AddHours(-hour), TimeSpan.FromSeconds(1)))
            .Concat(Enumerable.Range(1, failed).Select(hour => Run(@"\B", RunStatus.Failed, Now.AddHours(-hour), TimeSpan.FromSeconds(1), 1)));

        Assert.Equal(level, Of(runs).Health);
    }

    [Fact]
    public void ARun_Counts_WhenItEndedInTheLastSevenDays_OrIsStillGoingOn()
    {
        var figures = Of(
        [
            Run(@"\A", RunStatus.Succeeded, Now.AddHours(-2), TimeSpan.FromSeconds(10)),
            Run(@"\A", RunStatus.Succeeded, Now.AddHours(-30), TimeSpan.FromSeconds(10)),
            Run(@"\A", RunStatus.Failed, Now.AddDays(-7).AddMinutes(-10), TimeSpan.FromMinutes(20), 1),
            Run(@"\A", RunStatus.Succeeded, Now.AddDays(-8), TimeSpan.FromSeconds(10)),
            Run(@"\B", RunStatus.Running, Now.AddDays(-9), TimeSpan.Zero),
            Run(@"\C", RunStatus.CouldNotStart, Now.AddHours(-1), TimeSpan.Zero, 0x80070002)
        ]);

        Assert.Equal(5, figures.RunsInPeriod);
        Assert.Equal(3, figures.RunsLastDay);
        Assert.Equal((2, 2), (figures.SucceededRuns, figures.FailedRuns));
    }

    [Fact]
    public void Durations_AreAveragedOverFinishedRuns_AndTheLongestIsNamed_HoweverLongItTook()
    {
        var figures = Of(
        [
            Run(@"\Jobs\Quick", RunStatus.Succeeded, Now.AddHours(-1), TimeSpan.FromSeconds(30)),
            Run(@"\Jobs\Slow", RunStatus.Failed, Now.AddDays(-2), TimeSpan.FromHours(30), 1),
            Run(@"\Jobs\Blink", RunStatus.Succeeded, Now.AddHours(-2), TimeSpan.FromMilliseconds(200)),
            Run(@"\Jobs\Going", RunStatus.Running, Now.AddHours(-5), TimeSpan.Zero)
        ]);

        Assert.Equal(("1d 6h", "Slow"), (figures.LongestDuration, figures.LongestTask));
        Assert.Equal("10h", figures.AverageDuration);
    }

    [Theory]
    [InlineData(0.4, "< 1s")]
    [InlineData(95, "1m 35s")]
    [InlineData(3599.6, "1h")]
    [InlineData(2 * 3600 + 5 * 60 + 29, "2h 5m")]
    [InlineData(30 * 3600 + 3 * 60 + 12, "1d 6h")]
    public void ADuration_IsGivenInItsTwoLargestUnits(double seconds, string expected)
    {
        var figures = Of([Run(@"\A", RunStatus.Succeeded, Now.AddDays(-2), TimeSpan.FromSeconds(seconds))]);

        Assert.Equal(expected, figures.LongestDuration);
    }

    [Fact]
    public void WithoutFinishedRuns_DurationsAreDashes()
    {
        var figures = Of([Run(@"\A", RunStatus.Running, Now.AddMinutes(-1), TimeSpan.Zero)]);

        Assert.Equal(("-", "-", "", ""), (figures.AverageDuration, figures.LongestDuration, figures.LongestTask, figures.LongestRun));
    }

    [Fact]
    public void EveryHourOfTheDayHasACell_ShadedByItsShareOfTheBusiestHour_OnTheUsersClock()
    {
        // 01:10 UTC is 03:10 where the user is.
        var threeAtNight = new DateTimeOffset(2031, 5, 8, 1, 10, 0, TimeSpan.Zero);
        var figures = Of(
        [
            Run(@"\A", RunStatus.Succeeded, threeAtNight, TimeSpan.FromSeconds(1)),
            Run(@"\A", RunStatus.Succeeded, threeAtNight.AddDays(-1), TimeSpan.FromSeconds(1)),
            Run(@"\B", RunStatus.Failed, Now.AddMinutes(-20), TimeSpan.FromSeconds(1), 1)
        ]);

        Assert.Equal(24, figures.Hours.Count);
        Assert.Equal((3, 2, 1.0), (figures.Hours[3].Hour, figures.Hours[3].Runs, figures.Hours[3].Shade));
        Assert.Equal(1, figures.Hours[14].Runs);
        Assert.Equal(0.6, figures.Hours[14].Shade, precision: 9);
        Assert.Equal((0, 0.0), (figures.Hours[1].Runs, figures.Hours[1].Shade));
        Assert.Equal("03:00", figures.BusiestHour);
    }

    [Fact]
    public void AnHourWithOneRunAmongMany_IsStillShaded_SoThatItCannotPassForOneWithout()
    {
        // A hundred runs between 14:05 and 14:30, and one at 09:30.
        var runs = Enumerable.Range(1, 100).Select(quarter => Run(@"\Often", RunStatus.Succeeded, Now.AddSeconds(-15 * quarter), TimeSpan.FromSeconds(1)))
            .Append(Run(@"\Rarely", RunStatus.Succeeded, Now.AddHours(-5), TimeSpan.FromSeconds(1)));

        HourCell rare = Of(runs).Hours[Now.AddHours(-5).Hour];

        Assert.Equal(1, rare.Runs);
        Assert.InRange(rare.Shade, 0.2, 0.21);
    }

    [Fact]
    public void WithoutRuns_NoHourIsShaded_AndNoneIsTheBusiest()
    {
        var figures = Of([]);

        Assert.All(figures.Hours, hour => Assert.Equal(0, hour.Shade));
        Assert.Equal("-", figures.BusiestHour);
    }

    [Fact]
    public void TheLog_IsNewestFirst_AndNarrowedBeforeItIsCutToLength()
    {
        var runs = Enumerable.Range(1, DashboardFigures.MostLogEntries + 10)
            .Select(minute => Run(@"\Often", RunStatus.Succeeded, Now.AddMinutes(-minute), TimeSpan.FromSeconds(1)))
            .Append(Run(@"\Rarely", RunStatus.Failed, Now.AddDays(-3), TimeSpan.FromSeconds(1), 7))
            .Append(Run(@"\Going", RunStatus.Running, Now.AddSeconds(-5), TimeSpan.Zero))
            .ToList();

        var all = Of(runs);
        Assert.Equal(DashboardFigures.MostLogEntries, all.Log.Count);
        Assert.Equal((@"\Going", "Going", LogOutcome.Running, "RunStatus.Running"), (all.Log[0].TaskPath, all.Log[0].TaskName, all.Log[0].Outcome, all.Log[0].Status));
        Assert.DoesNotContain(all.Log, entry => entry.TaskPath == @"\Rarely");

        var failed = Assert.Single(Of(runs, filter: LogFilter.Failed).Log);
        Assert.Equal((@"\Rarely", LogOutcome.Failed, "Details.History.ResultFormat"), (failed.TaskPath, failed.Outcome, failed.Detail));
    }

    [Fact]
    public void ARunThatEnded_ShowsHowLongItTook_UnlessItReturnedAnError()
    {
        var figures = Of(
        [
            Run(@"\A", RunStatus.Succeeded, Now.AddMinutes(-10), TimeSpan.FromSeconds(95)),
            Run(@"\B", RunStatus.Succeeded, Now.AddMinutes(-20), TimeSpan.FromMilliseconds(300)),
            Run(@"\C", RunStatus.TimedOut, Now.AddMinutes(-90), TimeSpan.FromHours(1)),
            Run(@"\D", RunStatus.Failed, Now.AddMinutes(-100), TimeSpan.FromSeconds(5), 3),
            Run(@"\E", RunStatus.Running, Now.AddMinutes(-200), TimeSpan.Zero)
        ]);

        Assert.Equal(["1m 35s", "< 1s", "1h", "Details.History.ResultFormat", ""], figures.Log.Select(entry => entry.Detail));
    }

    [Fact]
    public void StartsASnoozeRefused_AreLoggedAmongTheRuns_WithWhoAskedForThem()
    {
        var figures = Of(
            [Run(@"\Backup", RunStatus.Succeeded, Now.AddMinutes(-30), TimeSpan.FromSeconds(1))],
            [
                new SuppressedRun(Now.AddMinutes(-10), @"\Backup", RunOrigin.Manual),
                new SuppressedRun(Now.AddHours(-1), @"\Report", RunOrigin.CompletionAction),
                new SuppressedRun(Now.AddDays(-8), @"\Report", RunOrigin.Manual)
            ]);

        Assert.Equal([@"\Backup", @"\Backup", @"\Report"], figures.Log.Select(entry => entry.TaskPath));
        Assert.Equal(
            [(LogOutcome.Snoozed, "Dashboard.Status.Snoozed", "Dashboard.Refused.Manual"), (LogOutcome.Snoozed, "Dashboard.Status.Snoozed", "Dashboard.Refused.CompletionAction")],
            figures.Log.Where(entry => entry.Outcome == LogOutcome.Snoozed).Select(entry => (entry.Outcome, entry.Status, entry.Detail)));
        Assert.Equal(1, figures.RunsInPeriod);
    }

    [Theory]
    [InlineData(LogFilter.All, new[] { @"\Refused", @"\Stopped", @"\TimedOut", @"\CouldNotStart", @"\Failed", @"\Succeeded" })]
    [InlineData(LogFilter.Succeeded, new[] { @"\Succeeded" })]
    [InlineData(LogFilter.Failed, new[] { @"\TimedOut", @"\CouldNotStart", @"\Failed" })]
    [InlineData(LogFilter.Snoozed, new[] { @"\Refused" })]
    public void TheLogFilter_KeepsWhatTurnedOutThatWay(LogFilter filter, string[] listed)
    {
        var runs = new[]
        {
            Run(@"\Succeeded", RunStatus.Succeeded, Now.AddMinutes(-60), TimeSpan.FromSeconds(1)),
            Run(@"\Failed", RunStatus.Failed, Now.AddMinutes(-50), TimeSpan.FromSeconds(1), 1),
            Run(@"\CouldNotStart", RunStatus.CouldNotStart, Now.AddMinutes(-40), TimeSpan.Zero, 0x80070002),
            Run(@"\TimedOut", RunStatus.TimedOut, Now.AddMinutes(-35), TimeSpan.FromMinutes(5)),
            Run(@"\Stopped", RunStatus.Stopped, Now.AddMinutes(-25), TimeSpan.FromMinutes(5))
        };

        var figures = Of(runs, [new SuppressedRun(Now.AddMinutes(-10), @"\Refused", RunOrigin.Manual)], filter);

        Assert.Equal(listed, figures.Log.Select(entry => entry.TaskPath));
    }

    [Fact]
    public void TheTime_OfAnEntryOfToday_IsGivenWithoutItsDate()
    {
        var figures = Of(
        [
            Run(@"\Today", RunStatus.Succeeded, Now.AddHours(-1), TimeSpan.FromSeconds(1)),
            Run(@"\Yesterday", RunStatus.Succeeded, Now.AddDays(-1), TimeSpan.FromSeconds(1))
        ]);

        Assert.Equal(new DateTime(2031, 5, 9, 13, 30, 1).ToString("t", CultureInfo.CurrentCulture), figures.Log[0].Time);
        Assert.Equal(new DateTime(2031, 5, 8, 14, 30, 1).ToString("g", CultureInfo.CurrentCulture), figures.Log[1].Time);
    }

    [Fact]
    public void TheRunningTasks_AreListedLastStartedFirst_AndTheNextFiveToRun_SoonestFirst()
    {
        var tasks = Enumerable.Range(1, 7)
            .Select(number => new TaskRow($@"\Task{number}") { IsEnabled = true, State = TaskRowState.Ready, NextRunTime = new DateTime(2031, 5, 10).AddHours(8 - number) })
            .Append(new TaskRow(@"\Off") { IsEnabled = false, State = TaskRowState.Disabled, NextRunTime = new DateTime(2031, 5, 9, 15, 0, 0) })
            .Append(new TaskRow(@"\Busy") { IsEnabled = true, State = TaskRowState.Running, LastRunTime = new DateTime(2031, 5, 9, 14, 0, 0) })
            .Append(new TaskRow(@"\Busier") { IsEnabled = true, State = TaskRowState.Running, LastRunTime = new DateTime(2031, 5, 9, 14, 20, 0) })
            .ToList();

        var figures = DashboardFigures.From(tasks, [], [], LogFilter.All, Now, Zone);

        Assert.Equal([@"\Busier", @"\Busy"], figures.Running.Select(line => line.TaskPath));
        Assert.Equal([@"\Task7", @"\Task6", @"\Task5", @"\Task4", @"\Task3"], figures.Upcoming.Select(line => line.TaskPath));
        Assert.False(figures.NothingRunning || figures.NothingUpcoming);
        Assert.True(figures.LogIsEmpty);
    }

    [Fact]
    public void ALineOrAnEntry_IsAnnouncedByItsTaskName()
    {
        Assert.Equal("Backup", new TaskLine(@"\Jobs\Backup", "14:00").ToString());
        Assert.Equal("Backup, Succeeded, 14:00", new LogEntry("1", @"\Jobs\Backup", LogOutcome.Succeeded, "14:00", "Succeeded", "2s").ToString());
    }
}

/// <summary>The notes under the figures, with the texts of the app in place, so that each figure can be seen to go where its text has it.</summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class DashboardNotesTests : IDisposable
{
    public DashboardNotesTests() => Loc.Use(key => key switch
    {
        "Dashboard.Tasks.Note" => "{0} enabled, {1} running",
        "Dashboard.Health.Note" => "{0} succeeded, {1} failed",
        "Dashboard.Runs.Note" => "{0} in the last 24 hours",
        "Dashboard.Duration.Note" => "Longest: {0} ({1})",
        _ => null
    });

    public void Dispose() => Loc.Use(static _ => null);

    [Fact]
    public void TheNotes_PutEachFigureInItsPlace()
    {
        var now = new DateTimeOffset(2031, 5, 9, 12, 0, 0, TimeSpan.Zero);
        var tasks = new[]
        {
            new TaskRow(@"\Backup") { IsEnabled = true, State = TaskRowState.Running },
            new TaskRow(@"\Report") { IsEnabled = true, State = TaskRowState.Ready },
            new TaskRow(@"\Old") { IsEnabled = true, State = TaskRowState.Ready }
        };
        var runs = new[]
        {
            DashboardFiguresTests.Run(@"\Backup", RunStatus.Failed, now.AddDays(-2), TimeSpan.FromMinutes(5), 1),
            DashboardFiguresTests.Run(@"\Report", RunStatus.Succeeded, now.AddDays(-3), TimeSpan.FromSeconds(30)),
            DashboardFiguresTests.Run(@"\Report", RunStatus.Succeeded, now.AddHours(-1), TimeSpan.FromSeconds(30))
        };

        var figures = DashboardFigures.From(tasks, runs, [], LogFilter.All, now, TimeZoneInfo.Utc);

        Assert.Equal("3 enabled, 1 running", figures.TasksNote);
        Assert.Equal("2 succeeded, 1 failed", figures.HealthNote);
        Assert.Equal("1 in the last 24 hours", figures.RunsNote);
        Assert.Equal("Longest: 5m (Backup)", figures.LongestRun);
    }
}
