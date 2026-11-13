using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.History;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class TaskDetailsViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeTaskHistory _history = new();
    private readonly ScriptedFilePicker _files = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2031, 5, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly TaskDetailsViewModel _details;

    // What a Windows machine really logged for a run that returned 0 and one that returned 3.
    private static readonly List<TaskEvent> Succeeded = EventsOf("A_ExitZero");
    private static readonly List<TaskEvent> Failed = EventsOf("B_ExitThree");

    public TaskDetailsViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));
        _store.Add(@"\Backup", draft: new TaskDraft
        {
            Triggers = [new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2031, 1, 1, 3, 0, 0) }],
            Actions = [new ActionDraft { Path = "robocopy", Arguments = @"C:\a D:\b /MIR" }, new ActionDraft { Path = "notepad.exe" }]
        });
        _details = new TaskDetailsFactory(_store, _history, _files, _clipboard, _time).Create(new TaskRow(@"\Backup"));
    }

    public void Dispose() => _folder.Dispose();

    private static List<TaskEvent> EventsOf(string task) =>
        Evidence.Of(task).Select(TaskEvent.From).OfType<TaskEvent>().ToList();

    [Fact]
    public async Task Opening_ShowsWhatStartsTheTask_AndWhatItRuns()
    {
        await _details.LoadAsync();

        Assert.Single(_details.Triggers);
        Assert.Equal([@"robocopy C:\a D:\b /MIR", "notepad.exe"], _details.Actions);
    }

    [Fact]
    public async Task ATaskThatCannotBeRead_StillShowsItsHistory()
    {
        var details = new TaskDetailsFactory(_store, _history, _files, _clipboard, _time).Create(new TaskRow(@"\Gone"));
        _history.Events.AddRange(Succeeded);
        details.PeriodIndex = (int)HistoryPeriod.Everything;

        await details.LoadAsync();

        Assert.Empty(details.Triggers);
        Assert.Equal(Succeeded.Count, details.Entries.Count);
    }

    [Fact]
    public async Task TheHistory_ListsEveryEvent_NewestFirst_AndCountsTheRuns()
    {
        _history.Events.AddRange(Succeeded);
        _history.Events.AddRange(Failed);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;

        await _details.LoadHistoryAsync();

        Assert.Equal(Succeeded.Count + Failed.Count, _details.Entries.Count);
        Assert.Equal(_details.Entries.Select(entry => entry.Event.Time).OrderByDescending(time => time), _details.Entries.Select(entry => entry.Event.Time));
        Assert.Equal((2, 1, 1), (_details.TotalRuns, _details.SucceededRuns, _details.FailedRuns));
        Assert.False(_details.ShowsNoHistory);
    }

    [Fact]
    public async Task TheLastResult_IsThatOfTheNewestRun_WithItsCode()
    {
        _history.Events.AddRange(Failed);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;

        await _details.LoadHistoryAsync();

        Assert.Equal("RunStatus.Failed (3)", _details.LastResult);
    }

    [Fact]
    public async Task TheFilter_KeepsTheEventsOfRunsThatEndedThatWay()
    {
        _history.Events.AddRange(Succeeded);
        _history.Events.AddRange(Failed);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;
        await _details.LoadHistoryAsync();

        _details.ShowCommand.Execute(HistoryFilter.Failed);
        string[] failedOnly = _details.Entries.Select(entry => entry.Event.TaskPath).Distinct().ToArray();
        _details.ShowCommand.Execute(HistoryFilter.Succeeded);
        string[] succeededOnly = _details.Entries.Select(entry => entry.Event.TaskPath).Distinct().ToArray();
        _details.ShowCommand.Execute(HistoryFilter.All);

        Assert.Equal([Failed[0].TaskPath], failedOnly);
        Assert.Equal([Succeeded[0].TaskPath], succeededOnly);
        Assert.Equal(Succeeded.Count + Failed.Count, _details.Entries.Count);
    }

    [Theory]
    [InlineData(HistoryPeriod.Today, "2031-05-06T00:00:00+02:00", null)]
    [InlineData(HistoryPeriod.Yesterday, "2031-05-05T00:00:00+02:00", "2031-05-06T00:00:00+02:00")]
    [InlineData(HistoryPeriod.LastSevenDays, "2031-04-30T00:00:00+02:00", null)]
    [InlineData(HistoryPeriod.Everything, null, null)]
    public async Task APeriod_BeginsAndEndsAtTheUsersMidnight(HistoryPeriod period, string? from, string? to)
    {
        _details.PeriodIndex = (int)period;

        await _details.LoadHistoryAsync();

        var asked = _history.Requests[^1];
        Assert.Equal(from is null ? null : DateTimeOffset.Parse(from), asked.From);
        Assert.Equal(to is null ? null : DateTimeOffset.Parse(to), asked.To);
    }

    [Fact]
    public async Task ChoosingAnotherPeriod_ReadsTheHistoryAgain()
    {
        await _details.LoadHistoryAsync();
        int before = _history.Requests.Count;

        _details.PeriodIndex = (int)HistoryPeriod.Today;
        await Eventually(() => _history.Requests.Count > before && !_details.IsLoading);

        Assert.Equal((int)HistoryPeriod.Today, _details.PeriodIndex);
    }

    [Fact]
    public void APeriodThatDoesNotExist_IsNotTaken()
    {
        _details.PeriodIndex = -1;

        Assert.Equal((int)HistoryPeriod.LastSevenDays, _details.PeriodIndex);
    }

    [Fact]
    public async Task WhenWindowsRecordsNoHistory_TheUserIsTold_AndCanSwitchItOn()
    {
        _history.State = LogState.Disabled;
        await _details.LoadHistoryAsync();
        Assert.True(_details.IsLogOff);

        await _details.SwitchLogOnCommand.ExecuteAsync(null);

        Assert.False(_details.IsLogOff);
        Assert.False(_details.HasProblem);
    }

    [Fact]
    public async Task SwitchingHistoryOn_WithoutTheRightToDoSo_SaysSo()
    {
        _history.State = LogState.Disabled;
        _history.EnableResult = EnableLogResult.AccessDenied;
        await _details.LoadHistoryAsync();

        await _details.SwitchLogOnCommand.ExecuteAsync(null);

        Assert.True(_details.IsLogOff);
        Assert.Equal("Details.History.SwitchOnDenied", _details.Problem);
    }

    [Fact]
    public async Task AHistoryThatCannotBeRead_IsAProblem_NotAnEmptyHistory()
    {
        _history.Unreadable = true;

        await _details.LoadHistoryAsync();

        Assert.True(_details.HasProblem);
        Assert.Empty(_details.Entries);
        Assert.False(_details.IsLoading);
    }

    [Fact]
    public async Task SelectingAnEvent_ShowsItsFacts_AndWhatHappenedToTheComputerAroundIt()
    {
        _history.Events.AddRange(Failed);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;
        await _details.LoadHistoryAsync();
        HistoryEntry entry = _details.Entries.First(candidate => candidate.Event.ResultCode == 3);
        _history.SystemEvents.Add(new SystemEvent(SystemEventKind.Resume, entry.Event.Time.AddMinutes(-2)));
        _history.SystemEvents.Add(new SystemEvent(SystemEventKind.Startup, entry.Event.Time.AddHours(-3)));

        _details.Selected = entry;
        await Eventually(() => _details.SelectedSurroundings.Count > 0);

        Assert.Contains(entry.Label, _details.SelectedFacts);
        Assert.Contains("3", _details.SelectedFacts);
        Assert.Single(_details.SelectedSurroundings);
        Assert.EndsWith("SystemEvent.Resume", _details.SelectedSurroundings[0]);
    }

    [Fact]
    public async Task Export_WritesTheListedEventsToTheChosenFile()
    {
        _history.Events.AddRange(Succeeded);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;
        await _details.LoadHistoryAsync();
        _files.Choice = _folder.File("history.csv");

        await _details.ExportCsvCommand.ExecuteAsync(null);

        string[] lines = File.ReadAllLines(_files.Choice);
        Assert.Equal(Succeeded.Count + 1, lines.Length);
    }

    [Fact]
    public async Task Export_Cancelled_WritesNothing()
    {
        await _details.LoadHistoryAsync();
        _files.Choice = null;

        await _details.ExportCsvCommand.ExecuteAsync(null);

        Assert.False(_details.HasProblem);
        Assert.Empty(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task Copy_PutsOneLinePerEventOnTheClipboard()
    {
        _history.Events.AddRange(Succeeded);
        _details.PeriodIndex = (int)HistoryPeriod.Everything;
        await _details.LoadHistoryAsync();

        _details.CopyCommand.Execute(null);

        Assert.Equal(Succeeded.Count, _clipboard.Text!.Split(Environment.NewLine).Length);
        Assert.Contains("\t", _clipboard.Text);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "It did not happen within two seconds.");
    }
}
