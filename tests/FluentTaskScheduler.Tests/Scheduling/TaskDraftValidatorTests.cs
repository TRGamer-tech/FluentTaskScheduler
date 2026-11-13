using FluentTaskScheduler.Scheduling;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;
using MonthsOfTheYear = Microsoft.Win32.TaskScheduler.MonthsOfTheYear;
using WhichWeek = Microsoft.Win32.TaskScheduler.WhichWeek;

namespace FluentTaskScheduler.Tests.Scheduling;

public class TaskDraftValidatorTests
{
    private static readonly DateTime Start = new(2031, 5, 6, 7, 8, 0);

    private static TaskDraft Valid() => new()
    {
        Name = "Backup",
        Actions = [new ActionDraft { Path = "backup.exe" }],
        Triggers = [new TriggerDraft { Type = TriggerType.Daily, Start = Start }]
    };

    private static string[] Problems(TaskDraft draft) => TaskDraftValidator.Validate(draft).Select(problem => problem.Key).ToArray();

    [Fact]
    public void AnOrdinaryTask_HasNoProblems()
    {
        Assert.Empty(Problems(Valid()));
        Assert.Empty(Problems(Valid() with { Triggers = [] }));
    }

    [Theory]
    [InlineData("", "Dialog.Error.EmptyName")]
    [InlineData("   ", "Dialog.Error.EmptyName")]
    [InlineData(@"a\b", "Editor.Error.NameCharacters")]
    [InlineData("a/b", "Editor.Error.NameCharacters")]
    [InlineData("what?", "Editor.Error.NameCharacters")]
    [InlineData("a:b", "Editor.Error.NameCharacters")]
    public void TheName_MustBeThere_AndFreeOfPathCharacters(string name, string expected)
    {
        Assert.Equal([expected], Problems(Valid() with { Name = name }));
        Assert.Equal(expected, TaskDraftValidator.CheckName(name)?.Key);
    }

    [Fact]
    public void ATaskNeedsAnAction_AndEveryActionAProgram()
    {
        Assert.Equal(["Editor.Error.NoAction"], Problems(Valid() with { Actions = [] }));

        var problem = Assert.Single(TaskDraftValidator.Validate(Valid() with { Actions = [new ActionDraft { Path = "a.exe" }, new ActionDraft { Path = " " }] }));
        Assert.Equal(new DraftProblem("Editor.Error.ActionWithoutProgram", "2"), problem);
    }

    [Fact]
    public void AnActionTheEditorOnlyShows_NeedsNoProgram()
    {
        Assert.Empty(Problems(Valid() with { Actions = [new ActionDraft { UnsupportedSummary = "ComHandler" }] }));
    }

    public static TheoryData<TriggerDraft, string> BrokenTriggers => new()
    {
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, DaysInterval = 0 }, "Editor.Error.TriggerInterval" },
        { new TriggerDraft { Type = TriggerType.Weekly, Start = Start, WeeksInterval = 0 }, "Editor.Error.TriggerInterval" },
        { new TriggerDraft { Type = TriggerType.Weekly, Start = Start, DaysOfWeek = 0 }, "Editor.Error.TriggerNeedsWeekday" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, Months = 0 }, "Editor.Error.TriggerNeedsMonth" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, DaysOfMonth = 0 }, "Editor.Error.TriggerNeedsDayOfMonth" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, Weeks = 0 }, "Editor.Error.TriggerNeedsWeekAndWeekday" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, DaysOfWeek = 0 }, "Editor.Error.TriggerNeedsWeekAndWeekday" },
        { new TriggerDraft { Type = TriggerType.Event }, "Editor.Error.EventNeedsLog" },
        { new TriggerDraft { Type = TriggerType.Daily }, "Editor.Error.TriggerNeedsStart" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, End = Start }, "Editor.Error.TriggerEndsBeforeStart" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromSeconds(30) }, "Editor.Error.RepetitionInterval" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromDays(40) }, "Editor.Error.RepetitionInterval" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromHours(2), RepetitionDuration = TimeSpan.FromHours(1) }, "Editor.Error.RepetitionDuration" }
    };

    [Theory]
    [MemberData(nameof(BrokenTriggers))]
    public void ATriggerThatCannotFire_IsReportedWithItsNumber(TriggerDraft broken, string expected)
    {
        var problem = Assert.Single(TaskDraftValidator.Validate(Valid() with { Triggers = [new TriggerDraft { Type = TriggerType.AtStartup }, broken] }));

        Assert.Equal(new DraftProblem(expected, "2"), problem);
    }

    public static TheoryData<TriggerDraft> SoundTriggers => new()
    {
        new TriggerDraft { Type = TriggerType.Monthly, Start = Start, DaysOfMonth = 0, LastDayOfMonth = true },
        new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, Weeks = WhichWeek.LastWeek, DaysOfWeek = DaysOfTheWeek.Friday, Months = MonthsOfTheYear.June },
        new TriggerDraft { Type = TriggerType.AtLogon, End = Start },
        new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("System", "", null) },
        new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromMinutes(1) },
        new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromHours(1), RepetitionDuration = TimeSpan.FromHours(1) },
        new TriggerDraft { Type = TriggerType.Unsupported, UnsupportedSummary = "Registration" }
    };

    [Theory]
    [MemberData(nameof(SoundTriggers))]
    public void TriggersAtTheEdgeOfWhatIsAllowed_Pass(TriggerDraft trigger)
    {
        Assert.Empty(Problems(Valid() with { Triggers = [trigger] }));
    }

    [Fact]
    public void ANamedAccount_NeedsAName()
    {
        Assert.Equal(["Editor.Error.UserNameMissing"], Problems(Valid() with { Account = new TaskAccount(TaskAccountKind.User, " ") }));
        Assert.Empty(Problems(Valid() with { Account = new TaskAccount(TaskAccountKind.User, @"PC\someone") }));
        Assert.Empty(Problems(Valid() with { Account = TaskAccount.System }));
    }

    [Fact]
    public void WaitingForIdle_NeedsAtLeastAMinute()
    {
        Assert.Equal(["Editor.Error.IdleDuration"], Problems(Valid() with { RunOnlyIfIdle = true, IdleDuration = TimeSpan.FromSeconds(10) }));
        Assert.Empty(Problems(Valid() with { RunOnlyIfIdle = false, IdleDuration = TimeSpan.Zero }));
    }

    [Fact]
    public void RestartingAfterAFailure_NeedsAnIntervalWindowsAccepts()
    {
        Assert.Equal(["Editor.Error.RestartInterval"], Problems(Valid() with { RestartCount = 3, RestartInterval = TimeSpan.FromSeconds(5) }));
        Assert.Equal(["Editor.Error.RestartInterval"], Problems(Valid() with { RestartCount = 3, RestartInterval = TimeSpan.FromDays(32) }));
        Assert.Empty(Problems(Valid() with { RestartCount = 0, RestartInterval = TimeSpan.Zero }));
    }

    [Fact]
    public void DeletingTheTaskWhenItExpires_NeedsATriggerThatExpires()
    {
        Assert.Equal(["Editor.Error.DeleteNeedsExpiry"], Problems(Valid() with { DeleteWhenExpired = true }));

        var expiring = Valid() with { DeleteWhenExpired = true, Triggers = [new TriggerDraft { Type = TriggerType.Daily, Start = Start, End = Start.AddDays(30) }] };
        Assert.Empty(Problems(expiring));
    }

    [Fact]
    public void SeveralProblems_AreAllListed()
    {
        Assert.Equal(["Dialog.Error.EmptyName", "Editor.Error.NoAction", "Editor.Error.DeleteNeedsExpiry"],
            Problems(new TaskDraft { DeleteWhenExpired = true }));
    }
}
