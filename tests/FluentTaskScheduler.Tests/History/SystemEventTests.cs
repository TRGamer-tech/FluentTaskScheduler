using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

public sealed class SystemEventTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    private static LogRecord Record(string provider, int id) => new(provider, id, Time, new Dictionary<string, string>());

    [Theory]
    [InlineData("EventLog", 6005, SystemEventKind.Startup)]
    [InlineData("EventLog", 6006, SystemEventKind.Shutdown)]
    [InlineData("EventLog", 6008, SystemEventKind.UnexpectedShutdown)]
    [InlineData("User32", 1074, SystemEventKind.ShutdownRequested)]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, SystemEventKind.UnexpectedShutdown)]
    [InlineData("Microsoft-Windows-Kernel-Power", 42, SystemEventKind.Sleep)]
    [InlineData("Microsoft-Windows-Kernel-Power", 107, SystemEventKind.Resume)]
    [InlineData("Microsoft-Windows-Kernel-Power", 506, SystemEventKind.Sleep)]
    [InlineData("Microsoft-Windows-Kernel-Power", 507, SystemEventKind.Resume)]
    [InlineData("Microsoft-Windows-Winlogon", 7001, SystemEventKind.Logon)]
    [InlineData("Microsoft-Windows-Winlogon", 7002, SystemEventKind.Logoff)]
    public void AnEventOfTheSystemLog_IsRecognisedByItsSourceAndId(string provider, int id, SystemEventKind expected)
    {
        Assert.Equal(new SystemEvent(expected, Time), SystemEvent.From(Record(provider, id)));
    }

    [Theory]
    [InlineData("Service Control Manager", 7001)] // a service that failed to start, not a logon
    [InlineData("Microsoft-Windows-Kernel-General", 41)]
    [InlineData("EventLog", 1074)]
    [InlineData("Microsoft-Windows-Winlogon", 6005)]
    [InlineData("Microsoft-Windows-TaskScheduler", 107)] // a time trigger, not a wake-up
    public void TheSameIdFromAnotherSource_MeansSomethingElse_AndIsLeftOut(string provider, int id)
    {
        Assert.Null(SystemEvent.From(Record(provider, id)));
    }

    [Fact]
    public void TheQuery_NamesTheSourcesTheIdsAndThePeriod()
    {
        string query = SystemEvent.Query(Time.AddMinutes(-10), Time.AddMinutes(10));

        Assert.Equal(
            "*[System[Provider[@Name='EventLog' or @Name='User32' or @Name='Microsoft-Windows-Kernel-Power' or @Name='Microsoft-Windows-Winlogon']"
            + " and (EventID=41 or EventID=42 or EventID=107 or EventID=506 or EventID=507 or EventID=1074 or EventID=6005 or EventID=6006 or EventID=6008 or EventID=7001 or EventID=7002)"
            + " and TimeCreated[@SystemTime>='2026-11-13T07:50:00.000Z' and @SystemTime<='2026-11-13T08:10:00.000Z']]]",
            query);
    }
}
