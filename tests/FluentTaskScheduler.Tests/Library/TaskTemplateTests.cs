using System.Globalization;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Scheduling;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;

namespace FluentTaskScheduler.Tests.Library;

public sealed class TaskTemplateTests
{
    private static readonly DateTime Noon = new(2030, 5, 6, 12, 0, 0);

    private static TaskTemplate Daily(TimeOnly? at) => new() { Name = "Tidy up", Command = "tidy.exe", At = at };

    private static DateTime Time(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    [Fact]
    public void ATemplate_BecomesANewTaskWithItsActionTriggerAndSettings_AndNothingElseChanged()
    {
        var template = new TaskTemplate
        {
            Name = "Tidy up",
            Description = "Tidies up.",
            Command = "tidy.exe",
            Arguments = "/all",
            Trigger = new TriggerDraft { Type = TriggerType.Weekly, DaysOfWeek = DaysOfTheWeek.Friday },
            At = new TimeOnly(18, 30),
            RunElevated = true,
            RunIfMissed = true,
            OnlyWhenIdle = true,
            OnlyOnAcPower = true,
            Category = "Maintenance",
            Tags = ["cleanup"]
        };

        TaskDraft draft = template.ToDraft(Noon);

        Assert.Equal(new TaskDraft
        {
            Name = "Tidy up",
            Description = "Tidies up.",
            Author = Environment.UserName,
            Category = "Maintenance",
            Tags = ["cleanup"],
            HighestPrivileges = true,
            RunIfMissed = true,
            RunOnlyIfIdle = true,
            RunOnlyOnAcPower = true,
            Triggers = [new TriggerDraft { Type = TriggerType.Weekly, DaysOfWeek = DaysOfTheWeek.Friday, Start = new DateTime(2030, 5, 6, 18, 30, 0) }],
            Actions = [new ActionDraft { Path = "tidy.exe", Arguments = "/all" }]
        }, draft);
    }

    [Theory]
    [InlineData(12, 1, "2030-05-06 12:01")]
    [InlineData(23, 59, "2030-05-06 23:59")]
    [InlineData(12, 0, "2030-05-07 12:00")]
    [InlineData(3, 0, "2030-05-07 03:00")]
    public void ATimeOfDay_FirstComesUpToday_OrTomorrowIfTodayItHasPassed(int hour, int minute, string expectedStart)
    {
        TriggerDraft trigger = Assert.Single(Daily(new TimeOnly(hour, minute)).ToDraft(Noon).Triggers);

        Assert.Equal(Time(expectedStart), trigger.Start);
    }

    [Theory]
    [InlineData("2030-05-06 14:37:12", "2030-05-06 15:00")]
    [InlineData("2030-05-06 15:00:00", "2030-05-06 16:00")]
    [InlineData("2030-12-31 23:10:00", "2031-01-01 00:00")]
    public void WithoutATimeOfDay_TheTaskStartsOutAtTheNextFullHour(string now, string expectedStart)
    {
        TriggerDraft trigger = Assert.Single(Daily(at: null).ToDraft(Time(now)).Triggers);

        Assert.Equal(Time(expectedStart), trigger.Start);
    }

    [Fact]
    public void TheStart_CarriesNoTimeZone_SoTheTaskKeepsItsTimeOfDayAcrossDaylightSaving()
    {
        var localNow = DateTime.SpecifyKind(Noon, DateTimeKind.Local);

        TriggerDraft trigger = Assert.Single(Daily(new TimeOnly(3, 0)).ToDraft(localNow).Triggers);

        Assert.Equal(DateTimeKind.Unspecified, trigger.Start!.Value.Kind);
    }

    [Fact]
    public void ATriggerThatFiresOnAnEvent_GetsNoStartTime()
    {
        var template = new TaskTemplate { Name = "Hello", Command = "hello.exe", Trigger = new TriggerDraft { Type = TriggerType.AtLogon }, At = new TimeOnly(3, 0) };

        Assert.Equal([new TriggerDraft { Type = TriggerType.AtLogon }], template.ToDraft(Noon).Triggers);
    }

    [Theory]
    [InlineData("Disk Optimize (Defrag / TRIM)", "Disk Optimize (Defrag - TRIM)")]
    [InlineData("a\\b:c*d?e\"f<g>h|i", "a-b-c-d-e-f-g-h-i")]
    [InlineData("  Backup, nightly.  ", "Backup, nightly")]
    public void ANameTaskSchedulerWouldRefuse_BecomesOneItAccepts(string name, string taskName)
    {
        Assert.Equal(taskName, (Daily(at: null) with { Name = name }).ToDraft(Noon).Name);
    }
}
