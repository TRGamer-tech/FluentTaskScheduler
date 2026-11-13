using FluentTaskScheduler.Reminders;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Snoozing;

namespace FluentTaskScheduler.Tests.Reminders;

/// <summary>
/// Announcing tasks before they run. The lead time is the default of five minutes unless a test
/// changes it; the clock starts at <see cref="SnoozeTestBase.Start"/>.
/// </summary>
public sealed class ReminderServiceTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";

    private IEnumerable<string> Announced => Notifier.Shown.Where(shown => shown.StartsWith("TaskRunsSoon", StringComparison.Ordinal));

    private ReminderService StartReminders(ISnoozeService? snooze = null)
    {
        var reminders = DisposedWithTheTest(new ReminderService(Store, Settings, snooze ?? NewService(), Notifier, Time));
        reminders.Start();
        return reminders;
    }

    /// <summary>Sets what the task reports, in local time as Task Scheduler gives it.</summary>
    private void RunsAt(string path, DateTimeOffset next, DateTimeOffset? last = null) =>
        Store.SetRunTimes(path, last is { } time ? Local(time) : null, Local(next));

    private DateTime Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Time.LocalTimeZone).DateTime;

    private void AdvanceSeconds(double seconds) => Time.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void ATask_IsAnnouncedTheLeadTimeBeforeItRuns_AndOnlyOnce()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(60));
        StartReminders();

        AdvanceMinutes(54);
        Assert.Empty(Announced);

        AdvanceMinutes(1);
        Assert.Equal(["TaskRunsSoon Backup in 5"], Announced);

        for (int minute = 0; minute < 10; minute++) AdvanceMinutes(1);
        Assert.Single(Announced);
    }

    [Fact]
    public void ARunThatIsDueThisVeryMoment_IsNotAnnounced()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddSeconds(30));
        StartReminders();

        AdvanceSeconds(30);

        Assert.Equal(1, Store.Listings);
        Assert.Empty(Announced);
    }

    [Fact]
    public void ARunThatIsNoticedLate_IsStillAnnounced_WithTheMinutesThatAreLeft()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(3));
        StartReminders();

        AdvanceSeconds(30);

        Assert.Equal(["TaskRunsSoon Backup in 3"], Announced);
    }

    [Fact]
    public void TasksOfWindows_HiddenTasks_AndDisabledTasks_AreNotAnnounced()
    {
        Store.Add(Backup)
            .Add(@"\Microsoft\Windows\Defrag\ScheduledDefrag")
            .Add(@"\Hidden", draft: new TaskDraft { Hidden = true })
            .Add(@"\Disabled", enabled: false);
        foreach (string path in Store.Paths) RunsAt(path, Start.AddMinutes(3));
        StartReminders();

        AdvanceMinutes(1);

        Assert.Equal(["TaskRunsSoon Backup in 2"], Announced);
    }

    [Fact]
    public async Task WhileAllTasksAreSnoozed_NothingIsAnnounced_AndTheTasksAreNotEvenListed()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(10));
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(30));
        StartReminders(snooze);

        for (int minute = 0; minute < 15; minute++) AdvanceMinutes(1);

        Assert.Empty(Announced);
        Assert.Equal(0, Store.Listings);
    }

    [Fact]
    public async Task WhenTheSnoozeOfAllTasksEnds_AnnouncementsResumeAtOnce()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(32));
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(30));
        StartReminders(snooze);

        AdvanceMinutes(29);
        Assert.Empty(Announced);

        AdvanceMinutes(1);
        Assert.Equal(["TaskRunsSoon Backup in 2"], Announced);
    }

    [Fact]
    public async Task WhenASnoozeThatDisabledTheTasksEnds_TheirRunsAreAnnouncedAsSoonAsTheyAreEnabledAgain()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(32));
        SuspendTriggers();
        var snooze = await StartedService();
        await snooze.SnoozeAllAsync(After(30));
        StartReminders(snooze);

        AdvanceMinutes(29);
        Assert.Empty(Announced);

        AdvanceMinutes(1);
        Assert.Equal(["TaskRunsSoon Backup in 2"], Announced);
    }

    [Fact]
    public async Task ATaskThatIsResumed_IsAnnouncedAtOnce_IfItRunsSoon()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(4));
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(60));
        StartReminders(snooze);
        AdvanceMinutes(1);
        Assert.Empty(Announced);

        await snooze.ResumeTaskAsync(Backup);

        Assert.Equal(["TaskRunsSoon Backup in 3"], Announced);
    }

    [Fact]
    public async Task ASnoozeOfAllTasksThatStartsLater_SilencesTheAnnouncements()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(20));
        var snooze = await StartedService();
        StartReminders(snooze);
        AdvanceMinutes(1);

        await snooze.SnoozeAllAsync(After(60));
        for (int minute = 0; minute < 25; minute++) AdvanceMinutes(1);

        Assert.Empty(Announced);
        Assert.Equal(1, Store.Listings);
    }

    [Fact]
    public async Task ATaskThatIsSnoozedOnItsOwn_IsNotAnnounced()
    {
        Store.Add(Backup).Add(@"\Other");
        RunsAt(Backup, Start.AddMinutes(3));
        RunsAt(@"\Other", Start.AddMinutes(3));
        var snooze = await StartedService();
        await snooze.SnoozeTaskAsync(Backup, After(60));
        StartReminders(snooze);

        AdvanceMinutes(1);

        Assert.Equal(["TaskRunsSoon Other in 2"], Announced);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithRemindersOrAllNotificationsSwitchedOff_NothingIsAnnounced_AndTheTasksAreNotListed(bool showNotifications, bool enableReminders)
    {
        Settings.Update(settings => settings with { ShowNotifications = showNotifications, EnableUpcomingReminders = enableReminders });
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(3));
        StartReminders();

        for (int minute = 0; minute < 10; minute++) AdvanceMinutes(1);

        Assert.Empty(Announced);
        Assert.Equal(0, Store.Listings);
    }

    [Fact]
    public void SwitchingRemindersOn_StartsThemAtOnce_AndSwitchingThemOffStopsThem()
    {
        Settings.Update(settings => settings with { EnableUpcomingReminders = false });
        Store.Add(Backup).Add(@"\Later");
        RunsAt(Backup, Start.AddMinutes(3));
        RunsAt(@"\Later", Start.AddMinutes(30));
        StartReminders();
        AdvanceMinutes(1);

        Settings.Update(settings => settings with { EnableUpcomingReminders = true });
        Assert.Equal(["TaskRunsSoon Backup in 2"], Announced);

        Settings.Update(settings => settings with { EnableUpcomingReminders = false });
        for (int minute = 0; minute < 40; minute++) AdvanceMinutes(1);
        Assert.Single(Announced);
    }

    [Fact]
    public void AChangedLeadTime_AppliesAtOnce()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(20));
        StartReminders();
        AdvanceMinutes(1);
        Assert.Empty(Announced);

        Settings.Update(settings => settings with { ReminderLeadMinutes = 30 });

        Assert.Equal(["TaskRunsSoon Backup in 19"], Announced);
    }

    [Fact]
    public void ASettingThatHasNothingToDoWithReminders_DoesNotMakeThemLook()
    {
        Store.Add(Backup);
        StartReminders();
        AdvanceMinutes(1);
        Assert.Equal(1, Store.Listings);

        Settings.Update(settings => settings with { ConfirmDelete = false });

        Assert.Equal(1, Store.Listings);
    }

    [Fact]
    public void TheFirstLook_WaitsUntilTheAppHasStarted()
    {
        Store.Add(Backup);
        StartReminders();
        Assert.Equal(0, Store.Listings);

        AdvanceSeconds(29);
        Assert.Equal(0, Store.Listings);

        AdvanceSeconds(1);
        Assert.Equal(1, Store.Listings);
    }

    [Fact]
    public void WithNothingDue_TheTasksAreListedEveryFiveMinutes_NotEveryMinute()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddDays(2));
        StartReminders();

        for (int minute = 0; minute < 60; minute++) AdvanceMinutes(1);

        Assert.Equal(12, Store.Listings);
    }

    [Fact]
    public void ATaskThatRunsAgainWithinTheLeadTime_IsNotAnnounced_WhileOthersAre()
    {
        Store.Add(Backup).Add(@"\Daily");
        RunsAt(@"\Daily", Start.AddMinutes(90));
        DateTimeOffset next = Start.AddMinutes(2);
        RunsAt(Backup, next, last: next.AddMinutes(-4));
        StartReminders();

        for (int minute = 0; minute < 120; minute++)
        {
            AdvanceMinutes(1);

            // As Task Scheduler does it: a run that has started is the last run, and the next is four minutes later.
            if (Time.GetUtcNow() < next) continue;
            RunsAt(Backup, next.AddMinutes(4), last: next);
            next = next.AddMinutes(4);
        }

        Assert.StartsWith("TaskRunsSoon Daily", Assert.Single(Announced));
    }

    [Fact]
    public void ATaskThatRunsDaily_IsAnnouncedBeforeEveryRun()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddHours(1));
        StartReminders();

        AdvanceMinutes(55);
        Assert.Single(Announced);

        // It has run; tomorrow's run is the next.
        AdvanceMinutes(10);
        RunsAt(Backup, Start.AddHours(25), last: Start.AddHours(1));
        Time.Advance(TimeSpan.FromHours(22));
        Assert.Single(Announced);

        Time.Advance(TimeSpan.FromMinutes(110));
        Assert.Equal(["TaskRunsSoon Backup in 5", "TaskRunsSoon Backup in 5"], Announced);
    }

    [Fact]
    public void ARunThatStaysTheSame_IsNotAnnouncedAgainAtLaterLooks()
    {
        Settings.Update(settings => settings with { ReminderLeadMinutes = 60 });
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(45));
        StartReminders();

        for (int minute = 0; minute < 44; minute++) AdvanceMinutes(1);

        Assert.Equal(["TaskRunsSoon Backup in 44"], Announced);
        Assert.True(Store.Listings > 5);
    }

    [Fact]
    public void RunTimes_AreReadAsTheLocalTimesTheyAre()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));
        Store.Add(Backup);
        // Noon here is 10:00 UTC: an hour from now.
        Store.SetRunTimes(Backup, last: null, next: new DateTime(2026, 3, 10, 12, 0, 0));
        StartReminders();

        AdvanceMinutes(54);
        Assert.Empty(Announced);

        AdvanceMinutes(1);
        Assert.Equal(["TaskRunsSoon Backup in 5"], Announced);
    }

    [Fact]
    public void EveryLook_ReadsTheTimeZoneOfTheComputerAfresh()
    {
        // In the app the zone comes from .NET, which reads it once and keeps it until its cache is
        // cleared; read again, it is a new object. A test cannot change the zone of the computer,
        // so the new object is what it can see.
        TimeZoneInfo readBefore = TimeZoneInfo.Local;
        StartReminders();

        AdvanceSeconds(30);

        Assert.Equal(1, Store.Listings);
        Assert.NotSame(readBefore, TimeZoneInfo.Local);
    }

    [Fact]
    public void WhenTheTasksCannotBeListed_TheNextLookTriesAgain()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(8));
        Store.ListingFails = new InvalidOperationException("Task Scheduler does not answer.");
        StartReminders();
        AdvanceMinutes(1);
        Assert.Empty(Announced);

        Store.ListingFails = null;
        AdvanceMinutes(5);

        Assert.Equal(["TaskRunsSoon Backup in 2"], Announced);
    }

    [Fact]
    public void AfterDispose_NothingIsAnnounced_AndNoSettingBringsItBack()
    {
        Store.Add(Backup);
        RunsAt(Backup, Start.AddMinutes(3));
        var reminders = StartReminders();

        reminders.Dispose();
        AdvanceMinutes(1);
        Settings.Update(settings => settings with { ReminderLeadMinutes = 10 });

        Assert.Empty(Announced);
        Assert.Equal(0, Store.Listings);
    }
}
