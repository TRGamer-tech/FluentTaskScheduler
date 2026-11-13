using System.Collections.Specialized;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.History;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class DashboardViewModelTests : IDisposable
{
    // The recorded log of a Windows machine: 21 runs between 14:25 and 14:27 UTC, of which 7
    // succeeded, 6 failed, 2 could not start, 1 timed out and 5 were stopped.
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Succeeds = Evidence.Folder + @"\A_ExitZero";
    private static readonly string Fails = Evidence.Folder + @"\B_ExitThree";

    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeTaskHistory _history = new();
    private readonly FakeTaskEventStream _stream = new();
    private readonly RecordingNavigator _navigator = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly SettingsStore _settings;
    private readonly SnoozeService _snooze;
    private readonly DashboardViewModel _dashboard;

    public DashboardViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test"));
        _store.Add(Succeeds, draft: new TaskDraft { Category = "Reports", Tags = ["nightly"] })
              .Add(Fails, draft: new TaskDraft { Category = "Backups", Tags = ["nightly", "disk"] })
              .Add(@"\Idle", enabled: false);
        _history.Events.AddRange(Evidence.Log.Select(TaskEvent.From).OfType<TaskEvent>());
        _settings = new SettingsStore(_folder.Paths, _time);
        _snooze = new SnoozeService(_store, _settings, new RecordingNotifier(), new FakeWindowsSession(), _folder.Paths, _time);
        _dashboard = NewDashboard(new InlineUiThread());
    }

    public void Dispose()
    {
        _dashboard.Dispose();
        _snooze.Dispose();
        _settings.Dispose();
        _folder.Dispose();
    }

    private DashboardViewModel NewDashboard(IUiThread ui) => new(_store, _history, _stream, _snooze, _navigator, ui, _time);

    private DashboardFigures Figures => _dashboard.Figures;

    private async Task AfterTheRefreshDelayAsync()
    {
        _time.Advance(DashboardViewModel.RefreshDelay);
        await _dashboard.PendingRefresh;
    }

    [Fact]
    public async Task ComingOnScreen_CountsTheTasks_AndTheRunsOfTheLastSevenDays()
    {
        _store.Run(Fails);
        Assert.False(_dashboard.HasFigures);

        await _dashboard.ShowAsync();

        Assert.Equal((3, 2, 1), (Figures.TotalTasks, Figures.EnabledTasks, Figures.RunningTasks));
        Assert.Equal((7, 9), (Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(44, Figures.HealthScore);
        Assert.Equal((21, 21), (Figures.RunsInPeriod, Figures.RunsLastDay));
        Assert.Equal(21, _dashboard.Log.Count);
        Assert.Equal(Fails, Assert.Single(_dashboard.Running).TaskPath);
        Assert.True(_dashboard.HasFigures);
        Assert.False(_dashboard.IsLoading);
        Assert.False(_dashboard.HasProblem);
    }

    [Fact]
    public async Task WhatHappensWhileEverythingIsFirstRead_IsShownWithIt_AndNothingIsShownBefore()
    {
        using var listed = new ManualResetEventSlim();
        using var read = new ManualResetEventSlim();
        _store.Listed = listed.Wait;
        _history.Reading = _ => read.Wait();

        Task showing = _dashboard.ShowAsync();
        _stream.Complete(FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: Now));
        _snooze.RecordSuppressedRun(Fails, RunOrigin.Manual);
        _dashboard.Filter = LogFilter.Succeeded;

        Assert.True(_dashboard.IsLoading);
        Assert.False(_dashboard.HasFigures);
        Assert.Empty(_dashboard.Log);

        listed.Set();
        read.Set();
        await showing;

        Assert.Equal(3, Figures.TotalTasks);
        Assert.Equal((8, 9), (Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(8, _dashboard.Log.Count);
        Assert.All(_dashboard.Log, entry => Assert.Equal(LogOutcome.Succeeded, entry.Outcome));
    }

    [Fact]
    public async Task WhileEverythingIsReadAnew_TheFiguresStay_AndNoRunReportedMeanwhileIsLost()
    {
        await _dashboard.ShowAsync();

        // A run ends; Task Scheduler logs it, and then it is reported.
        TaskRun logged = FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: Now);
        _history.Events.Add(new TaskEvent { Kind = TaskEventKind.RunStarted, EventId = 100, Time = logged.Started!.Value, TaskPath = Succeeds, InstanceId = logged.InstanceId, RecordId = 500 });
        _history.Events.Add(new TaskEvent { Kind = TaskEventKind.RunCompleted, EventId = 102, Time = Now, TaskPath = Succeeds, InstanceId = logged.InstanceId, RecordId = 501 });
        _stream.Complete(logged);
        LogEntry newest = _dashboard.Log[0];

        using var read = new ManualResetEventSlim();
        _history.Reading = _ => read.Wait();
        Task loading = _dashboard.LoadAsync();
        _stream.Complete(FakeTaskEventStream.Finished(Fails, RunStatus.Failed, resultCode: 3, ended: Now.AddSeconds(1)));

        Assert.Equal((22, 8, 9), (Figures.RunsInPeriod, Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Same(newest, _dashboard.Log[0]);

        read.Set();
        await loading;

        Assert.Equal((23, 8, 10), (Figures.RunsInPeriod, Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(23, _dashboard.Log.Count);
    }

    [Fact]
    public async Task ARefreshAskedForWhileEverythingIsRead_DoesNotReadItAllASecondTime()
    {
        using var read = new ManualResetEventSlim();
        _history.Reading = _ => read.Wait();
        Task showing = _dashboard.ShowAsync();

        Task refreshing = _dashboard.LoadAsync();

        Assert.True(refreshing.IsCompletedSuccessfully);
        read.Set();
        await showing;
        Assert.Equal(1, _store.Listings);
    }

    [Fact]
    public async Task TheHoursOfTheRuns_AreThoseOfTheUsersClock()
    {
        await _dashboard.ShowAsync();

        // 14:25 UTC is 15:25 where the user is.
        Assert.Equal(24, _dashboard.Hours.Count);
        Assert.Equal((21, 1.0), (_dashboard.Hours[15].Runs, _dashboard.Hours[15].Shade));
        Assert.Equal(0, _dashboard.Hours[14].Runs);
        Assert.Equal("15:00", Figures.BusiestHour);
    }

    [Fact]
    public async Task TheLog_CanBeNarrowedToWhatSucceeded_WhatWentWrong_OrWhatASnoozeRefused()
    {
        await _dashboard.ShowAsync();
        _snooze.RecordSuppressedRun(Succeeds, RunOrigin.Manual);

        _dashboard.Filter = LogFilter.Succeeded;
        Assert.Equal(7, _dashboard.Log.Count);
        Assert.All(_dashboard.Log, entry => Assert.Equal(LogOutcome.Succeeded, entry.Outcome));

        _dashboard.Filter = LogFilter.Failed;
        Assert.Equal(9, _dashboard.Log.Count);
        Assert.All(_dashboard.Log, entry => Assert.Equal(LogOutcome.Failed, entry.Outcome));

        _dashboard.Filter = LogFilter.Snoozed;
        Assert.Equal(Succeeds, Assert.Single(_dashboard.Log).TaskPath);

        // The counts stay those of all runs.
        Assert.Equal((7, 9), (Figures.SucceededRuns, Figures.FailedRuns));
    }

    [Fact]
    public async Task AStartASnoozeRefusesWhileTheDashboardIsOnScreen_IsLoggedAtOnce()
    {
        await _dashboard.ShowAsync();

        _snooze.RecordSuppressedRun(Fails, RunOrigin.CompletionAction);

        LogEntry newest = _dashboard.Log[0];
        Assert.Equal((Fails, LogOutcome.Snoozed, "Dashboard.Refused.CompletionAction"), (newest.TaskPath, newest.Outcome, newest.Detail));
        Assert.Equal(22, _dashboard.Log.Count);
        Assert.Equal(21, Figures.RunsInPeriod);
    }

    [Fact]
    public async Task ACategoryOrATag_NarrowsTheTasks_TheirRuns_AndTheStartsRefusedForThem()
    {
        await _dashboard.ShowAsync();
        _snooze.RecordSuppressedRun(Fails, RunOrigin.Manual);
        Assert.Equal(["Dashboard.AllCategories", "Backups", "Reports"], _dashboard.Categories);
        Assert.Equal(["Dashboard.AllTags", "disk", "nightly"], _dashboard.Tags);

        _dashboard.CategoryIndex = 2;
        Assert.Equal((1, 1, 0), (Figures.TotalTasks, Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(100, Figures.HealthScore);
        Assert.DoesNotContain(_dashboard.Log, entry => entry.Outcome == LogOutcome.Snoozed);

        _dashboard.CategoryIndex = 0;
        _dashboard.TagIndex = 1;
        Assert.Equal((1, 0, 1), (Figures.TotalTasks, Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Contains(_dashboard.Log, entry => entry.Outcome == LogOutcome.Snoozed);
    }

    [Fact]
    public async Task AChoiceThatNoTaskHasAnyMore_FallsBackToAll_AndOneThatMovedStaysChosen()
    {
        await _dashboard.ShowAsync();
        _dashboard.CategoryIndex = 2;
        _dashboard.TagIndex = 2;

        // "Reports" moves down the list; the tag "nightly" disappears.
        _store.Add(@"\New", draft: new TaskDraft { Category = "Other" });
        _store.Add(Succeeds, draft: new TaskDraft { Category = "Reports" });
        _store.Add(Fails, draft: new TaskDraft { Category = "Backups", Tags = ["disk"] });
        await _dashboard.LoadAsync();

        Assert.Equal(["Dashboard.AllCategories", "Backups", "Other", "Reports"], _dashboard.Categories);
        Assert.Equal(3, _dashboard.CategoryIndex);
        Assert.Equal(["Dashboard.AllTags", "disk"], _dashboard.Tags);
        Assert.Equal(0, _dashboard.TagIndex);
        Assert.Equal(1, Figures.TotalTasks);
    }

    [Fact]
    public async Task AListControlThatLosesItsSelection_MeansAll()
    {
        await _dashboard.ShowAsync();
        _dashboard.CategoryIndex = 1;

        _dashboard.CategoryIndex = -1;

        Assert.Equal(0, _dashboard.CategoryIndex);
        Assert.Equal(3, Figures.TotalTasks);
    }

    [Fact]
    public async Task ARunThatEndsWhileTheDashboardIsOnScreen_IsCountedAtOnce_AndTheHistoryIsNotReadAgain()
    {
        await _dashboard.ShowAsync();
        _history.Events.Clear();

        _stream.Complete(FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: Now));

        Assert.Equal((8, 9), (Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(Succeeds, _dashboard.Log[0].TaskPath);
    }

    [Fact]
    public async Task ARunIsCountedOnce_HoweverOftenItIsHeardOf()
    {
        // A run that is going on when the history is read ...
        var instance = Guid.NewGuid();
        _history.Events.Add(new TaskEvent { Kind = TaskEventKind.RunStarted, EventId = 100, Time = Now.AddMinutes(-1), TaskPath = Fails, InstanceId = instance, RecordId = 500 });
        _store.Run(Fails);
        await _dashboard.ShowAsync();
        Assert.Equal(22, Figures.RunsInPeriod);

        // ... is reported again as starting, and then as over.
        _stream.Start(FakeTaskEventStream.Running(Fails, instance, started: Now.AddMinutes(-1)));
        _stream.Complete(FakeTaskEventStream.Finished(Fails, RunStatus.Failed, instance, resultCode: 3, ended: Now));

        Assert.Equal(22, Figures.RunsInPeriod);
        Assert.Equal((7, 10), (Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(22, _dashboard.Log.Count);
    }

    [Fact]
    public async Task ANewRun_IsAddedToTheLog_WithoutTouchingTheEntriesThatStayTheSame()
    {
        await _dashboard.ShowAsync();
        LogEntry newestBefore = _dashboard.Log[0];
        var changes = new List<NotifyCollectionChangedAction>();
        _dashboard.Log.CollectionChanged += (_, e) => changes.Add(e.Action);

        _stream.Complete(FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: Now));

        Assert.Equal([NotifyCollectionChangedAction.Add], changes);
        Assert.Equal(Succeeds, _dashboard.Log[0].TaskPath);
        Assert.Same(newestBefore, _dashboard.Log[1]);
    }

    [Fact]
    public async Task AfterReports_TheTasksAreReadAgain_OnceForAllThatCameTogether()
    {
        await _dashboard.ShowAsync();
        Assert.Equal(1, _store.Listings);
        _store.Add(@"\New");

        _stream.Change(new TaskChange(@"\New", TaskChangeKind.Registered, Now));
        _stream.Change(new TaskChange(@"\New", TaskChangeKind.Updated, Now));
        _stream.Start(FakeTaskEventStream.Running(Succeeds));
        Assert.Equal(3, Figures.TotalTasks);

        await AfterTheRefreshDelayAsync();

        Assert.Equal(4, Figures.TotalTasks);
        Assert.Equal(2, _store.Listings);
    }

    [Fact]
    public async Task AfterRunsAreReported_OnlyTheTasksThatRanAreReadAgain_OfThoseTheDashboardHas()
    {
        await _dashboard.ShowAsync();
        _store.Run(Succeeds);

        _stream.Start(FakeTaskEventStream.Running(Succeeds, started: Now));
        _stream.Start(FakeTaskEventStream.Running(@"\Microsoft\Windows\Defrag\ScheduledDefrag", started: Now));
        await AfterTheRefreshDelayAsync();

        Assert.Equal([Succeeds], _store.TasksRead);
        Assert.Equal(1, _store.Listings);
        Assert.Equal(Succeeds, Assert.Single(_dashboard.Running).TaskPath);
    }

    [Fact]
    public async Task ATaskThatIsGoneWhenItIsReadAgainAfterARun_IsNoLongerCounted()
    {
        await _dashboard.ShowAsync();
        _store.Delete(Fails);

        _stream.Complete(FakeTaskEventStream.Finished(Fails, RunStatus.Failed, resultCode: 3, ended: Now));
        await AfterTheRefreshDelayAsync();

        Assert.Equal(2, Figures.TotalTasks);
        Assert.Equal(["Dashboard.AllCategories", "Reports"], _dashboard.Categories);
    }

    [Fact]
    public async Task ASlowReadOfAllTasks_CannotOverwriteWhatALaterReadFound()
    {
        await _dashboard.ShowAsync();

        // A task is registered. The read that follows has listed the tasks, but not handed them
        // over yet when the next task is registered and read.
        using var listing = new ManualResetEventSlim();
        using var handOver = new ManualResetEventSlim();
        _store.Listed = () =>
        {
            _store.Listed = null;
            listing.Set();
            handOver.Wait();
        };
        _store.Add(@"\First");
        _stream.Change(new TaskChange(@"\First", TaskChangeKind.Registered, Now));
        _time.Advance(DashboardViewModel.RefreshDelay);
        Task slow = _dashboard.PendingRefresh;
        listing.Wait();

        _store.Add(@"\Second");
        _stream.Change(new TaskChange(@"\Second", TaskChangeKind.Registered, Now));
        _time.Advance(DashboardViewModel.RefreshDelay);
        Task later = _dashboard.PendingRefresh;

        // A later read that went ahead without waiting for the slow one has shown its tasks by now.
        await Task.WhenAny(later, Task.Delay(TimeSpan.FromMilliseconds(250)));
        handOver.Set();
        await Task.WhenAll(slow, later);

        Assert.Equal(5, Figures.TotalTasks);
    }

    [Fact]
    public async Task OffScreen_RunsAreTakenNoteOf_ButNothingIsRead_AndBackOnScreenOnlyTheTasksAre()
    {
        await _dashboard.ShowAsync();
        var later = Now.AddMinutes(1);
        _stream.Change(new TaskChange(Fails, TaskChangeKind.Updated, later));
        _dashboard.Hide();
        _history.Events.Clear();

        _stream.Complete(FakeTaskEventStream.Finished(Fails, RunStatus.CouldNotStart, ended: later));
        await AfterTheRefreshDelayAsync();
        Assert.Equal(1, _store.Listings);
        Assert.Equal(9, Figures.FailedRuns);

        await _dashboard.ShowAsync();

        Assert.Equal(2, _store.Listings);
        Assert.Equal((7, 10), (Figures.SucceededRuns, Figures.FailedRuns));
    }

    [Fact]
    public async Task RunsThatEndedMoreThanSevenDaysAgo_NoLongerCount()
    {
        await _dashboard.ShowAsync();
        _time.Advance(TimeSpan.FromDays(7));

        _stream.Complete(FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: _time.GetUtcNow()));

        Assert.Equal((1, 0), (Figures.SucceededRuns, Figures.FailedRuns));
        Assert.Equal(1, Figures.RunsInPeriod);
        Assert.Single(_dashboard.Log);
    }

    [Fact]
    public async Task ARunWhoseEndNeverReachedTheLog_IsOver_OnceItsTaskIsSeenIdle()
    {
        await _dashboard.ShowAsync();

        // Task Scheduler stops logging while two tasks run: their starts are reported, their ends
        // never are. One of the tasks is one this user cannot see.
        const string unseen = @"\Microsoft\Windows\Defrag\ScheduledDefrag";
        _stream.Start(FakeTaskEventStream.Running(Succeeds, started: Now));
        _stream.Start(FakeTaskEventStream.Running(unseen, started: Now));
        Assert.Equal((23, 23), (Figures.RunsInPeriod, Figures.RunsLastDay));

        _dashboard.Hide();
        _time.Advance(TimeSpan.FromDays(2));
        await _dashboard.ShowAsync();

        // Of the other run nothing more is known than before.
        Assert.Equal((22, 1), (Figures.RunsInPeriod, Figures.RunsLastDay));
        Assert.Equal(unseen, Assert.Single(_dashboard.Log, entry => entry.Outcome == LogOutcome.Running).TaskPath);
    }

    [Fact]
    public async Task ARunCutShortByAPowerFailure_StaysOver_WhenItsTaskRunsAgain()
    {
        // The computer lost power two days ago while a task ran: the log has its start, nothing more.
        _history.Events.Add(new TaskEvent { Kind = TaskEventKind.RunStarted, EventId = 100, Time = Now.AddDays(-2), TaskPath = Succeeds, InstanceId = Guid.NewGuid(), RecordId = 600 });
        await _dashboard.ShowAsync();
        Assert.Equal((21, 21), (Figures.RunsInPeriod, Figures.RunsLastDay));
        Assert.DoesNotContain(_dashboard.Log, entry => entry.Outcome == LogOutcome.Running);

        _time.Advance(TimeSpan.FromMinutes(1));
        _store.Run(Succeeds);
        _stream.Start(FakeTaskEventStream.Running(Succeeds, started: _time.GetUtcNow()));
        _stream.Change(new TaskChange(Fails, TaskChangeKind.Updated, _time.GetUtcNow()));
        await AfterTheRefreshDelayAsync();

        Assert.Equal(22, Figures.RunsInPeriod);
        Assert.Single(_dashboard.Log, entry => entry.Outcome == LogOutcome.Running);
    }

    [Fact]
    public async Task AShortRun_IsNotTakenForOneWhoseEndWasLost_BeforeItsEndIsReported()
    {
        await _dashboard.ShowAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        // The run is over by the time its task is read again; its end is reported a moment later.
        _stream.Start(FakeTaskEventStream.Running(Succeeds, started: _time.GetUtcNow()));
        await AfterTheRefreshDelayAsync();

        Assert.Equal((Succeeds, LogOutcome.Running), (_dashboard.Log[0].TaskPath, _dashboard.Log[0].Outcome));
        Assert.Equal(22, Figures.RunsInPeriod);
    }

    [Fact]
    public async Task WhenWindowsRecordsNoHistory_TheDashboardSaysSo_AndCanSwitchItOn()
    {
        _history.State = LogState.Disabled;
        await _dashboard.ShowAsync();
        Assert.True(_dashboard.IsLogOff);

        await _dashboard.SwitchLogOnCommand.ExecuteAsync(null);

        Assert.False(_dashboard.IsLogOff);
        Assert.False(_dashboard.HasProblem);
    }

    [Theory]
    [InlineData(EnableLogResult.AccessDenied, "Details.History.SwitchOnDenied")]
    [InlineData(EnableLogResult.Failed, "Details.History.SwitchOnFailed")]
    public async Task WhenHistoryCannotBeSwitchedOn_TheDashboardSaysWhy(EnableLogResult result, string expected)
    {
        _history.State = LogState.Disabled;
        _history.EnableResult = result;
        await _dashboard.ShowAsync();

        await _dashboard.SwitchLogOnCommand.ExecuteAsync(null);

        Assert.True(_dashboard.IsLogOff);
        Assert.Equal(expected, _dashboard.Problem);
    }

    [Fact]
    public async Task HistorySwitchedOnElsewhere_IsNoticedWhenTheDashboardComesBack()
    {
        _history.State = LogState.Disabled;
        await _dashboard.ShowAsync();
        _dashboard.Hide();

        _history.State = LogState.Enabled;
        await _dashboard.ShowAsync();

        Assert.False(_dashboard.IsLogOff);
    }

    [Fact]
    public async Task WhenTheHistoryCannotBeRead_TheTasksAreStillCounted_AndTheProblemIsShown()
    {
        _history.Unreadable = true;

        await _dashboard.ShowAsync();

        Assert.Equal(3, Figures.TotalTasks);
        Assert.Null(Figures.HealthScore);
        Assert.Equal("Details.History.Unavailable", _dashboard.Problem);
    }

    [Fact]
    public async Task WhenTheTasksCannotBeRead_TheRunsAreStillCounted_AndTheProblemIsShownUntilTheyCanBeReadAgain()
    {
        _store.ListingFails = new InvalidOperationException("The Task Scheduler service is not available.");

        await _dashboard.ShowAsync();

        Assert.Equal("The Task Scheduler service is not available.", _dashboard.Problem);
        Assert.Equal(7, Figures.SucceededRuns);

        _store.ListingFails = null;
        _stream.Change(new TaskChange(Fails, TaskChangeKind.Updated, Now));
        await AfterTheRefreshDelayAsync();

        Assert.False(_dashboard.HasProblem);
        Assert.Equal(3, Figures.TotalTasks);
    }

    [Fact]
    public void ALineOfAList_LeadsToItsTask()
    {
        _dashboard.ShowTask(Fails);

        Assert.Equal([$"ShowTask {Fails}"], _navigator.Calls);
    }

    [Fact]
    public async Task AClosedWindow_NoLongerListens_AndIgnoresWhatStillArrives()
    {
        var ui = new BusyUiThread();
        using DashboardViewModel closing = NewDashboard(ui);
        await closing.ShowAsync();
        _stream.Complete(FakeTaskEventStream.Finished(Succeeds, RunStatus.Succeeded, ended: Now));

        closing.Dispose();
        ui.RunPosted();
        _time.Advance(DashboardViewModel.RefreshDelay);
        _snooze.RecordSuppressedRun(Succeeds, RunOrigin.Manual);

        Assert.Equal(7, closing.Figures.SucceededRuns);
        Assert.Equal(0, ui.PostedCount);
        _dashboard.Dispose();
        Assert.Equal(0, _stream.Subscribers);
    }

    [Fact]
    public async Task AWindowClosedWhileTheHistoryIsRead_EndsTheLoadQuietly()
    {
        _history.Reading = cancellation => cancellation.WaitHandle.WaitOne();
        Task showing = _dashboard.ShowAsync();

        _dashboard.Dispose();
        await showing;

        Assert.False(_dashboard.IsLoading);
        Assert.False(_dashboard.HasFigures);
    }
}
