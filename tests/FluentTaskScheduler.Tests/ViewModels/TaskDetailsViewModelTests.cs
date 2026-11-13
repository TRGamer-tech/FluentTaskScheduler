using System.Runtime.InteropServices;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.History;
using FluentTaskScheduler.Tests.Snoozing;
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
    private readonly FakeSnoozeService _snooze = new();
    private readonly FakeAppLifetime _app = new() { IsElevated = true };
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
        _details = Open(@"\Backup");
    }

    public void Dispose()
    {
        _details.Dispose();
        _folder.Dispose();
    }

    private TaskDetailsViewModel Open(string path, IUiThread? ui = null) =>
        new TaskDetailsFactory(_store, _history, _snooze, _files, _clipboard, _app, ui ?? new InlineUiThread(), _time).Create(new TaskRow(path));

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
    public async Task AnActionTheEditorCannotChange_IsNamedAsSuch()
    {
        _store.Add(@"\Windows", draft: new TaskDraft { Actions = [new ActionDraft { SourceIndex = 0, UnsupportedSummary = "ComHandler {0000}" }] });
        using TaskDetailsViewModel details = Open(@"\Windows");

        await details.LoadAsync();

        Assert.Equal(["Action.Unsupported (ComHandler {0000})"], details.Actions);
    }

    [Fact]
    public async Task ATaskThatCannotBeRead_StillShowsItsHistory()
    {
        using TaskDetailsViewModel details = Open(@"\Gone");
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

    /// <summary>Central European time: an hour ahead of UTC, two in summer, which begins on the last Sunday of March and ends on the last Sunday of October.</summary>
    private static TimeZoneInfo CentralEurope()
    {
        var summer = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Central Europe", TimeSpan.FromHours(1), "Central Europe", "CET", "CEST", [summer]);
    }

    [Theory]
    [InlineData("2031-03-30T10:00:00Z", HistoryPeriod.Today, "2031-03-30T00:00:00+01:00", null)]
    [InlineData("2031-10-26T11:00:00Z", HistoryPeriod.Today, "2031-10-26T00:00:00+02:00", null)]
    [InlineData("2031-03-31T10:00:00Z", HistoryPeriod.Yesterday, "2031-03-30T00:00:00+01:00", "2031-03-31T00:00:00+02:00")]
    [InlineData("2031-04-02T10:00:00Z", HistoryPeriod.LastSevenDays, "2031-03-27T00:00:00+01:00", null)]
    public async Task APeriodAroundAChangeOfDaylightSavingTime_StillBeginsAndEndsAtTheUsersMidnight(string now, HistoryPeriod period, string from, string? to)
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(now));
        time.SetLocalTimeZone(CentralEurope());
        using TaskDetailsViewModel details = new TaskDetailsFactory(_store, _history, _snooze, _files, _clipboard, _app, new InlineUiThread(), time)
            .Create(new TaskRow(@"\Backup"));
        details.PeriodIndex = (int)period;

        await details.LoadHistoryAsync();

        var asked = _history.Requests[^1];
        Assert.Equal(DateTimeOffset.Parse(from), asked.From);
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

    [Fact]
    public void ATaskSnoozedOnItsOwn_SaysUntilWhen_AndTheSnoozeOfAllTasksIsNotItsOwn()
    {
        Assert.False(_details.IsSnoozed);
        Assert.Equal("", _details.SnoozeStatus);

        _snooze.Publish(new SnoozeState { Global = new GlobalSnooze(SnoozeWindow.UntilRestart, SuspendsTriggers: true, [@"\Backup"]) });
        Assert.False(_details.IsSnoozed);

        _snooze.Publish(new SnoozeState { Tasks = [new TaskSnooze(@"\backup", new SnoozeWindow(_time.GetUtcNow().AddHours(1)))] });
        Assert.True(_details.IsSnoozed);
        Assert.Equal("Task.Snooze.Status", _details.SnoozeStatus);
    }

    [Fact]
    public async Task SnoozingTheTask_AsksForTheChosenLength()
    {
        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.ThreeHours);
        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.UntilRestart);

        Assert.Equal([@"SnoozeTask \Backup for 03:00:00", @"SnoozeTask \Backup until restart"], _snooze.Calls);
        Assert.False(_details.HasSnoozeProblem);
    }

    [Fact]
    public async Task ATaskThatIsDisabledAlready_IsNotSnoozed_AndTheDetailsSayWhy()
    {
        _snooze.TaskOutcome = SnoozeTaskOutcome.AlreadyDisabled;

        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);

        Assert.Equal("Task.Snooze.AlreadyDisabled", _details.SnoozeProblem);
        Assert.True(_details.HasSnoozeProblem);
    }

    [Theory]
    [InlineData(true, "Tasks.Error.Denied")]
    [InlineData(false, "Tasks.Error.DeniedNotElevated")]
    public async Task ATaskWindowsRefusesToDisable_IsNotSnoozed_AndTheDetailsSayWhetherElevationWouldHelp(bool elevated, string expected)
    {
        _app.IsElevated = elevated;
        _snooze.Fails = new TaskAccessDeniedException(@"\Backup", new UnauthorizedAccessException());

        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);

        Assert.Equal(expected, _details.SnoozeProblem);
    }

    [Fact]
    public async Task ATaskThatIsGone_OrASnoozeThatCannotBeSaved_IsReportedAsSuch()
    {
        _snooze.Fails = new TaskNotFoundException(@"\Backup");
        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);
        Assert.Equal("Tasks.Error.Gone", _details.SnoozeProblem);

        _snooze.Fails = new IOException("There is not enough space on the disk.");
        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);
        Assert.Equal("Snooze.Error.NotRecorded", _details.SnoozeProblem);
    }

    [Fact]
    public async Task AnyOtherErrorOfTaskScheduler_IsShownAsAProblemOfTheTask()
    {
        _snooze.Fails = new COMException("The task image is corrupt or has been tampered with.", unchecked((int)0x80041321));

        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);

        Assert.Equal("Backup: The task image is corrupt or has been tampered with.", _details.SnoozeProblem);
    }

    [Fact]
    public async Task Resuming_EndsTheTasksOwnSnooze_AndForgetsAnEarlierProblem()
    {
        _snooze.TaskOutcome = SnoozeTaskOutcome.AlreadyDisabled;
        await _details.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);

        await _details.ResumeSnoozeCommand.ExecuteAsync(null);

        Assert.Equal(@"ResumeTask \Backup", _snooze.Calls.Last());
        Assert.False(_details.HasSnoozeProblem);
    }

    [Fact]
    public async Task TheDialog_SnoozesTheTaskUntilTheMomentPicked_InTheTimeOfThisComputer()
    {
        SnoozeUntilViewModel dialog = _details.PickSnoozeMoment();

        // It is 14:00 here; the dialog offers 15:00.
        Assert.Equal(new TimeSpan(15, 0, 0), dialog.TimeOfDay);
        dialog.TimeOfDay = new TimeSpan(16, 30, 0);

        Assert.True(await dialog.SnoozeAsync());

        Assert.Equal([@"SnoozeTask \Backup until 2031-05-06 16:30 +02:00"], _snooze.Calls);
        Assert.Equal("Backup", dialog.Title);
        Assert.Equal("Task.Snooze.CustomIntro", dialog.UntilText);
        Assert.False(dialog.IsForAllTasks);
    }

    [Fact]
    public async Task TheDialog_StaysOpenForATaskThatIsDisabledAlready_AndSaysWhy()
    {
        _snooze.TaskOutcome = SnoozeTaskOutcome.AlreadyDisabled;
        SnoozeUntilViewModel dialog = _details.PickSnoozeMoment();

        Assert.False(await dialog.SnoozeAsync());

        Assert.Equal("Task.Snooze.AlreadyDisabled", dialog.Problem);
    }

    [Fact]
    public async Task ClosingTheDetails_CancelsTheReadOfTheHistory_AndShowsNothingOfIt()
    {
        _history.Events.Add(new TaskEvent { Kind = TaskEventKind.RunStarted, EventId = 100, Time = _time.GetUtcNow().AddHours(-1), TaskPath = @"\Backup", RecordId = 1 });
        using var reading = new ManualResetEventSlim();
        CancellationToken read = default;
        _history.ReadingEvents = cancellation =>
        {
            read = cancellation;
            reading.Set();
            cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        };
        TaskDetailsViewModel details = Open(@"\Backup");
        Task loading = details.LoadHistoryAsync();
        Assert.True(reading.Wait(TimeSpan.FromSeconds(5)));

        details.Dispose();
        await loading;

        Assert.True(read.IsCancellationRequested);
        Assert.Empty(details.Entries);
    }

    [Fact]
    public void ClosedDetails_NoLongerFollowTheSnooze_EvenWithAChangePostedBeforeTheyClosed()
    {
        int listening = _snooze.Subscribers;
        var ui = new BusyUiThread();
        TaskDetailsViewModel details = Open(@"\Backup", ui);

        _snooze.Publish(new SnoozeState { Tasks = [new TaskSnooze(@"\Backup", SnoozeWindow.UntilRestart)] });
        details.Dispose();
        ui.RunPosted();

        Assert.False(details.IsSnoozed);
        Assert.Equal(listening, _snooze.Subscribers);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "It did not happen within two seconds.");
    }
}

/// <summary>Apart from the other tests, for the reason <see cref="LocalizedTextCollection"/> gives.</summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class TaskDetailsTimeZoneTests
{
    [Fact]
    public async Task TheHistory_IsReadInTheTimeZoneTheComputerUsesNow()
    {
        using TaskDetailsViewModel details = new TaskDetailsFactory(new FakeTaskStore(), new FakeTaskHistory(), new FakeSnoozeService(),
            new ScriptedFilePicker(), new FakeClipboard(), new FakeAppLifetime(), new InlineUiThread(), new FakeTimeProvider()).Create(new TaskRow(@"\Backup"));

        // .NET keeps the zone it read first until its cache is cleared, and reads it into a new
        // object afterwards: that new object is what a test can see of a fresh read.
        TimeZoneInfo readBefore = TimeZoneInfo.Local;

        await details.LoadHistoryAsync();

        Assert.NotSame(readBefore, TimeZoneInfo.Local);
    }
}
