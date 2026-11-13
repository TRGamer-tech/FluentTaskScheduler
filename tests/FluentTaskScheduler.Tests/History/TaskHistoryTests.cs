using FluentTaskScheduler.History;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.History;

public sealed class TaskHistoryTests
{
    private const string Channel = "Microsoft-Windows-TaskScheduler/Operational";
    private static readonly string ExitThree = Evidence.Folder + @"\B_ExitThree";

    private readonly FakeEventLog _log = new();

    /// <summary>The records of the real log that the query for runs selects.</summary>
    private static IEnumerable<LogRecord> RunRecords => Evidence.Log.Where(record =>
        TaskSchedulerLog.KindOf(record.Id) is not (TaskEventKind.Other or TaskEventKind.Registered or TaskEventKind.Updated or TaskEventKind.Deleted));

    private TaskHistory NewHistory() => new(_log);

    [Fact]
    public void TheQueryForOneTask_NamesTheTaskAndNothingElse()
    {
        Assert.Equal(@"*[EventData[Data[@Name='TaskName']='\Backups\Nightly']]", TaskSchedulerLog.EventsOf(@"\Backups\Nightly", null, null));
    }

    [Fact]
    public void ATaskNameWithAnApostrophe_IsQuotedSoThatItStaysOneName()
    {
        Assert.Equal("""*[EventData[Data[@Name='TaskName']="\O'Brien's backup"]]""", TaskSchedulerLog.EventsOf(@"\O'Brien's backup", null, null));
    }

    [Fact]
    public void APeriod_IsAskedForInUtcToTheMillisecond()
    {
        var from = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(1));
        var to = new DateTimeOffset(2026, 11, 13, 16, 30, 0, 250, TimeSpan.FromHours(1));

        Assert.Equal(
            @"*[System[TimeCreated[@SystemTime>='2026-11-13T14:00:00.000Z' and @SystemTime<='2026-11-13T15:30:00.250Z']] and EventData[Data[@Name='TaskName']='\A']]",
            TaskSchedulerLog.EventsOf(@"\A", from, to));
        Assert.Equal(
            @"*[System[TimeCreated[@SystemTime>='2026-11-13T14:00:00.000Z']] and EventData[Data[@Name='TaskName']='\A']]",
            TaskSchedulerLog.EventsOf(@"\A", from, null));
        Assert.Equal(
            @"*[System[TimeCreated[@SystemTime<='2026-11-13T15:30:00.250Z']] and EventData[Data[@Name='TaskName']='\A']]",
            TaskSchedulerLog.EventsOf(@"\A", null, to));
    }

    [Fact]
    public void TheQueryForRuns_AsksForTheEventsRunsAreMadeOf_ButNotForTriggersOrRegistrations()
    {
        Assert.Equal(
            "*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103 or EventID=111 or EventID=200 or EventID=201 or EventID=202 or EventID=203"
            + " or EventID=323 or EventID=327 or EventID=328 or EventID=329 or EventID=330) and TimeCreated[@SystemTime>='2026-11-06T00:00:00.000Z']]]",
            TaskSchedulerLog.RunEventsSince(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheLiveQuery_AlsoAsksForTasksBeingRegisteredChangedAndDeleted()
    {
        Assert.Equal(
            "*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103 or EventID=106 or EventID=111 or EventID=140 or EventID=141 or EventID=142"
            + " or EventID=200 or EventID=201 or EventID=202 or EventID=203 or EventID=323 or EventID=327 or EventID=328 or EventID=329 or EventID=330)]]",
            TaskSchedulerLog.LiveEvents);
    }

    [Fact]
    public void GetEvents_ReturnsTheNewestEventsOfTheTask_NewestFirst_AndStopsReadingAtTheLimit()
    {
        _log.Add(Channel, [.. Evidence.Of("B_ExitThree")]);
        int inLog = Evidence.Of("B_ExitThree").Count;

        var events = NewHistory().GetEvents(ExitThree, maxCount: 3);

        Assert.Equal([141, 102, 201], events.Select(e => e.EventId));
        Assert.All(events, e => Assert.Equal(ExitThree, e.TaskPath));
        Assert.Equal((Channel, TaskSchedulerLog.EventsOf(ExitThree, null, null), true), Assert.Single(_log.Queries));
        Assert.True(inLog > 3);
        Assert.Equal(3, _log.RecordsRead);
    }

    [Fact]
    public void GetEvents_PassesThePeriodOnToTheLog()
    {
        var from = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        NewHistory().GetEvents(ExitThree, 100, from, to);

        Assert.Equal(TaskSchedulerLog.EventsOf(ExitThree, from, to), Assert.Single(_log.Queries).Query);
    }

    [Fact]
    public void TheEventsOfATask_FoldIntoItsRuns()
    {
        _log.Add(Channel, [.. Evidence.Of("B_ExitThree")]);

        var run = Assert.Single(TaskRun.FromEvents(NewHistory().GetEvents(ExitThree, 500)));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(3u, run.ResultCode);
    }

    [Fact]
    public void GetEvents_WithALimitOfZero_ReadsNothing()
    {
        _log.Add(Channel, [.. Evidence.Of("B_ExitThree")]);

        Assert.Empty(NewHistory().GetEvents(ExitThree, maxCount: 0));
        Assert.Empty(_log.Queries);
    }

    [Fact]
    public void GetRuns_ReturnsTheRunsOfAllTasks_NewestFirst()
    {
        _log.Add(Channel, [.. Evidence.Log]);
        var since = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

        var runs = NewHistory().GetRuns(since);

        Assert.Equal(21, runs.Count);
        Assert.Equal(runs.OrderByDescending(run => run.Started ?? run.Ended), runs);
        Assert.Equal((Channel, TaskSchedulerLog.RunEventsSince(since), true), Assert.Single(_log.Queries));
    }

    [Fact]
    public void GetRuns_ReadsNoMoreThanItsLimit_AndKeepsTheNewestRuns()
    {
        _log.Add(Channel, [.. RunRecords]);
        var history = new TaskHistory(_log, eventLimit: 4);

        var runs = history.GetRuns(DateTimeOffset.MinValue);

        // The newest four are the end of the timed-out run and the completion of one other.
        Assert.Equal(4, _log.RecordsRead);
        Assert.Equal(2, runs.Count);
        Assert.Contains(runs, run => run.TaskPath == Evidence.Folder + @"\F_TimeLimit" && run.Status == RunStatus.TimedOut && run.Started is null);
    }

    [Fact]
    public void GetSystemEvents_ReadsTheSystemLogAroundTheMoment_OldestFirst()
    {
        var moment = new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.Zero);
        _log.Add(SystemEvent.Log,
            SystemRecord("Microsoft-Windows-Winlogon", 7001, 2, moment.AddMinutes(4)),
            SystemRecord("EventLog", 6005, 1, moment.AddMinutes(-3)),
            SystemRecord("Service Control Manager", 7001, 3, moment.AddMinutes(5)));

        var events = NewHistory().GetSystemEvents(moment, TimeSpan.FromMinutes(10));

        Assert.Equal(
            [new SystemEvent(SystemEventKind.Startup, moment.AddMinutes(-3)), new SystemEvent(SystemEventKind.Logon, moment.AddMinutes(4))],
            events);
        Assert.Equal((SystemEvent.Log, SystemEvent.Query(moment.AddMinutes(-10), moment.AddMinutes(10)), false), Assert.Single(_log.Queries));
    }

    [Fact]
    public void GetSystemEvents_PutsThemInTheOrderTheyHappened_WhichIsNotTheOrderTheSystemLogKeepsThemIn()
    {
        // As a real System log has it: the shutdown is recorded before the request that led to it.
        var requested = new DateTimeOffset(2026, 11, 3, 17, 53, 14, 985, TimeSpan.Zero);
        var shutDown = new DateTimeOffset(2026, 11, 3, 17, 53, 15, 308, TimeSpan.Zero);
        var started = new DateTimeOffset(2026, 11, 3, 18, 24, 13, TimeSpan.Zero);
        _log.Add(SystemEvent.Log,
            SystemRecord("EventLog", 6006, 1, shutDown),
            SystemRecord("User32", 1074, 2, requested),
            SystemRecord("EventLog", 6005, 3, started));

        var events = NewHistory().GetSystemEvents(shutDown, TimeSpan.FromHours(1));

        Assert.Equal(
            [new SystemEvent(SystemEventKind.ShutdownRequested, requested), new SystemEvent(SystemEventKind.Shutdown, shutDown), new SystemEvent(SystemEventKind.Startup, started)],
            events);
    }

    [Fact]
    public void AQueryThatIsCancelledBeforeItStarts_ReadsNothing()
    {
        _log.Add(Channel, [.. Evidence.Log]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => NewHistory().GetRuns(DateTimeOffset.MinValue, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => NewHistory().GetEvents(ExitThree, 10, cancellation: cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => NewHistory().GetSystemEvents(DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(1), cancelled.Token));
        Assert.Equal(0, _log.RecordsRead);
    }

    [Fact]
    public void AQueryThatIsCancelledWhileReading_StopsThere()
    {
        _log.Add(Channel, [.. Evidence.Log]);
        using var cancelling = new CancellationTokenSource();
        _log.Reading = _ =>
        {
            if (_log.RecordsRead == 5) cancelling.Cancel();
        };

        Assert.Throws<OperationCanceledException>(() => NewHistory().GetRuns(DateTimeOffset.MinValue, cancelling.Token));
        Assert.Equal(5, _log.RecordsRead);
    }

    [Fact]
    public void ALogThatCannotBeRead_IsReportedAsSuch_NotAsAnEmptyHistory()
    {
        _log.Unreadable = true;

        Assert.Throws<HistoryUnavailableException>(() => NewHistory().GetEvents(ExitThree, 10));
        Assert.Throws<HistoryUnavailableException>(() => NewHistory().GetRuns(DateTimeOffset.MinValue));
        Assert.Throws<HistoryUnavailableException>(() => NewHistory().GetSystemEvents(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(LogState.Enabled)]
    [InlineData(LogState.Disabled)]
    [InlineData(LogState.Unavailable)]
    public void GetLogState_TellsWhetherHistoryIsBeingRecorded(LogState state)
    {
        _log.State = state;

        Assert.Equal(state, NewHistory().GetLogState());
    }

    [Fact]
    public void EnableLog_SwitchesRecordingOn()
    {
        _log.State = LogState.Disabled;

        Assert.Equal(EnableLogResult.Enabled, NewHistory().EnableLog());
        Assert.Equal(LogState.Enabled, NewHistory().GetLogState());
    }

    [Fact]
    public void EnableLog_WithoutTheRights_SaysSo_AndChangesNothing()
    {
        _log.State = LogState.Disabled;
        _log.EnableResult = EnableLogResult.AccessDenied;

        Assert.Equal(EnableLogResult.AccessDenied, NewHistory().EnableLog());
        Assert.Equal(LogState.Disabled, NewHistory().GetLogState());
    }

    private static LogRecord SystemRecord(string provider, int id, long recordId, DateTimeOffset time) =>
        new(provider, id, time, new Dictionary<string, string>()) { RecordId = recordId };
}
