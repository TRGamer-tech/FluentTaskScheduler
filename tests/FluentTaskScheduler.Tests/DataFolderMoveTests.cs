using FluentTaskScheduler.History;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Logging;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Support;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FluentTaskScheduler.Tests;

public sealed class DataFolderMoveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Laid out as under %LOCALAPPDATA%: the folder of versions before 2.0, and the one of today.
    private string Old => _temp.File("FluentTaskScheduler");
    private string New => Path.Combine(_temp.Path, "TRGamer-tech", "FluentTaskScheduler");

    /// <summary>One of each kind of file the app has ever kept in its data folder, the saved scripts aside. Named from that folder.</summary>
    private static readonly string[] AppFilesOfEveryKind =
    [
        "App_Log.txt", "App_Log.txt.old", "App_Log_001.txt",
        "Crash_Log.txt", "Crash_Log.txt.old",
        "Error_Log.txt", "Error_Log.txt.old", "Error_Log_002.txt",
        "ExecutionHistory/runs-2026-10.jsonl", "ExecutionHistory/runs-2026-11.jsonl",
        "settings.json", "settings.unreadable.json",
        "snooze.json", "snooze.unreadable-20261118-093000.json",
        "snooze_suppressed.json", "task_snoozes.json", "user_templates.json"
    ];

    /// <summary>Two scripts as the editor of version 1.9 saved them, and the list version 2.0 keeps next to them.</summary>
    private static readonly string[] SavedScripts =
    [
        "Scripts/0f8fad5b-d9cb-469f-a165-70867728950e.ps1", "Scripts/7c9e6679-7425-40de-944b-e07fc1f90ae7.ps1", "Scripts/scripts.json"
    ];

    /// <summary>What Velopack keeps in the folder it installs the app into for one user, and a file of the user's own.</summary>
    private static readonly string[] FilesOfOthers =
    [
        "Update.exe", "current/FluentTaskScheduler.exe", "current/sq.version", "notes.txt", "packages/FluentTaskScheduler-2.0.0-win-x64-full.nupkg"
    ];

    private static void Write(string folder, string name, string content)
    {
        string file = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private static void Write(string folder, IEnumerable<string> names, string content)
    {
        foreach (string name in names) Write(folder, name, content);
    }

    /// <summary>Every file below a folder, named from that folder with forward slashes, in order.</summary>
    private static string[] FilesIn(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

    /// <summary>Every file below a folder with what it holds, e.g. "Scripts/tidy.ps1: old".</summary>
    private static string[] Content(string folder) =>
        FilesIn(folder).Select(name => $"{name}: {File.ReadAllText(Path.Combine(folder, name))}").ToArray();

    private static string[] Holding(IEnumerable<string> names, string content) =>
        names.Order(StringComparer.Ordinal).Select(name => $"{name}: {content}").ToArray();

    [Fact]
    public void WithoutAnOldFolder_ThereIsNothingToDo_AndNoFolderIsMade()
    {
        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(0, report.Moved);
        Assert.Empty(report.Failures);
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void AnOldFolderThatHoldsOnlyTheInstallersFiles_IsLeftAsItIs_AndNoFolderIsMade()
    {
        Write(Old, FilesOfOthers, "theirs");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(0, report.Moved);
        Assert.Empty(report.Failures);
        Assert.Equal(Holding(FilesOfOthers, "theirs"), Content(Old));
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void TheAppsFiles_ArriveInTheNewFolderWithWhatTheyHeld_AndTheEmptiedOldFolderIsRemoved()
    {
        Write(Old, AppFilesOfEveryKind, "old");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(Holding(AppFilesOfEveryKind, "old"), Content(New));
        Assert.Equal(AppFilesOfEveryKind.Length, report.Moved);
        Assert.Empty(report.Failures);
        Assert.False(Directory.Exists(Old));
    }

    [Fact]
    public void WhatIsNotTheApps_StaysWhereItIs_AndSoDoesTheOldFolder()
    {
        Write(Old, AppFilesOfEveryKind, "old");
        Write(Old, FilesOfOthers, "theirs");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(Holding(FilesOfOthers, "theirs"), Content(Old));
        Assert.Equal(Holding(AppFilesOfEveryKind, "old"), Content(New));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void TheSavedScripts_AreCopied_AndStayWhereTheTasksCreatedFromThemLookForThem()
    {
        Write(Old, SavedScripts, "old");
        Write(Old, "settings.json", "old");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(Holding([.. SavedScripts, "settings.json"], "old"), Content(New));
        Assert.Equal(Holding(SavedScripts, "old"), Content(Old));
        Assert.Equal(SavedScripts.Length + 1, report.Moved);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void AnOldFolderThatIsEmptyAlready_IsRemoved()
    {
        Directory.CreateDirectory(Old);

        DataFolderMove.Run(Old, New);

        Assert.False(Directory.Exists(Old));
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void AnEntryOfTheApp_IsKnownWhateverTheCaseOfItsName()
    {
        Write(Old, ["SETTINGS.JSON", "app_log.TXT", "executionHISTORY/runs-2026-11.jsonl", "scripts/tidy.ps1"], "old");

        DataFolderMove.Run(Old, New);

        Assert.Equal(["SETTINGS.JSON: old", "app_log.TXT: old", "executionHISTORY/runs-2026-11.jsonl: old", "scripts/tidy.ps1: old"], Content(New));
        Assert.Equal(["scripts/tidy.ps1: old"], Content(Old));
    }

    [Fact]
    public void AFileThatIsInBothFolders_StaysAsItIsInBoth()
    {
        Write(Old, ["ExecutionHistory/runs-2026-10.jsonl", "ExecutionHistory/runs-2026-11.jsonl", "Scripts/tidy.ps1", "settings.json", "snooze.json"], "old");
        Write(New, ["ExecutionHistory/runs-2026-11.jsonl", "Scripts/tidy.ps1", "settings.json"], "new");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(
            ["ExecutionHistory/runs-2026-10.jsonl: old", "ExecutionHistory/runs-2026-11.jsonl: new", "Scripts/tidy.ps1: new", "settings.json: new", "snooze.json: old"],
            Content(New));
        Assert.Equal(["ExecutionHistory/runs-2026-11.jsonl: old", "Scripts/tidy.ps1: old", "settings.json: old"], Content(Old));
        Assert.Equal(2, report.Moved);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void AMoveThatWasEndedHalfWay_IsFinishedByTheNextStart()
    {
        // Some files had arrived, two folders were half way, and the rest had not been touched.
        string[] arrived = ["App_Log.txt", "ExecutionHistory/runs-2026-10.jsonl", "settings.json"];
        Write(New, arrived, "old");
        Write(New, SavedScripts[0], "old");
        Write(Old, AppFilesOfEveryKind.Except(arrived), "old");
        Write(Old, SavedScripts, "old");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(Holding([.. AppFilesOfEveryKind, .. SavedScripts], "old"), Content(New));
        Assert.Equal(Holding(SavedScripts, "old"), Content(Old));
        Assert.Equal(AppFilesOfEveryKind.Length - arrived.Length + SavedScripts.Length - 1, report.Moved);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void WhatTheUserPutIntoAFolderOfTheApp_ComesAlong()
    {
        string[] history = ["ExecutionHistory/2025/runs-2025-12.jsonl", "ExecutionHistory/notes.txt"];
        string[] scripts = ["Scripts/Kept for later/tidy.ps1", "Scripts/Kept for later/tidy.txt"];
        Write(Old, [.. history, .. scripts], "old");

        DataFolderMove.Run(Old, New);

        Assert.Equal(Holding([.. history, .. scripts], "old"), Content(New));
        Assert.Equal(Holding(scripts, "old"), Content(Old));
    }

    [Fact]
    public void WhatAnInterruptedSaveOrRunLeftBehind_IsDeletedInsteadOfMoved()
    {
        Write(Old, ["settings.json.tmp", "snooze.json.tmp", "script-run-6a4be0a5cf0c4b6a8e4d1f0a1c2b3d4e.ps1", "script-run-6a4be0a5cf0c4b6a8e4d1f0a1c2b3d4e.ps1.tmp"], "half written");
        Write(Old, "settings.json", "old");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(["settings.json: old"], Content(New));
        Assert.Equal(1, report.Moved);
        Assert.False(Directory.Exists(Old));
    }

    [Fact]
    public void MovingASecondTime_ChangesNothing()
    {
        Write(Old, [.. AppFilesOfEveryKind, .. SavedScripts], "old");
        Write(Old, FilesOfOthers, "theirs");
        Write(New, "settings.json", "new");
        DataFolderMove.Run(Old, New);
        string[] oldBefore = Content(Old), newBefore = Content(New);

        DataFolderMoveReport second = DataFolderMove.Run(Old, New);

        Assert.Equal(0, second.Moved);
        Assert.Empty(second.Failures);
        Assert.Equal(oldBefore, Content(Old));
        Assert.Equal(newBefore, Content(New));
    }

    [Fact]
    public void AFolderThatCannotBeMoved_IsReported_AndTheRestStillMoves()
    {
        Write(Old, ["ExecutionHistory/runs-2026-11.jsonl", "settings.json", "snooze.json"], "old");
        Write(New, "ExecutionHistory", "a file where the folder should be");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        DataFolderMoveFailure failure = Assert.Single(report.Failures);
        Assert.Equal(Path.Combine(Old, "ExecutionHistory"), failure.Path);
        Assert.IsAssignableFrom<IOException>(failure.Error);
        Assert.Equal(["ExecutionHistory/runs-2026-11.jsonl: old"], Content(Old));
        Assert.Equal(["ExecutionHistory: a file where the folder should be", "settings.json: old", "snooze.json: old"], Content(New));
        Assert.Equal(2, report.Moved);
    }

    [Fact]
    public void WhenTheNewFolderCannotBeMade_EverythingStays_AndThatIsReported()
    {
        Write(Old, ["ExecutionHistory/runs-2026-11.jsonl", "settings.json"], "old");
        Write(_temp.Path, "TRGamer-tech", "a file where the folder should be");

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Equal(Old, Assert.Single(report.Failures).Path);
        Assert.Equal(0, report.Moved);
        Assert.Equal(["ExecutionHistory/runs-2026-11.jsonl: old", "settings.json: old"], Content(Old));
    }

    [SkippableFact]
    public void AFileThatIsInUse_StaysAndIsReported_AndTheNextStartMovesIt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "only Windows keeps a file that is open elsewhere from being moved");
        Write(Old, ["settings.json", "snooze.json", "task_snoozes.json"], "old");

        DataFolderMoveReport report;
        using (new FileStream(Path.Combine(Old, "snooze.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report = DataFolderMove.Run(Old, New);
        }

        Assert.Equal(Path.Combine(Old, "snooze.json"), Assert.Single(report.Failures).Path);
        Assert.Equal(["settings.json: old", "task_snoozes.json: old"], Content(New));
        Assert.Equal(["snooze.json: old"], Content(Old));

        DataFolderMoveReport next = DataFolderMove.Run(Old, New);

        Assert.Empty(next.Failures);
        Assert.Equal(["settings.json: old", "snooze.json: old", "task_snoozes.json: old"], Content(New));
        Assert.False(Directory.Exists(Old));
    }

    [Fact]
    public void StartsAtTheSameMoment_BringEverythingOver_AndReportNoFailure()
    {
        string[] scripts = Enumerable.Range(0, 20).Select(number => $"Scripts/{number:00}.ps1").ToArray();
        string[] history = Enumerable.Range(0, 20).Select(number => $"ExecutionHistory/runs-20{number:00}-01.jsonl").ToArray();
        string[] all = [.. AppFilesOfEveryKind, .. scripts, .. history];

        for (int round = 0; round < 20; round++)
        {
            string old = Path.Combine(_temp.Path, $"old-{round}"), @new = Path.Combine(_temp.Path, $"new-{round}");
            Write(old, all, "old");
            var reports = new DataFolderMoveReport[4];

            Parallel.For(0, reports.Length, start => reports[start] = DataFolderMove.Run(old, @new));

            Assert.All(reports, report => Assert.Empty(report.Failures));
            Assert.Equal(Holding(all, "old"), Content(@new));
            Assert.Equal(Holding(scripts, "old"), Content(old));
        }
    }

    [Fact]
    public async Task EverythingTheAppWritesIntoItsDataFolder_ComesAlong()
    {
        var paths = new AppPaths(Old);
        Write(Old, ["settings.json", "snooze.json"], "damaged, so that the app sets them aside");
        Write(_temp.Path, "built-in.json", "[]");

        using (var settings = new SettingsStore(paths))
        {
            settings.Update(current => current with { EnableExecutionHistoryLog = true });
            settings.Flush();

            var logging = new AppLogging(paths, maxFileBytes: 200);
            for (int line = 0; line < 10; line++) logging.Logger.Information("Enough to fill more than one part of the log");
            logging.Logger.Error("An error");
            logging.Logger.Fatal("A crash");
            logging.Logger.Dispose();

            new ScriptStore(paths, _temp.File("built-in.json")).Save(null, "Tidy up", "", "Remove-Item old.txt");

            using (var snooze = new SnoozeService(new FakeTaskStore(), settings, new RecordingNotifier(), new FakeWindowsSession(), paths))
            {
                await snooze.SnoozeAllAsync(SnoozeEnd.After(TimeSpan.FromMinutes(30)));
            }

            var stream = new FakeTaskEventStream();
            using var history = new ExecutionHistoryLog(stream, settings, paths);
            stream.Complete(FakeTaskEventStream.Finished(@"\Backup", RunStatus.Succeeded));
        }
        string[] written = FilesIn(Old);
        Assert.Contains("settings.unreadable.json", written);
        Assert.Contains(written, name => name.StartsWith("snooze.unreadable-", StringComparison.Ordinal));
        Assert.Contains(written, name => name.StartsWith("App_Log_", StringComparison.Ordinal));
        Assert.Contains(written, name => name.StartsWith("ExecutionHistory/", StringComparison.Ordinal));

        DataFolderMoveReport report = DataFolderMove.Run(Old, New);

        Assert.Empty(report.Failures);
        Assert.Equal(written, FilesIn(New));
        Assert.Equal(written.Where(name => name.StartsWith("Scripts/", StringComparison.Ordinal)), FilesIn(Old));
    }

    [Fact]
    public void WhatWasMovedAndWhatCouldNotBe_AreWrittenToTheLog()
    {
        var written = new CollectedEvents();
        using Logger log = new LoggerConfiguration().WriteTo.Sink(written).CreateLogger();
        var failure = new DataFolderMoveFailure(Path.Combine(Old, "snooze.json"), new IOException("in use"));

        new DataFolderMoveReport(Old, New, 12, [failure]).WriteTo(log);

        Assert.Equal(2, written.Events.Count);
        Assert.Equal(LogEventLevel.Information, written.Events[0].Level);
        Assert.Contains("12", written.Events[0].RenderMessage());
        Assert.Contains(Old, written.Events[0].RenderMessage());
        Assert.Contains(New, written.Events[0].RenderMessage());
        Assert.Equal(LogEventLevel.Warning, written.Events[1].Level);
        Assert.Contains(failure.Path, written.Events[1].RenderMessage());
        Assert.Same(failure.Error, written.Events[1].Exception);
    }

    [Fact]
    public void AMoveThatHadNothingToDo_WritesNothingToTheLog()
    {
        var written = new CollectedEvents();
        using Logger log = new LoggerConfiguration().WriteTo.Sink(written).CreateLogger();

        DataFolderMove.Run(Old, New).WriteTo(log);

        Assert.Empty(written.Events);
    }

    private sealed class CollectedEvents : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
