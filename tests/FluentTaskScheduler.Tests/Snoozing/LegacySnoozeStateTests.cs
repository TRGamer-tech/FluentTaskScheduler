using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>
/// The first start after an upgrade from version 1.9, with the files exactly as that version
/// wrote them: compact JSON, UTC times with seven decimals, the snooze inside settings.json.
/// </summary>
public sealed class LegacySnoozeStateTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";
    private const string Weekly = @"\Reports\Weekly";

    /// <summary>A snooze of all tasks until 10:30 UTC, ninety minutes after <see cref="SnoozeTestBase.Start"/>, with two tasks disabled for it.</summary>
    private const string RunningSnooze =
        """
        "IsSnoozed":true,"SnoozeUntilUtc":"2026-03-10T10:30:00.0000000Z","SnoozeUntilReboot":false,"SnoozeBootStamp":"","SnoozeSuspendsScheduledTasks":true,"SnoozeIncludeMicrosoftTasks":false,"SnoozeDisabledTaskPaths":["\\Backup","\\Reports\\Weekly"]
        """;

    private const string NoSnooze =
        """
        "IsSnoozed":false,"SnoozeUntilUtc":null,"SnoozeUntilReboot":false,"SnoozeBootStamp":"","SnoozeSuspendsScheduledTasks":false,"SnoozeIncludeMicrosoftTasks":false,"SnoozeDisabledTaskPaths":[]
        """;

    private string TaskSnoozes19 => Folder.File("task_snoozes.json");
    private string SuppressedRuns19 => Folder.File("snooze_suppressed.json");

    /// <summary>settings.json as version 1.9 wrote it, with the given snooze fields in their place.</summary>
    private void WriteSettings19(string snoozeFields) => File.WriteAllText(Folder.Paths.SettingsFile,
        """{"Theme":"Dark","IsOledMode":false,"IsMicaEnabled":true,"Language":"en-US","ConfirmDelete":true,"ShowNotifications":true,"EnableTrayIcon":true,"MinimizeToTray":false,"EnableLogging":true,"SeparateLogFiles":true,"EnableExecutionHistoryLog":false,"RunOnStartup":false,"SmoothScrolling":true,"WindowWidth":1200,"WindowHeight":800,"LastFolderPath":"\\","LastSeenVersion":"1.9.0","HasCompletedOnboarding":true,"EnableUpcomingReminders":true,"ReminderLeadMinutes":5,"ShowHiddenTasks":true,"SavedCategories":["Work","Personal"],"SavedTags":["urgent"],"""
        + snoozeFields.Trim()
        + ""","EnableTaskPipelines":true}""");

    [Fact]
    public async Task ASnoozeOfAllTasksThat19Started_GoesOn_AndItsTasksAreReEnabledWhenItEnds()
    {
        WriteSettings19(RunningSnooze);
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false).Add(@"\Other");

        var snooze = await StartedService();

        Assert.Equal(
            new GlobalSnooze(new SnoozeWindow(Start.AddMinutes(90)), SuspendsTriggers: true, SuspendedTasks: [Backup, Weekly]),
            snooze.State.Global);
        Assert.True(snooze.BlocksRun(@"\Other"));
        Assert.Empty(Store.Calls);

        AdvanceMinutes(90);

        Assert.True(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task WhatWasTakenOver_IsStillKnown_AfterTheSettingsWereSavedWithoutItAndTheAppRestarted()
    {
        WriteSettings19(RunningSnooze);
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false);
        var first = await StartedService();

        // The new settings no longer hold the snooze: their first save drops it from settings.json.
        Settings.Update(settings => settings with { ConfirmDelete = false });
        Settings.Flush();
        Assert.DoesNotContain("SnoozeDisabledTaskPaths", File.ReadAllText(Folder.Paths.SettingsFile));
        first.Dispose();

        var second = await StartedService();

        Assert.Equal([Backup, Weekly], second.State.Global?.SuspendedTasks);
    }

    [Fact]
    public async Task ASnoozeOf19ThatHasRunOutMeanwhile_IsOver_AndItsTasksAreReEnabledAtStartup()
    {
        WriteSettings19(RunningSnooze.Replace("2026-03-10T10:30", "2026-03-10T08:30"));
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false);

        var snooze = NewService();
        Assert.Null(snooze.State.Global);
        Assert.False(snooze.BlocksRun(Backup));

        await snooze.StartAsync();
        Assert.True(Store.IsEnabled(Backup));
        Assert.True(Store.IsEnabled(Weekly));
    }

    [Fact]
    public async Task ASnoozeUntilRebootOf19_IsTakenToBeOver_AndItsTasksAreReEnabledAtStartup()
    {
        WriteSettings19(
            """
            "IsSnoozed":true,"SnoozeUntilUtc":null,"SnoozeUntilReboot":true,"SnoozeBootStamp":"2026-03-10T06:12:00.0000000Z","SnoozeSuspendsScheduledTasks":true,"SnoozeIncludeMicrosoftTasks":false,"SnoozeDisabledTaskPaths":["\\Backup"]
            """);
        Store.Add(Backup, enabled: false);

        var snooze = await StartedService();

        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task TasksThat19WasStillReEnablingWhenItStopped_AreReEnabled()
    {
        WriteSettings19(NoSnooze.Replace("\"SnoozeDisabledTaskPaths\":[]", "\"SnoozeDisabledTaskPaths\":[\"\\\\Backup\"]"));
        Store.Add(Backup, enabled: false);

        var snooze = await StartedService();

        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public async Task TheTaskSnoozesOf19_GoOnOrEnd_AsTheirTimesSay()
    {
        File.WriteAllText(TaskSnoozes19,
            """[{"TaskPath":"\\StillSnoozed","UntilUtc":"2026-03-10T10:00:00.0000000Z","UntilReboot":false,"BootStamp":"","WasEnabled":true},{"TaskPath":"\\RanOut","UntilUtc":"2026-03-10T08:30:00.0000000Z","UntilReboot":false,"BootStamp":"","WasEnabled":true},{"TaskPath":"\\UntilReboot","UntilUtc":null,"UntilReboot":true,"BootStamp":"2026-03-09T07:00:00.0000000Z","WasEnabled":true},{"TaskPath":"\\DisabledBefore","UntilUtc":"2026-03-10T10:00:00.0000000Z","UntilReboot":false,"BootStamp":"","WasEnabled":false}]""");
        foreach (string path in new[] { @"\StillSnoozed", @"\RanOut", @"\UntilReboot", @"\DisabledBefore" }) Store.Add(path, enabled: false);

        var snooze = await StartedService();

        Assert.Equal([new TaskSnooze(@"\StillSnoozed", new SnoozeWindow(Start.AddMinutes(60)))], snooze.State.Tasks);
        Assert.False(Store.IsEnabled(@"\StillSnoozed"));
        Assert.True(Store.IsEnabled(@"\RanOut"));
        Assert.True(Store.IsEnabled(@"\UntilReboot"));

        AdvanceMinutes(60);
        Assert.True(Store.IsEnabled(@"\StillSnoozed"));

        // It was disabled before 1.9 snoozed it, and 1.9 would not have enabled it either.
        Assert.False(Store.IsEnabled(@"\DisabledBefore"));
    }

    [Fact]
    public async Task ATaskThat19SnoozedBothWays_IsReEnabledOnlyWhenBothSnoozesAreOver()
    {
        // 1.9 marked the task "was not enabled" because its own snooze of all tasks had disabled it first.
        WriteSettings19(RunningSnooze);
        File.WriteAllText(TaskSnoozes19,
            """[{"TaskPath":"\\Backup","UntilUtc":"2026-03-10T11:00:00.0000000Z","UntilReboot":false,"BootStamp":"","WasEnabled":false}]""");
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false);
        var snooze = await StartedService();

        AdvanceMinutes(90);
        Assert.True(Store.IsEnabled(Weekly));
        Assert.False(Store.IsEnabled(Backup));

        AdvanceMinutes(30);
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), snooze.State);
    }

    [Fact]
    public void TheRunsRefusedBy19_AreTakenOver_WithTheirOrigin()
    {
        File.WriteAllText(SuppressedRuns19,
            """[{"TimeUtc":"2026-03-10T08:15:00.1234567Z","TaskPath":"\\Backup","TaskName":"Backup","Origin":"Pipeline"},{"TimeUtc":"2026-03-09T22:00:00.0000000Z","TaskPath":"\\Reports\\Weekly","TaskName":"Weekly","Origin":"Manual"}]""");

        var snooze = NewService();

        Assert.Equal(
            [
                new SuppressedRun(new DateTimeOffset(2026, 3, 10, 8, 15, 0, TimeSpan.Zero).AddTicks(1234567), Backup, RunOrigin.CompletionAction),
                new SuppressedRun(new DateTimeOffset(2026, 3, 9, 22, 0, 0, TimeSpan.Zero), Weekly, RunOrigin.Manual)
            ],
            snooze.State.SuppressedRuns);
    }

    [Fact]
    public void TheFilesOf19_AreRemoved_OnceTheirContentHasItsNewHome()
    {
        WriteSettings19(RunningSnooze);
        File.WriteAllText(TaskSnoozes19, "[]");
        File.WriteAllText(SuppressedRuns19, "[]");

        NewService();

        Assert.True(File.Exists(StateFile));
        Assert.False(File.Exists(TaskSnoozes19));
        Assert.False(File.Exists(SuppressedRuns19));
    }

    [Fact]
    public void WhenVersion19LeftNothingSnoozed_NothingIsWritten()
    {
        WriteSettings19(NoSnooze);

        var snooze = NewService();

        Assert.Equal(new SnoozeState(), snooze.State);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task TheStateOf19_IsTakenOverOnce_AndNotAgainAtLaterStarts()
    {
        WriteSettings19(RunningSnooze);
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false);
        var first = await StartedService();
        await first.ResumeAllAsync();
        first.Dispose();
        Store.SetEnabled(Backup, false);

        // settings.json still holds the old fields: the settings were never saved in this test.
        var second = await StartedService();

        Assert.Null(second.State.Global);
        Assert.False(Store.IsEnabled(Backup));
    }

    [Fact]
    public async Task WhenTheStateOf19CannotBeWrittenToItsNewPlaceAtOnce_ItIsWrittenAsSoonAsThatWorks()
    {
        WriteSettings19(RunningSnooze);
        File.WriteAllText(TaskSnoozes19, "[]");
        Store.Add(Backup, enabled: false).Add(Weekly, enabled: false);
        // Nothing can be written where a folder is.
        Directory.CreateDirectory(StateFile);

        var first = NewService();
        Assert.NotNull(first.State.Global);
        Assert.True(File.Exists(TaskSnoozes19));

        Directory.Delete(StateFile);
        await first.StartAsync();
        Settings.Update(settings => settings with { ConfirmDelete = false });
        Settings.Flush();
        first.Dispose();

        var second = NewService();
        Assert.Equal([Backup, Weekly], second.State.Global?.SuspendedTasks);
    }

    [Fact]
    public void AListOfDisabledTasksThatIsNull_CountsAsEmpty()
    {
        WriteSettings19(RunningSnooze.Replace("[\"\\\\Backup\",\"\\\\Reports\\\\Weekly\"]", "null"));

        var snooze = NewService();

        Assert.Equal(new GlobalSnooze(new SnoozeWindow(Start.AddMinutes(90)), SuspendsTriggers: false, SuspendedTasks: []), snooze.State.Global);
    }

    [Fact]
    public void AFileOf19ThatCannotBeRead_IsLeftInPlaceAndReported_WithoutLosingWhatTheOthersHold()
    {
        const string damaged = "[{\"TaskPath\":\"\\\\Backup\",\"UntilUtc\":";
        WriteSettings19(RunningSnooze);
        File.WriteAllText(TaskSnoozes19, damaged);
        File.WriteAllText(SuppressedRuns19, "[]");

        var snooze = NewService();

        Assert.Equal([Backup, Weekly], snooze.State.Global?.SuspendedTasks);
        Assert.Equal(TaskSnoozes19, snooze.State.UnreadableStateFile);
        Assert.Equal(damaged, File.ReadAllText(TaskSnoozes19));
        Assert.False(File.Exists(SuppressedRuns19));
    }
}
