using System.Diagnostics.Tracing;
using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

public sealed class TaskEventTests
{
    private const string Provider = "Microsoft-Windows-TaskScheduler";

    /// <summary>The record of the real log with this number.</summary>
    private static LogRecord Logged(long recordId) => Evidence.Log.Single(record => record.RecordId == recordId);

    private static LogRecord Record(int id, params (string Name, string Value)[] data) =>
        new(Provider, id, DateTimeOffset.UnixEpoch, data.ToDictionary(field => field.Name, field => field.Value));

    [Theory]
    [InlineData(100, TaskEventKind.RunStarted)]
    [InlineData(101, TaskEventKind.StartFailed)]
    [InlineData(102, TaskEventKind.RunCompleted)]
    [InlineData(103, TaskEventKind.StartFailed)]
    [InlineData(106, TaskEventKind.Registered)]
    [InlineData(111, TaskEventKind.Terminated)]
    [InlineData(140, TaskEventKind.Updated)]
    [InlineData(141, TaskEventKind.Deleted)]
    [InlineData(142, TaskEventKind.Updated)]
    [InlineData(200, TaskEventKind.ActionStarted)]
    [InlineData(201, TaskEventKind.ActionCompleted)]
    [InlineData(202, TaskEventKind.ActionFailed)]
    [InlineData(203, TaskEventKind.ActionFailed)]
    [InlineData(323, TaskEventKind.Stopping)]
    [InlineData(327, TaskEventKind.Stopping)]
    [InlineData(328, TaskEventKind.Stopping)]
    [InlineData(329, TaskEventKind.TimedOut)]
    [InlineData(330, TaskEventKind.Stopping)]
    [InlineData(107, TaskEventKind.Other)] // triggered by its schedule
    [InlineData(110, TaskEventKind.Other)] // triggered by a user
    [InlineData(129, TaskEventKind.Other)] // process created
    [InlineData(322, TaskEventKind.Other)] // request ignored: already running
    [InlineData(325, TaskEventKind.Other)] // request queued
    [InlineData(331, TaskEventKind.Other)] // the time limit could not be enforced
    [InlineData(999, TaskEventKind.Other)]
    public void TheKindOfAnEvent_FollowsFromItsIdAlone(int eventId, TaskEventKind expected)
    {
        var e = TaskEvent.From(Record(eventId, ("TaskName", @"\Any")));

        Assert.Equal(expected, e!.Kind);
        Assert.Equal(eventId, e.EventId);
    }

    [Fact]
    public void AStart_CarriesItsTaskRunAccountTimeAndPlaceInTheLog()
    {
        var e = TaskEvent.From(Logged(14))!;

        Assert.Equal(TaskEventKind.RunStarted, e.Kind);
        Assert.Equal(Evidence.Folder + @"\A_ExitZero", e.TaskPath);
        Assert.Equal(Guid.Parse("4483e347-1005-4795-8711-6a94f872bcad"), e.InstanceId);
        Assert.Equal(@"runnervmm7ud1\runneradmin", e.User);
        Assert.Equal(Logged(14).Time, e.Time);
        Assert.Equal(14, e.RecordId);
        Assert.Equal(EventLevel.Informational, e.Level);
        Assert.Null(e.ResultCode);
        Assert.Equal("", e.Action);
    }

    [Theory]
    [InlineData(14, "4483e347-1005-4795-8711-6a94f872bcad")] // 100 names it InstanceId
    [InlineData(15, "4483e347-1005-4795-8711-6a94f872bcad")] // 200 names it TaskInstanceId
    [InlineData(62, "b65e4485-fda7-48b1-b0e2-6986b74c7c29")] // 330, stopping on request
    [InlineData(83, "62ab9657-17da-42fb-9746-53a5757dd1dc")] // 325 names the queued instance
    public void TheRunAnEventBelongsTo_IsReadFromWhicheverFieldCarriesIt(long recordId, string instanceId)
    {
        Assert.Equal(Guid.Parse(instanceId), TaskEvent.From(Logged(recordId))!.InstanceId);
    }

    [Fact]
    public void AnIgnoredStartRequest_DoesNotBelongToTheRunItNames()
    {
        // 322 names the instance that keeps running, which is not the one that was asked for.
        var ignored = TaskEvent.From(Logged(85))!;

        Assert.Equal(322, ignored.EventId);
        Assert.Null(ignored.InstanceId);
    }

    [Fact]
    public void AnInstanceStoppedForANewOne_IsTheOneTheEventBelongsTo()
    {
        var e = TaskEvent.From(Record(323,
            ("TaskName", @"\Any"),
            ("StoppedTaskInstanceId", "{11111111-1111-1111-1111-111111111111}"),
            ("NewTaskInstanceId", "{22222222-2222-2222-2222-222222222222}")))!;

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), e.InstanceId);
    }

    [Fact]
    public void AFailedStart_NamesNoRun()
    {
        var failed = TaskEvent.From(Logged(50))!;

        Assert.Equal(TaskEventKind.StartFailed, failed.Kind);
        Assert.Null(failed.InstanceId);
        Assert.Equal(EventLevel.Error, failed.Level);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(2147942403u, 3u)]            // "exit 3" as it was logged
    [InlineData(2147942402u, 2u)]
    [InlineData(2147946864u, 4464u)]         // "exit 70000": only the lower 16 bits reach the log
    [InlineData(4294967295u, 4294967295u)]   // "exit -1" is logged as it is
    [InlineData(3221225786u, 3221225786u)]   // 0xC000013A, ended by Ctrl+C
    [InlineData(5u, 5u)]
    public void AnExitCode_IsUnwrappedFromTheWindowsErrorItIsLoggedAs(uint logged, uint exitCode)
    {
        var completed = TaskEvent.From(Record(201, ("TaskName", @"\Any"), ("ResultCode", logged.ToString())))!;

        Assert.Equal(exitCode, completed.ResultCode);
    }

    [Fact]
    public void TheExitCodesOfRealRuns_AreWhatTheProgramsReturned()
    {
        Assert.Equal(0u, TaskEvent.From(Logged(17))!.ResultCode);            // exit 0
        Assert.Equal(3u, TaskEvent.From(Logged(25))!.ResultCode);            // exit 3
        Assert.Equal(70000u & 0xFFFF, TaskEvent.From(Logged(33))!.ResultCode); // exit 70000
        Assert.Equal(uint.MaxValue, TaskEvent.From(Logged(45))!.ResultCode); // exit -1
    }

    [Fact]
    public void TheErrorOfAFailure_IsKeptAsTheWindowsErrorItIs()
    {
        Assert.Equal(0x80070002, TaskEvent.From(Logged(49))!.ResultCode);  // 203: file not found
        Assert.Equal(0x80070002, TaskEvent.From(Logged(50))!.ResultCode);  // 101 that followed it
        Assert.Equal(0x8007052E, TaskEvent.From(Logged(121))!.ResultCode); // 101: logon failure
    }

    [Fact]
    public void AnActionCompletedEventOfAnOldWindows_HasNoResultCode()
    {
        // Version 0 of event 201 has no such field.
        var completed = TaskEvent.From(Record(201, ("TaskName", @"\Any"), ("ActionName", "x.exe"), ("TaskInstanceId", "{11111111-1111-1111-1111-111111111111}")))!;

        Assert.Null(completed.ResultCode);
    }

    [Fact]
    public void TheAction_IsTheProgramTheEventNames()
    {
        Assert.Equal("cmd.exe", TaskEvent.From(Logged(15))!.Action);                          // 200: ActionName
        Assert.Equal(@"C:\FtsTests\does-not-exist.exe", TaskEvent.From(Logged(49))!.Action);  // 203: ActionName
        Assert.Equal("cmd.exe", TaskEvent.From(Logged(13))!.Action);                          // 129: Path
    }

    [Fact]
    public void TheUser_IsTakenFromEitherOfTheFieldsTaskSchedulerUsesForIt()
    {
        Assert.Equal(@"runnervmm7ud1\runneradmin", TaskEvent.From(Logged(11))!.User); // 106: UserContext
        Assert.Equal(@"runnervmm7ud1\runneradmin", TaskEvent.From(Logged(12))!.User); // 140: UserName
    }

    [Fact]
    public void ARecordThatNamesNoTask_IsNotATaskEvent()
    {
        // 104, the logon failure, has no TaskName: the task's path sits in its UserName field.
        Assert.Equal(104, Logged(120).Id);
        Assert.Null(TaskEvent.From(Logged(120)));
        Assert.Null(TaskEvent.From(Record(100, ("TaskName", ""))));
    }

    [Fact]
    public void ARecordOfAnotherSource_IsNotATaskEvent()
    {
        var foreign = new LogRecord("Service Control Manager", 100, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { ["TaskName"] = @"\Any" });

        Assert.Null(TaskEvent.From(foreign));
    }

    [Fact]
    public void AnEventReadFromItsXml_IsTheSameAsOneReadFromItsFields()
    {
        const string xml =
            """<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-TaskScheduler' Guid='{de7b24ea-73c8-4a09-985d-5bdadcfa9017}'/><EventID>201</EventID><Version>2</Version><Level>4</Level><Task>201</Task><Opcode>2</Opcode><Keywords>0x8000000000000000</Keywords><TimeCreated SystemTime='2026-11-13T14:25:33.5417884Z'/><EventRecordID>9</EventRecordID><Correlation ActivityID='{741a009b-e4f3-4372-995d-6814dad817f5}'/><Execution ProcessID='2624' ThreadID='3116'/><Channel>Microsoft-Windows-TaskScheduler/Operational</Channel><Computer>runnervmm7ud1</Computer><Security UserID='S-1-5-18'/></System><EventData Name='ActionSuccess'><Data Name='TaskName'>\FtsTests_a070dc65\W_WarmUp</Data><Data Name='TaskInstanceId'>{741a009b-e4f3-4372-995d-6814dad817f5}</Data><Data Name='ActionName'>cmd.exe</Data><Data Name='ResultCode'>0</Data><Data Name='EnginePID'>4564</Data></EventData></Event>""";

        Assert.Equal(TaskEvent.From(Logged(9)), TaskEvent.From(LogRecord.FromXml(xml)));
    }
}
