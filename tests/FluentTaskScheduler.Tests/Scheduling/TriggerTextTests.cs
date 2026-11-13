using System.Globalization;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Library;
using FluentTaskScheduler.Tests.Snoozing;
using FluentTaskScheduler.ViewModels;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;
using MonthsOfTheYear = Microsoft.Win32.TaskScheduler.MonthsOfTheYear;
using TaskSessionStateChangeType = Microsoft.Win32.TaskScheduler.TaskSessionStateChangeType;
using WhichWeek = Microsoft.Win32.TaskScheduler.WhichWeek;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>The summaries with the app's own texts in place, in English unless a test says otherwise.</summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class TriggerTextTests : IDisposable
{
    private static readonly DateTime Start = new(2031, 5, 6, 7, 8, 0);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public TriggerTextTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        UseTextsOf("en-US");
    }

    public void Dispose()
    {
        Loc.Use(static _ => null);
        CultureInfo.CurrentCulture = _culture;
    }

    private static void UseTextsOf(string language)
    {
        var texts = RepoFiles.Texts(language);
        Loc.Use(key => texts.GetValueOrDefault(key));
    }

    public static TheoryData<TriggerDraft, string> Described => new()
    {
        { new TriggerDraft { Type = TriggerType.Once, Start = Start }, "One time: 05/06/2031 07:08" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start }, "Daily at 07:08" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, DaysInterval = 3 }, "Daily (every 3 days) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Weekly, Start = Start, DaysOfWeek = DaysOfTheWeek.Sunday | DaysOfTheWeek.Tuesday }, "Weekly (Tue, Sun) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Weekly, Start = Start, WeeksInterval = 2, DaysOfWeek = DaysOfTheWeek.Monday }, "Weekly (every 2 weeks: Mon) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, DaysOfMonth = 0b100000000000001, LastDayOfMonth = true }, "Monthly (1, 15, last day) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, DaysOfMonth = 1, Months = MonthsOfTheYear.March | MonthsOfTheYear.December }, "Monthly (1; Mar, Dec) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, Weeks = WhichWeek.FirstWeek | WhichWeek.LastWeek, DaysOfWeek = DaysOfTheWeek.Monday }, "Monthly (First, Last: Mon) at 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, Weeks = WhichWeek.SecondWeek, DaysOfWeek = DaysOfTheWeek.Friday, Months = MonthsOfTheYear.January }, "Monthly (Second: Fri; Jan) at 07:08" },
        { new TriggerDraft { Type = TriggerType.AtLogon }, "At logon" },
        { new TriggerDraft { Type = TriggerType.AtStartup }, "At startup" },
        { new TriggerDraft { Type = TriggerType.OnIdle }, "On idle" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("Application", "VSS", 8224) }, "On an event (Application, VSS, 8224)" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("System", "", null) }, "On an event (System)" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = "<QueryList/>" }, "On an event" },
        { new TriggerDraft { Type = TriggerType.SessionStateChange, SessionChange = TaskSessionStateChangeType.RemoteConnect }, "On a session change (Remote connect)" },
        { new TriggerDraft { Type = TriggerType.Unsupported, UnsupportedSummary = "custom" }, "Unsupported trigger (custom)" }
    };

    [Theory]
    [MemberData(nameof(Described))]
    public void ATrigger_IsDescribedByItsTypeAndWhatMattersForIt(TriggerDraft trigger, string expected)
    {
        Assert.Equal(expected, TriggerText.Describe(trigger));
    }

    /// <summary>Where the time goes, the brackets and the separators of lists all come from the texts.</summary>
    [Theory]
    [InlineData("de-DE", "Monatlich (1., 15., letzter Tag; Mär, Dez) um 07:08")]
    [InlineData("ja-JP", "毎月 (3月、12月の1日、15日、最終日) 07:08")]
    [InlineData("zh-CN", "每月（1日、15日、最后一天；三月、十二月）的 07:08")]
    public void ASummary_IsWrittenTheWayItsLanguageWritesOne(string language, string expected)
    {
        UseTextsOf(language);
        var trigger = new TriggerDraft
        {
            Type = TriggerType.Monthly,
            Start = Start,
            DaysOfMonth = 0b100000000000001,
            LastDayOfMonth = true,
            Months = MonthsOfTheYear.March | MonthsOfTheYear.December
        };

        Assert.Equal(expected, TriggerText.Describe(trigger));
    }

    [Fact]
    public void EverySessionChange_HasAName()
    {
        foreach (TaskSessionStateChangeType change in Enum.GetValues<TaskSessionStateChangeType>())
            Assert.StartsWith("Dialog.Sess.", TriggerText.SessionChangeKey(change));
    }

    [Fact]
    public void EveryTypeTheEditorOffers_HasANameOfItsOwn()
    {
        string[] keys = TriggerViewModel.Types.Select(TriggerText.TypeKey).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.DoesNotContain(TriggerText.TypeKey(TriggerType.Unsupported), keys);
    }

    [Theory]
    [MemberData(nameof(RepoFiles.Languages), MemberType = typeof(RepoFiles))]
    public void TheNamesOfTypesAndSessionChanges_ExistInEveryLanguage(string language)
    {
        var texts = RepoFiles.Texts(language);
        string[] keys =
        [
            .. Enum.GetValues<TriggerType>().Select(TriggerText.TypeKey),
            .. Enum.GetValues<TaskSessionStateChangeType>().Select(TriggerText.SessionChangeKey)
        ];

        string[] missing = keys.Where(key => !texts.ContainsKey(key)).ToArray();
        Assert.Empty(missing);
    }
}
