using System.Globalization;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Library;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>
/// <see cref="Loc"/> is one lookup for the whole process. Tests that install texts in it run in
/// this collection, apart from all other tests, and take the texts out again.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizedTextCollection
{
    public const string Name = "Localized text";
}

[Collection(LocalizedTextCollection.Name)]
public sealed class SnoozeTextTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureInfo _cultureBefore = CultureInfo.CurrentCulture;

    public SnoozeTextTests()
    {
        // Time as 14:30, date and time as 03/12/2026 14:30.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Loc.Use(key => key switch
        {
            "Snooze.Status.EndsAt" => "Global Snooze Active (Ends at {0})",
            "Snooze.Status.UntilReboot" => "Global Snooze Active (until next restart)",
            "Task.Snooze.Status" => "Snoozed until {0}",
            "Task.Snooze.UntilRestart" => "Snoozed until restart or sign-out",
            _ => null
        });
    }

    public void Dispose()
    {
        Loc.Use(static _ => null);
        CultureInfo.CurrentCulture = _cultureBefore;
    }

    [Fact]
    public void AnEndLaterToday_IsGivenAsATime()
    {
        var window = new SnoozeWindow(Now.AddHours(5.5));

        Assert.Equal("Global Snooze Active (Ends at 14:30)", SnoozeText.AllTasks(window, _time));
        Assert.Equal("Snoozed until 14:30", SnoozeText.OneTask(window, _time));
    }

    [Fact]
    public void AnEndOnAnotherDay_IsGivenWithItsDate()
    {
        var window = new SnoozeWindow(Now.AddDays(2).AddHours(5.5));

        Assert.Equal("Global Snooze Active (Ends at 03/12/2026 14:30)", SnoozeText.AllTasks(window, _time));
        Assert.Equal("Snoozed until 03/12/2026 14:30", SnoozeText.OneTask(window, _time));
    }

    [Fact]
    public void TheEnd_IsGivenInTheTimeOfThisComputer()
    {
        // 09:00 UTC is 20:00 here, so an end five and a half hours later falls on the next day.
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(11), "Test", "Test"));

        Assert.Equal("Snoozed until 21:30", SnoozeText.OneTask(new SnoozeWindow(Now.AddHours(1.5)), _time));
        Assert.Equal("Snoozed until 03/11/2026 01:30", SnoozeText.OneTask(new SnoozeWindow(Now.AddHours(5.5)), _time));
    }

    [Fact]
    public void TheUsersCulture_DecidesHowTheTimeIsWritten()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortTimePattern = "H.mm 'h'";
        CultureInfo.CurrentCulture = culture;

        Assert.Equal("Snoozed until 14.30 h", SnoozeText.OneTask(new SnoozeWindow(Now.AddHours(5.5)), _time));
    }

    [Fact]
    public void ASnoozeUntilRestart_SaysSo()
    {
        Assert.Equal("Global Snooze Active (until next restart)", SnoozeText.AllTasks(SnoozeWindow.UntilRestart, _time));
        Assert.Equal("Snoozed until restart or sign-out", SnoozeText.OneTask(SnoozeWindow.UntilRestart, _time));
    }

    /// <summary>A mistake of the app has to reach its crash report, not pass for a snooze that could not be saved.</summary>
    [Fact]
    public void AnExceptionThatIsNoFailureOfASnooze_IsNotWordedAsOne()
    {
        Assert.Null(SnoozeText.WhyNotSnoozed(new InvalidOperationException("Collection was modified.")));
        Assert.Null(SnoozeText.WhyNotSnoozed(new NullReferenceException()));
    }

    /// <summary>That the other languages have the same texts with the same placeholders is checked for all texts at once.</summary>
    [Fact]
    public void TheTextsTheStatusIsMadeOf_Exist_WithAPlaceForTheTimeWhereOneIsGiven()
    {
        var english = RepoFiles.Texts("en-US");

        Assert.Contains("{0}", english["Snooze.Status.EndsAt"]);
        Assert.Contains("{0}", english["Task.Snooze.Status"]);
        Assert.NotEmpty(english["Snooze.Status.UntilReboot"]);
        Assert.NotEmpty(english["Task.Snooze.UntilRestart"]);
    }
}
