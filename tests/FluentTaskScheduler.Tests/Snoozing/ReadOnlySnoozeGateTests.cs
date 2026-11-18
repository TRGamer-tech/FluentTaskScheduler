using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>
/// The gate of a second process, such as the command line. The service of each test plays the
/// app that runs at the same time and owns the state file.
/// </summary>
public sealed class ReadOnlySnoozeGateTests : SnoozeTestBase
{
    private const string Backup = @"\Backup";
    private const string Weekly = @"\Reports\Weekly";

    private ReadOnlySnoozeGate NewGate() => new(Folder.Paths, Session, Time);

    /// <summary>Every file in the data folder with its content.</summary>
    private string[] DataFolderContent() => Directory.GetFiles(Folder.Path, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(file => $"{Path.GetRelativePath(Folder.Path, file)}: {File.ReadAllText(file)}")
        .ToArray();

    [Fact]
    public void WithoutAnyState_NothingIsBlocked_AndNothingIsCreated()
    {
        var gate = NewGate();

        Assert.False(gate.BlocksRun(Backup));
        Assert.Empty(DataFolderContent());
    }

    [Fact]
    public async Task WhatTheAppSnoozes_IsBlockedForTheSecondProcess_FromThatMomentUntilTheAppResumesIt()
    {
        Store.Add(Backup).Add(Weekly);
        var app = await StartedService();
        var gate = NewGate();
        Assert.False(gate.BlocksRun(Backup));

        await app.SnoozeTaskAsync(Backup, After(60));
        Assert.True(gate.BlocksRun(Backup));
        Assert.True(gate.BlocksRun(@"\BACKUP"));
        Assert.False(gate.BlocksRun(Weekly));

        await app.SnoozeAllAsync(After(30));
        Assert.True(gate.BlocksRun(Weekly));

        await app.ResumeAllAsync();
        await app.ResumeTaskAsync(Backup);
        Assert.False(gate.BlocksRun(Backup));
        Assert.False(gate.BlocksRun(Weekly));
    }

    [Fact]
    public async Task ASnoozeThatHasRunOut_DoesNotBlock_EvenIfTheAppHasNotEndedItYet()
    {
        Store.Add(Backup);
        SuspendTriggers();
        var app = await StartedService();
        await app.SnoozeAllAsync(After(30));
        await app.SnoozeTaskAsync(Backup, After(60));

        // The app is closed, so nobody ends the snoozes; the file goes on saying they are running.
        app.Dispose();
        var gate = NewGate();

        AdvanceMinutes(29);
        Assert.True(gate.BlocksRun(@"\Other"));

        AdvanceMinutes(1);
        Assert.False(gate.BlocksRun(@"\Other"));
        Assert.True(gate.BlocksRun(Backup));

        AdvanceMinutes(30);
        Assert.False(gate.BlocksRun(Backup));

        // Re-enabling the task stays the app's job.
        Assert.False(Store.IsEnabled(Backup));
        var restarted = await StartedService();
        Assert.True(Store.IsEnabled(Backup));
        Assert.Equal(new SnoozeState(), restarted.State);
    }

    [Fact]
    public async Task ASnoozeUntilRestart_BlocksInTheWindowsSessionItWasStartedIn_AndNotInTheNext()
    {
        var app = await StartedService();
        await app.SnoozeAllAsync(SnoozeEnd.AtRestart);
        var gate = NewGate();

        Time.Advance(TimeSpan.FromDays(2));
        Assert.True(gate.BlocksRun(Backup));

        Session.Id = "session-2";
        Assert.False(gate.BlocksRun(Backup));
    }

    [Fact]
    public async Task TheSecondProcess_NeverChangesAnythingOnDisk_WhateverItIsAsked()
    {
        Store.Add(Backup);
        SuspendTriggers();
        Settings.Flush();
        var app = await StartedService();
        await app.SnoozeAllAsync(After(30));
        app.RecordSuppressedRun(Backup, RunOrigin.Manual);
        app.Dispose();
        AdvanceMinutes(45);
        string[] before = DataFolderContent();

        var gate = NewGate();
        gate.BlocksRun(Backup);
        gate.RecordSuppressedRun(Backup, RunOrigin.Manual);
        gate.BlocksRun(Weekly);

        Assert.Equal(before, DataFolderContent());
    }

    [Fact]
    public async Task ARunRefusedInTheSecondProcess_IsNotAddedToTheRecordOfTheApp()
    {
        Store.Add(Backup);
        var app = await StartedService();
        await app.SnoozeAllAsync(After(30));
        string before = File.ReadAllText(StateFile);

        var outcome = new TaskRunner(Store, NewGate(), Notifier).Run(Backup);

        Assert.Equal(RunOutcome.Suppressed, outcome);
        Assert.False(Store.IsRunning(Backup));
        Assert.Contains("RunSuppressed Backup", Notifier.Shown);
        Assert.Equal(before, File.ReadAllText(StateFile));
        Assert.Empty(app.State.SuppressedRuns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"Ledger\": {")]
    [InlineData("{}")]
    public void AStateFileThatCannotBeRead_BlocksNothing_AndIsLeftExactlyWhereItIs(string content)
    {
        File.WriteAllText(StateFile, content);
        string[] before = DataFolderContent();

        Assert.False(NewGate().BlocksRun(Backup));

        Assert.Equal(before, DataFolderContent());
    }

    [Fact]
    public async Task AStateFileThatCannotBeOpened_BlocksNothing()
    {
        var app = await StartedService();
        await app.SnoozeAllAsync(After(30));
        var gate = NewGate();
        Assert.True(gate.BlocksRun(Backup));

        using (new FileStream(StateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(gate.BlocksRun(Backup));
        }

        Assert.True(gate.BlocksRun(Backup));
    }

    [Fact]
    public void BeforeTheAppHasTakenOverTheStateOfVersion19_ASnoozeOf19StillBlocks_AndItsFilesAreLeftAlone()
    {
        // As version 1.9 wrote them: all tasks snoozed until 10:30 UTC, one task until 11:00.
        File.WriteAllText(Folder.Paths.SettingsFile,
            """{"Theme":"Default","Language":"en-US","IsSnoozed":true,"SnoozeUntilUtc":"2026-03-10T10:30:00.0000000Z","SnoozeUntilReboot":false,"SnoozeBootStamp":"","SnoozeSuspendsScheduledTasks":false,"SnoozeIncludeMicrosoftTasks":false,"SnoozeDisabledTaskPaths":[],"EnableTaskPipelines":true}""");
        File.WriteAllText(Folder.File("task_snoozes.json"),
            """[{"TaskPath":"\\Backup","UntilUtc":"2026-03-10T11:00:00.0000000Z","UntilReboot":false,"BootStamp":"","WasEnabled":true}]""");
        string[] before = DataFolderContent();
        var gate = NewGate();

        Assert.True(gate.BlocksRun(Weekly));

        AdvanceMinutes(90);
        Assert.False(gate.BlocksRun(Weekly));
        Assert.True(gate.BlocksRun(Backup));

        AdvanceMinutes(30);
        Assert.False(gate.BlocksRun(Backup));
        Assert.Equal(before, DataFolderContent());
    }
}
