using System.Reflection;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.ViewModels;

public class TriggerViewModelTests
{
    private static readonly DateTime Start = new(2031, 5, 6, 7, 8, 9);

    public static TheoryData<TriggerDraft> StoredTriggers => new()
    {
        new TriggerDraft { Type = TriggerType.Once, SourceIndex = 0, Start = Start, RandomDelay = TimeSpan.FromSeconds(90) },
        new TriggerDraft { Type = TriggerType.Daily, SourceIndex = 1, Start = Start, End = Start.AddYears(1), DaysInterval = 3, Enabled = false },
        new TriggerDraft { Type = TriggerType.Daily, SourceIndex = 0, Start = Start, RepetitionInterval = TimeSpan.FromMinutes(7.5), RepetitionDuration = TimeSpan.FromHours(5) },
        new TriggerDraft { Type = TriggerType.Weekly, SourceIndex = 2, Start = Start, WeeksInterval = 2, DaysOfWeek = DaysOfTheWeek.Tuesday | DaysOfTheWeek.Sunday },
        new TriggerDraft { Type = TriggerType.Monthly, SourceIndex = 0, Start = Start, DaysOfMonth = 0b101, LastDayOfMonth = true, Months = MonthsOfTheYear.March | MonthsOfTheYear.December },
        new TriggerDraft { Type = TriggerType.Monthly, SourceIndex = 0, Start = Start, MonthlyByWeekday = true, Weeks = WhichWeek.SecondWeek | WhichWeek.LastWeek, DaysOfWeek = DaysOfTheWeek.Friday },
        new TriggerDraft { Type = TriggerType.AtLogon, SourceIndex = 0 },
        new TriggerDraft { Type = TriggerType.AtLogon, SourceIndex = 0, Start = Start },
        new TriggerDraft { Type = TriggerType.AtStartup, SourceIndex = 0, End = Start },
        new TriggerDraft { Type = TriggerType.OnIdle, SourceIndex = 0 },
        new TriggerDraft { Type = TriggerType.Event, SourceIndex = 0, EventSubscription = EventQuery.Build("Application", "VSS", 8224) },
        new TriggerDraft { Type = TriggerType.Event, SourceIndex = 0, EventSubscription = "<QueryList><Query Id=\"0\"><Select Path=\"System\">*[System[(Level=1 or Level=2)]]</Select></Query></QueryList>" },
        new TriggerDraft { Type = TriggerType.SessionStateChange, SourceIndex = 0, SessionChange = TaskSessionStateChangeType.RemoteDisconnect },
        new TriggerDraft { Type = TriggerType.Unsupported, SourceIndex = 4, UnsupportedSummary = "Registration" }
    };

    [Theory]
    [MemberData(nameof(StoredTriggers))]
    public void ATriggerNobodyTouched_BuildsIntoExactlyWhatItWasLoadedFrom(TriggerDraft stored)
    {
        var trigger = new TriggerViewModel(stored);

        Assert.True(trigger.TryBuild(out TriggerDraft built, out string? problem), problem);
        Assert.Equal(stored, built);
    }

    [Fact]
    public void TheTimeOfDay_CanBeSet()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, SourceIndex = 0, Start = Start });

        trigger.StartTime = new TimeSpan(21, 45, 0);

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(new DateTime(2031, 5, 6, 21, 45, 0), built.Start);
        Assert.Equal(DateTimeKind.Unspecified, built.Start!.Value.Kind);
        Assert.Equal(TriggerType.Daily, built.Type);
    }

    [Fact]
    public void AMomentKeptWithItsTimeZone_KeepsItWhenChanged()
    {
        var zoned = DateTime.SpecifyKind(Start, DateTimeKind.Local);
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, SourceIndex = 0, Start = zoned, End = zoned.AddYears(1) });

        trigger.StartTime = new TimeSpan(21, 45, 0);
        trigger.EndDate = new DateTimeOffset(new DateTime(2033, 3, 4));

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(new DateTime(2031, 5, 6, 21, 45, 0), built.Start);
        Assert.Equal(DateTimeKind.Local, built.Start!.Value.Kind);
        Assert.Equal(new DateTime(2033, 3, 4, 7, 8, 0), built.End);
        Assert.Equal(DateTimeKind.Local, built.End!.Value.Kind);
    }

    [Fact]
    public void TheDate_CanBeSet_AndTheTimeStays()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Once, SourceIndex = 0, Start = Start });

        trigger.StartDate = new DateTimeOffset(new DateTime(2032, 1, 2));

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(new DateTime(2032, 1, 2, 7, 8, 0), built.Start);
    }

    [Fact]
    public void ChoosingWeekly_MakesAWeeklyTrigger_WithTheChosenDaysAndTime()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start });

        trigger.TypeIndex = TriggerViewModel.Types.ToList().IndexOf(TriggerType.Weekly);
        trigger.Weekdays = (int)(DaysOfTheWeek.Monday | DaysOfTheWeek.Thursday);
        trigger.WeeksInterval = 2;
        trigger.StartTime = new TimeSpan(6, 30, 0);

        Assert.True(trigger.IsWeekly);
        Assert.False(trigger.IsDaily);
        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(TriggerType.Weekly, built.Type);
        Assert.Equal(DaysOfTheWeek.Monday | DaysOfTheWeek.Thursday, built.DaysOfWeek);
        Assert.Equal(2, built.WeeksInterval);
        Assert.Equal(new TimeSpan(6, 30, 0), built.Start!.Value.TimeOfDay);
    }

    [Theory]
    [InlineData(TriggerType.Once, true)]
    [InlineData(TriggerType.Daily, true)]
    [InlineData(TriggerType.Weekly, true)]
    [InlineData(TriggerType.Monthly, true)]
    [InlineData(TriggerType.AtLogon, false)]
    [InlineData(TriggerType.AtStartup, false)]
    [InlineData(TriggerType.OnIdle, false)]
    [InlineData(TriggerType.Event, false)]
    [InlineData(TriggerType.SessionStateChange, false)]
    public void OnlyTriggersThatFireAtATimeOfDay_ShowTheSchedule(TriggerType type, bool hasSchedule)
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = type });

        Assert.Equal(hasSchedule, trigger.HasSchedule);
        Assert.Equal(type, trigger.Type);
    }

    [Theory]
    [InlineData(TriggerType.AtLogon)]
    [InlineData(TriggerType.AtStartup)]
    [InlineData(TriggerType.OnIdle)]
    [InlineData(TriggerType.Event)]
    [InlineData(TriggerType.SessionStateChange)]
    public void ANewTriggerTurnedIntoOneWithoutATimeOfDay_IsActiveAtOnce(TriggerType type)
    {
        var trigger = new TriggerViewModel(NewTask.Trigger(new DateTime(2031, 5, 6, 10, 20, 0)))
        {
            TypeIndex = TriggerViewModel.Types.ToList().IndexOf(type),
            EventLog = "System"
        };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out string? problem), problem);
        Assert.Null(built.Start);
    }

    [Fact]
    public void ATriggerWithoutATimeOfDay_KeepsWhenItBecomesActive_AcrossSuchTypes()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.AtLogon, SourceIndex = 0, Start = Start })
        {
            TypeIndex = TriggerViewModel.Types.ToList().IndexOf(TriggerType.OnIdle)
        };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(Start, built.Start);
    }

    [Fact]
    public void EveryTypeTheEditorOffers_CanBeChosen_AndIsWhatGetsBuilt()
    {
        for (int index = 0; index < TriggerViewModel.Types.Count; index++)
        {
            var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start }) { TypeIndex = index, EventLog = "System" };

            Assert.True(trigger.TryBuild(out TriggerDraft built, out string? problem), problem);
            Assert.Equal(TriggerViewModel.Types[index], built.Type);
        }
    }

    /// <summary>The form shows and hides its parts by these flags, so it has to hear of every change.</summary>
    [Fact]
    public void ChangingTheType_AnnouncesEveryPartOfTheFormThatAppearsOrDisappears()
    {
        PropertyInfo[] flags = typeof(TriggerViewModel).GetProperties().Where(property => property.PropertyType == typeof(bool)).ToArray();

        foreach (bool byWeekday in new[] { false, true })
        {
            foreach (TriggerType from in TriggerViewModel.Types)
            {
                foreach (TriggerType to in TriggerViewModel.Types)
                {
                    var trigger = new TriggerViewModel(new TriggerDraft { Type = from, Start = Start, MonthlyByWeekday = byWeekday });
                    var before = flags.ToDictionary(flag => flag.Name, flag => flag.GetValue(trigger));
                    var announced = new List<string?>();
                    trigger.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

                    trigger.TypeIndex = TriggerViewModel.Types.ToList().IndexOf(to);

                    foreach (PropertyInfo flag in flags.Where(flag => !Equals(flag.GetValue(trigger), before[flag.Name])))
                        Assert.True(announced.Contains(flag.Name), $"From {from} to {to}: {flag.Name} changed without notice.");
                }
            }
        }
    }

    [Fact]
    public void AnExpiry_CanBeAddedAndRemoved()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start });

        trigger.Expires = true;
        trigger.EndDate = new DateTimeOffset(new DateTime(2033, 3, 4));
        trigger.EndTime = new TimeSpan(12, 0, 0);
        Assert.True(trigger.TryBuild(out TriggerDraft withEnd, out _));
        Assert.Equal(new DateTime(2033, 3, 4, 12, 0, 0), withEnd.End);

        trigger.Expires = false;
        Assert.True(trigger.TryBuild(out TriggerDraft withoutEnd, out _));
        Assert.Null(withoutEnd.End);
    }

    [Theory]
    [InlineData("15m", "", 15, 0)]
    [InlineData("1h", "1d", 60, 1440)]
    [InlineData(" 90m ", "12h", 90, 720)]
    public void RepetitionIsTypedAsDurations(string every, string duration, int everyMinutes, int forMinutes)
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start })
        {
            RepetitionInterval = every,
            RepetitionDuration = duration
        };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(TimeSpan.FromMinutes(everyMinutes), built.RepetitionInterval);
        Assert.Equal(TimeSpan.FromMinutes(forMinutes), built.RepetitionDuration);
    }

    [Fact]
    public void ClearingTheRepetition_TurnsItOff()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start, RepetitionInterval = TimeSpan.FromMinutes(5) })
        {
            RepetitionInterval = ""
        };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(TimeSpan.Zero, built.RepetitionInterval);
    }

    [Theory]
    [InlineData("often", "", "", "Editor.Error.RepetitionText")]
    [InlineData("5m", "a while", "", "Editor.Error.RepetitionText")]
    [InlineData("", "", "soon", "Editor.Error.RandomDelayText")]
    public void ADurationThatCannotBeRead_IsReported_NotGuessed(string every, string duration, string delay, string expectedProblem)
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start })
        {
            RepetitionInterval = every,
            RepetitionDuration = duration,
            RandomDelay = delay
        };

        Assert.False(trigger.TryBuild(out _, out string? problem));
        Assert.Equal(expectedProblem, problem);
    }

    [Fact]
    public void AnEventTrigger_IsBuiltFromLogSourceAndId()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Event })
        {
            EventLog = "Application",
            EventSource = "MyApp",
            EventId = "1000"
        };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.True(EventQuery.TryParse(built.EventSubscription, out string log, out string source, out int? id));
        Assert.Equal(("Application", "MyApp", 1000), (log, source, id));
    }

    [Fact]
    public void AnEventIdThatIsNotANumber_IsReported()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Event }) { EventLog = "System", EventId = "seven" };

        Assert.False(trigger.TryBuild(out _, out string? problem));
        Assert.Equal("Editor.Error.EventIdText", problem);
    }

    [Fact]
    public void AnEventSubscriptionBeyondTheThreeFields_IsEditedAsXml_AndKeptVerbatim()
    {
        const string query = "<QueryList><Query Id=\"0\"><Select Path=\"System\">*[System[(Level=1 or Level=2)]]</Select></Query></QueryList>";
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Event, EventSubscription = query });

        Assert.True(trigger.CustomEventQuery);
        Assert.Equal(query, trigger.EventQueryXml);

        trigger.EventQueryXml = query.Replace("Level=2", "Level=3");
        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Contains("Level=3", built.EventSubscription);
    }

    [Fact]
    public void TheSessionChange_CanBeAnyOfTheSix()
    {
        foreach (TaskSessionStateChangeType change in Enum.GetValues<TaskSessionStateChangeType>())
        {
            int index = TriggerViewModel.SessionChanges.ToList().IndexOf(change);
            Assert.True(index >= 0, $"{change} is not offered.");

            var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.SessionStateChange }) { SessionChangeIndex = index };
            Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
            Assert.Equal(change, built.SessionChange);
        }
    }

    [Fact]
    public void AnEmptyNumberBox_KeepsTheStoredInterval()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start, DaysInterval = 4 }) { DaysInterval = double.NaN };

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(4, built.DaysInterval);
    }

    [Fact]
    public void ATriggerTypeTheEditorDoesNotKnow_IsShownButNotEditable()
    {
        var stored = new TriggerDraft { Type = TriggerType.Unsupported, SourceIndex = 2, UnsupportedSummary = "Registration" };
        var trigger = new TriggerViewModel(stored) { Enabled = false, TypeIndex = 3 };

        Assert.False(trigger.IsEditable);
        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(stored, built);
    }

    [Fact]
    public void TheSummary_FollowsTheInput()
    {
        var trigger = new TriggerViewModel(new TriggerDraft { Type = TriggerType.Daily, Start = Start });
        var changed = new List<string?>();
        trigger.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        string before = trigger.Summary;

        trigger.TypeIndex = TriggerViewModel.Types.ToList().IndexOf(TriggerType.AtStartup);

        Assert.Contains(nameof(TriggerViewModel.Summary), changed);
        Assert.NotEqual(before, trigger.Summary);
    }
}
