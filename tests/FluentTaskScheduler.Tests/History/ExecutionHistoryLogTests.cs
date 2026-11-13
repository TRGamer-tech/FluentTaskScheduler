using System.Text.Json;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.History;

public sealed class ExecutionHistoryLogTests : IDisposable
{
    private static readonly DateTimeOffset InNovember = new(2026, 11, 13, 14, 25, 38, TimeSpan.Zero);

    private readonly TempFolder _folder = new();
    private readonly FakeTaskEventStream _stream = new();
    private readonly SettingsStore _settings;
    private readonly ExecutionHistoryLog _log;

    public ExecutionHistoryLogTests()
    {
        _settings = new SettingsStore(_folder.Paths, new FakeTimeProvider());
        _settings.Update(s => s with { EnableExecutionHistoryLog = true });
        _log = new ExecutionHistoryLog(_stream, _settings, _folder.Paths);
    }

    public void Dispose()
    {
        _log.Dispose();
        _settings.Dispose();
        _folder.Dispose();
    }

    private string FileOf(string month) => Path.Combine(_folder.Paths.ExecutionHistoryFolder, $"runs-{month}.jsonl");

    private string[] FilesKept() =>
        Directory.GetFiles(_folder.Paths.ExecutionHistoryFolder).Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void AFinishedRun_BecomesOneLineOfJson_InTheFileOfTheMonthItEndedIn()
    {
        var instance = Guid.Parse("362395ca-4710-453e-aea6-4d0dd8bcc7b3");
        _stream.Complete(new TaskRun
        {
            TaskPath = @"\Backups\Nightly",
            Status = RunStatus.Failed,
            ResultCode = 3,
            InstanceId = instance,
            Started = InNovember.AddMinutes(-2),
            Ended = InNovember,
            User = @"PC\Zoë"
        });

        string line = Assert.Single(File.ReadAllLines(FileOf("2026-11")));
        using var json = JsonDocument.Parse(line);
        var entry = json.RootElement;
        Assert.Equal(@"\Backups\Nightly", entry.GetProperty("Task").GetString());
        Assert.Equal("Failed", entry.GetProperty("Result").GetString());
        Assert.Equal(3u, entry.GetProperty("ResultCode").GetUInt32());
        Assert.Equal(InNovember, entry.GetProperty("Ended").GetDateTimeOffset());
        Assert.Equal(InNovember.AddMinutes(-2), entry.GetProperty("Started").GetDateTimeOffset());
        Assert.Equal(instance, entry.GetProperty("InstanceId").GetGuid());
        Assert.Equal(@"PC\Zoë", entry.GetProperty("User").GetString());
    }

    [Theory]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.CouldNotStart)]
    [InlineData(RunStatus.Stopped)]
    [InlineData(RunStatus.TimedOut)]
    public void TheResult_IsWrittenAsTheNameOfTheStatus_InNoParticularLanguage(RunStatus status)
    {
        _stream.Complete(FakeTaskEventStream.Finished(@"\A", status, ended: InNovember));

        using var json = JsonDocument.Parse(Assert.Single(File.ReadAllLines(FileOf("2026-11"))));
        Assert.Equal(status.ToString(), json.RootElement.GetProperty("Result").GetString());
    }

    [Fact]
    public void ATaskThatCouldNotStart_IsRecordedWithoutAStartAndWithItsError()
    {
        _stream.Complete(new TaskRun { TaskPath = @"\A", Status = RunStatus.CouldNotStart, ResultCode = 0x8007052E, Ended = InNovember });

        using var json = JsonDocument.Parse(Assert.Single(File.ReadAllLines(FileOf("2026-11"))));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Started").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("InstanceId").ValueKind);
        Assert.Equal(0x8007052E, json.RootElement.GetProperty("ResultCode").GetUInt32());
    }

    [Fact]
    public void EveryRun_IsALineOfItsOwn_InTheOrderTheRunsEnded()
    {
        for (int i = 1; i <= 5; i++) _stream.Complete(FakeTaskEventStream.Finished($@"\Task {i}", RunStatus.Succeeded, ended: InNovember.AddMinutes(i)));

        string[] lines = File.ReadAllLines(FileOf("2026-11"));

        Assert.Equal(5, lines.Length);
        Assert.Equal([@"\Task 1", @"\Task 2", @"\Task 3", @"\Task 4", @"\Task 5"], lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("Task").GetString()));
    }

    [Fact]
    public void TheMonth_IsThatOfTheEndOfTheRun_InUtc()
    {
        // Half past midnight on 1 December in Berlin is still November in UTC.
        _stream.Complete(FakeTaskEventStream.Finished(@"\A", RunStatus.Succeeded, ended: new DateTimeOffset(2026, 12, 1, 0, 30, 0, TimeSpan.FromHours(1))));
        _stream.Complete(FakeTaskEventStream.Finished(@"\B", RunStatus.Succeeded, ended: new DateTimeOffset(2026, 12, 1, 1, 30, 0, TimeSpan.FromHours(1))));

        Assert.Equal(["runs-2026-11.jsonl", "runs-2026-12.jsonl"], FilesKept());
    }

    [Fact]
    public void WhileTheSettingIsOff_NothingIsWritten_AndChangingItTakesEffectWithTheNextRun()
    {
        _settings.Update(s => s with { EnableExecutionHistoryLog = false });
        _stream.Complete(FakeTaskEventStream.Finished(@"\A", RunStatus.Succeeded, ended: InNovember));
        Assert.False(Directory.Exists(_folder.Paths.ExecutionHistoryFolder));

        _settings.Update(s => s with { EnableExecutionHistoryLog = true });
        _stream.Complete(FakeTaskEventStream.Finished(@"\B", RunStatus.Succeeded, ended: InNovember));
        Assert.Single(File.ReadAllLines(FileOf("2026-11")));

        _settings.Update(s => s with { EnableExecutionHistoryLog = false });
        _stream.Complete(FakeTaskEventStream.Finished(@"\C", RunStatus.Succeeded, ended: InNovember));
        Assert.Single(File.ReadAllLines(FileOf("2026-11")));
    }

    [Fact]
    public void OnlyTheNewestTwelveMonths_AreKept_AndOtherFilesAreLeftAlone()
    {
        Directory.CreateDirectory(_folder.Paths.ExecutionHistoryFolder);
        for (var month = new DateTime(2025, 1, 1); month < new DateTime(2026, 11, 1); month = month.AddMonths(1)) File.WriteAllText(FileOf($"{month:yyyy-MM}"), "{}\n");
        File.WriteAllText(Path.Combine(_folder.Paths.ExecutionHistoryFolder, "history-2025-01.jsonl"), "not one of these");
        File.WriteAllText(Path.Combine(_folder.Paths.ExecutionHistoryFolder, "notes.txt"), "mine");

        _stream.Complete(FakeTaskEventStream.Finished(@"\A", RunStatus.Succeeded, ended: InNovember));

        string[] runs = FilesKept().Where(name => name.StartsWith("runs-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(ExecutionHistoryLog.MonthsKept, runs.Length);
        Assert.Equal("runs-2025-12.jsonl", runs[0]);
        Assert.Equal("runs-2026-11.jsonl", runs[^1]);
        Assert.Contains("history-2025-01.jsonl", FilesKept());
        Assert.Contains("notes.txt", FilesKept());
    }

    [Fact]
    public void WhenAFileCannotBeWritten_TheRunIsLost_ButNothingElseBreaks()
    {
        // A folder where the file should be makes the write fail.
        Directory.CreateDirectory(FileOf("2026-11"));

        _stream.Complete(FakeTaskEventStream.Finished(@"\A", RunStatus.Succeeded, ended: InNovember));
        _stream.Complete(FakeTaskEventStream.Finished(@"\B", RunStatus.Succeeded, ended: InNovember.AddMonths(1)));

        Assert.Single(File.ReadAllLines(FileOf("2026-12")));
    }

    [Fact]
    public void AfterDispose_RunsAreNoLongerRecorded()
    {
        _log.Dispose();

        _stream.Complete(FakeTaskEventStream.Finished(@"\A", RunStatus.Succeeded, ended: InNovember));

        Assert.Equal(0, _stream.Subscribers);
        Assert.False(Directory.Exists(_folder.Paths.ExecutionHistoryFolder));
    }
}
