using System.Text.Json;
using FluentTaskScheduler.Cli;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.History;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Cli;

public sealed class CommandLineTests : IDisposable
{
    private const string HistoryLog = "Microsoft-Windows-TaskScheduler/Operational";

    private readonly FakeTaskStore _store = new FakeTaskStore().Add(@"\Backup").Add(@"\Jobs\Report", enabled: false);
    private readonly FakeSnoozeGate _snooze = new();
    private readonly FakeEventLog _log = new();
    private readonly TempFolder _folder = new();
    private readonly StringWriter _output = new();

    // What a Windows machine really logged for one run of a task.
    private static readonly IReadOnlyList<LogRecord> OneRun = Evidence.Of("B_ExitThree");

    public void Dispose() => _folder.Dispose();

    private int Run(params string[] arguments) =>
        new CommandLine(_store, new TaskRunner(_store, _snooze, new RecordingNotifier()), new TaskHistory(_log), _output).Run(arguments);

    /// <summary>The file an export of <paramref name="records"/> has to be: the newest event first.</summary>
    private static byte[] CsvOf(IEnumerable<LogRecord> records) =>
        HistoryCsv.Write(records.OrderByDescending(record => record.RecordId).Select(TaskEvent.From).OfType<TaskEvent>(), TimeZoneInfo.Local);

    [Theory]
    [InlineData(true, "--list")]
    [InlineData(true, "--RUN", @"\Backup")]
    [InlineData(true, "--export-history", @"\Backup")]
    [InlineData(true, "/?")]
    [InlineData(true, "--frobnicate")]
    [InlineData(false)]
    [InlineData(false, "--squirrel-install", "1.9.0")]
    [InlineData(false, "--veloapp-updated", "1.9.1")]
    [InlineData(false, "-ToastActivated")]
    [InlineData(false, "-Embedding")]
    [InlineData(false, "--after-restart")]
    [InlineData(false, @"C:\some\file.xml")]
    public void OnlySwitchesMeantForIt_GoToTheCommandLine(bool expected, params string[] arguments) =>
        Assert.Equal(expected, CommandLine.Handles(arguments));

    [Fact]
    public void List_PrintsEveryTaskAsJson()
    {
        Assert.Equal(CommandLine.Success, Run("--list"));

        using var json = JsonDocument.Parse(_output.ToString());
        var tasks = json.RootElement.EnumerateArray().ToList();
        Assert.Equal([@"\Backup", @"\Jobs\Report"], tasks.Select(task => task.GetProperty("Path").GetString()));
        Assert.Equal(["Ready", "Disabled"], tasks.Select(task => task.GetProperty("State").GetString()));
        Assert.Equal("Report", tasks[1].GetProperty("Name").GetString());
    }

    [Fact]
    public void Run_StartsTheTask()
    {
        Assert.Equal(CommandLine.Success, Run("--run", @"\Backup"));
        Assert.True(_store.IsRunning(@"\Backup"));
    }

    [Fact]
    public void Run_WhileTasksAreSnoozed_StartsNothing_AndFails()
    {
        _snooze.Snoozed = true;

        Assert.Equal(CommandLine.Failed, Run("--run", @"\Backup"));

        Assert.False(_store.IsRunning(@"\Backup"));
        Assert.Contains("snoozed", _output.ToString());
    }

    [Fact]
    public void EnableAndDisable_ChangeTheTask()
    {
        Assert.Equal(CommandLine.Success, Run("--enable", @"\Jobs\Report"));
        Assert.True(_store.IsEnabled(@"\Jobs\Report"));

        Assert.Equal(CommandLine.Success, Run("--disable", @"\Jobs\Report"));
        Assert.False(_store.IsEnabled(@"\Jobs\Report"));
    }

    [Theory]
    [InlineData("--run")]
    [InlineData("--enable")]
    [InlineData("--disable")]
    [InlineData("--export-history")]
    public void ATaskThatDoesNotExist_IsReported_WithAFailingExitCode(string command)
    {
        Assert.Equal(CommandLine.Failed, Run(command, @"\Nope"));
        Assert.Contains(@"Task not found: \Nope", _output.ToString());
    }

    [Fact]
    public void ATaskWindowsProtects_IsReportedAsAccessDenied()
    {
        _store.Protected.Add(@"\Backup");

        Assert.Equal(CommandLine.Failed, Run("--disable", @"\Backup"));
        Assert.Contains("Access denied", _output.ToString());
    }

    [Fact]
    public void ExportHistory_WritesTheTasksEventsToTheNamedFile_NewestFirst()
    {
        _log.Add(HistoryLog, [.. OneRun]);
        string file = _folder.File("backup.csv");

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\Backup", "--output", file));

        Assert.Equal(CsvOf(OneRun), File.ReadAllBytes(file));
        Assert.Contains($@"Exported {OneRun.Count} events of \Backup to {file}", _output.ToString());
    }

    [Fact]
    public void ExportHistory_LooksTheTaskUpTheWayTaskSchedulerSpellsIt_BecauseTheLogTellsUpperFromLowerCase()
    {
        _log.Add(HistoryLog, [.. OneRun]);

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\BACKUP", "--OUTPUT", _folder.File("backup.csv")));

        Assert.Equal(TaskSchedulerLog.EventsOf(@"\Backup", null, null), Assert.Single(_log.Queries).Query);
    }

    [Fact]
    public void ExportHistory_WithoutAFileName_WritesHistoryCsvInTheCurrentFolder()
    {
        _log.Add(HistoryLog, [.. OneRun]);
        string file = Path.Combine(Environment.CurrentDirectory, CommandLine.DefaultHistoryFile);
        try
        {
            Assert.Equal(CommandLine.Success, Run("--export-history", @"\Backup"));

            Assert.Equal("history.csv", CommandLine.DefaultHistoryFile);
            Assert.Equal(CsvOf(OneRun), File.ReadAllBytes(file));
            Assert.Contains(file, _output.ToString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ExportHistory_OfATaskThatHasBeenDeleted_StillWritesWhatTheLogHoldsAboutIt()
    {
        _log.Add(HistoryLog, [.. OneRun]);
        string file = _folder.File("old.csv");

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\Jobs\Old", "--output", file));

        Assert.Equal(CsvOf(OneRun), File.ReadAllBytes(file));
        Assert.Equal(TaskSchedulerLog.EventsOf(@"\Jobs\Old", null, null), Assert.Single(_log.Queries).Query);
    }

    [Fact]
    public void ExportHistory_OfATaskThatNeverRan_WritesAFileWithOnlyTheHeader()
    {
        string file = _folder.File("empty.csv");

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\Backup", "--output", file));

        Assert.Equal(CsvOf([]), File.ReadAllBytes(file));
        Assert.Contains("Exported 0 events", _output.ToString());
    }

    [Fact]
    public void ExportHistory_WhileHistoryIsSwitchedOffAndEmpty_Fails_AndSaysHowToSwitchItOn()
    {
        _log.State = LogState.Disabled;
        string file = _folder.File("backup.csv");

        Assert.Equal(CommandLine.Failed, Run("--export-history", @"\Backup", "--output", file));

        Assert.Contains($"wevtutil sl {HistoryLog} /e:true", _output.ToString());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ExportHistory_WhileHistoryIsSwitchedOff_WritesWhatWasRecordedBefore_AndSaysThatItMayBeStale()
    {
        _log.Add(HistoryLog, [.. OneRun]);
        _log.State = LogState.Disabled;
        string file = _folder.File("backup.csv");

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\Backup", "--output", file));

        Assert.Equal(CsvOf(OneRun), File.ReadAllBytes(file));
        Assert.Contains("not recording history at the moment", _output.ToString());
    }

    [Fact]
    public void ExportHistory_OfALogThatCannotBeRead_Fails_AndSaysWhy()
    {
        _log.Unreadable = true;
        string file = _folder.File("backup.csv");

        Assert.Equal(CommandLine.Failed, Run("--export-history", @"\Backup", "--output", file));

        Assert.Contains($"Error: The event log '{HistoryLog}' could not be read", _output.ToString());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ExportHistory_ToAFolderThatDoesNotExist_Fails()
    {
        _log.Add(HistoryLog, [.. OneRun]);

        Assert.Equal(CommandLine.Failed, Run("--export-history", @"\Backup", "--output", Path.Combine(_folder.Path, "missing", "backup.csv")));

        Assert.StartsWith("Error: ", _output.ToString());
    }

    [Fact]
    public void ExportHistory_OfMoreEventsThanOneExportHolds_KeepsTheNewest_AndSaysThatOlderOnesAreMissing()
    {
        var time = new DateTimeOffset(2031, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var data = new Dictionary<string, string> { ["TaskName"] = @"\Backup" };
        var records = Enumerable.Range(1, CommandLine.MostEventsExported + 1)
            .Select(number => new LogRecord("Microsoft-Windows-TaskScheduler", 107, time.AddSeconds(number), data) { RecordId = number })
            .ToArray();
        _log.Add(HistoryLog, records);
        string file = _folder.File("long.csv");

        Assert.Equal(CommandLine.Success, Run("--export-history", @"\Backup", "--output", file));

        string[] lines = File.ReadAllLines(file);
        Assert.Equal(1 + CommandLine.MostEventsExported, lines.Length);
        Assert.Equal(RowOf(records[^1]), lines[1]);
        Assert.Equal(RowOf(records[1]), lines[^1]);
        Assert.Contains("older events were left out", _output.ToString());

        static string RowOf(LogRecord record) => System.Text.Encoding.UTF8.GetString(CsvOf([record])).Split("\r\n")[1];
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("--run")]
    [InlineData("--enable")]
    [InlineData("--export-history")]
    [InlineData("--export-history", @"\Backup", "--output")]
    [InlineData("--export-history", @"\Backup", "backup.csv")]
    [InlineData("--export-history", @"\Backup", "--to", "backup.csv")]
    public void WhatIsNotUnderstood_GetsTheUsage_AndItsOwnExitCode(params string[] arguments)
    {
        Assert.Equal(CommandLine.UsageError, Run(arguments));
        Assert.Contains("usage:", _output.ToString());
        Assert.Empty(_store.Calls);
        Assert.Empty(_log.Queries);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_PrintsTheUsage_AndSucceeds(string switchName)
    {
        Assert.Equal(CommandLine.Success, Run(switchName));
        Assert.Contains("--list", _output.ToString());
        Assert.Contains("--export-history <task path> [--output <file>]", _output.ToString());
    }
}
