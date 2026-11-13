using System.Diagnostics.Tracing;
using System.Xml;
using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

public sealed class LogRecordTests
{
    // Two events exactly as Windows rendered them on the test runner.
    private const string ActionCouldNotBeLaunched =
        """<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-TaskScheduler' Guid='{de7b24ea-73c8-4a09-985d-5bdadcfa9017}'/><EventID>203</EventID><Version>0</Version><Level>2</Level><Task>203</Task><Opcode>101</Opcode><Keywords>0x8000000000000000</Keywords><TimeCreated SystemTime='2026-11-13T14:25:46.1627145Z'/><EventRecordID>49</EventRecordID><Correlation ActivityID='{1685ddf0-e636-49d4-9dc4-74892d9dc1ab}'/><Execution ProcessID='2624' ThreadID='3116'/><Channel>Microsoft-Windows-TaskScheduler/Operational</Channel><Computer>runnervmm7ud1</Computer><Security UserID='S-1-5-18'/></System><EventData Name='ActionLaunchFailure'><Data Name='TaskName'>\FtsTests_a070dc65\C_MissingExe</Data><Data Name='TaskInstanceId'>{1685ddf0-e636-49d4-9dc4-74892d9dc1ab}</Data><Data Name='ActionName'>C:\FtsTests\does-not-exist.exe</Data><Data Name='ResultCode'>2147942402</Data></EventData></Event>""";

    private const string TaskCouldNotBeStarted =
        """<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-TaskScheduler' Guid='{de7b24ea-73c8-4a09-985d-5bdadcfa9017}'/><EventID>101</EventID><Version>0</Version><Level>2</Level><Task>101</Task><Opcode>101</Opcode><Keywords>0x8000000000000001</Keywords><TimeCreated SystemTime='2026-11-13T14:25:46.1628788Z'/><EventRecordID>50</EventRecordID><Correlation/><Execution ProcessID='2624' ThreadID='3116'/><Channel>Microsoft-Windows-TaskScheduler/Operational</Channel><Computer>runnervmm7ud1</Computer><Security UserID='S-1-5-18'/></System><EventData Name='TaskStartFailedEvent'><Data Name='TaskName'>\FtsTests_a070dc65\C_MissingExe</Data><Data Name='UserContext'>runnervmm7ud1\runneradmin</Data><Data Name='ResultCode'>2147942402</Data></EventData></Event>""";

    [Fact]
    public void AnEventAsWindowsRendersIt_IsReadWithItsSourceIdLevelTimeAndFields()
    {
        var record = LogRecord.FromXml(ActionCouldNotBeLaunched);

        Assert.Equal("Microsoft-Windows-TaskScheduler", record.Provider);
        Assert.Equal(203, record.Id);
        Assert.Equal(EventLevel.Error, record.Level);
        Assert.Equal(49, record.RecordId);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 14, 25, 46, TimeSpan.Zero).AddTicks(1627145), record.Time);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["TaskName"] = @"\FtsTests_a070dc65\C_MissingExe",
                ["TaskInstanceId"] = "{1685ddf0-e636-49d4-9dc4-74892d9dc1ab}",
                ["ActionName"] = @"C:\FtsTests\does-not-exist.exe",
                ["ResultCode"] = "2147942402"
            },
            record.Data);
    }

    [Fact]
    public void AnEventWithoutACorrelation_IsReadLikeAnyOther()
    {
        var record = LogRecord.FromXml(TaskCouldNotBeStarted);

        Assert.Equal(101, record.Id);
        Assert.Equal(@"runnervmm7ud1\runneradmin", record.Data["UserContext"]);
        Assert.Equal(3, record.Data.Count);
    }

    [Theory]
    [InlineData("2026-11-13T14:25:46.1627145Z", 1627145)]
    [InlineData("2011-05-05T15:52:39.593750000Z", 5937500)] // nine digits, as Windows 7 wrote them
    [InlineData("2026-11-13T14:25:46.162714599Z", 1627145)]
    [InlineData("2026-11-13T14:25:46Z", 0)]
    public void TheTime_IsReadInUtc_HoweverManyFractionalDigitsWindowsWrote(string systemTime, int ticksIntoTheSecond)
    {
        var record = LogRecord.FromXml($"<Event><System><EventID>1</EventID><TimeCreated SystemTime='{systemTime}'/></System></Event>");

        Assert.Equal(TimeSpan.Zero, record.Time.Offset);
        Assert.Equal(ticksIntoTheSecond, record.Time.Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void MarkupCharactersInAField_AreDecoded()
    {
        var record = LogRecord.FromXml(
            """<Event><System><EventID>100</EventID><TimeCreated SystemTime='2026-01-01T00:00:00.0000000Z'/></System><EventData><Data Name='TaskName'>\R&amp;D\&lt;nightly&gt; "x"</Data></EventData></Event>""");

        Assert.Equal(@"\R&D\<nightly> ""x""", record.Data["TaskName"]);
    }

    [Fact]
    public void AnEventOfAClassicSource_WithQualifiersAndUnnamedData_IsRead()
    {
        // The System log's "event log service started", the way sources from before Windows Vista are rendered.
        var record = LogRecord.FromXml(
            """<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='EventLog'/><EventID Qualifiers='32768'>6005</EventID><Level>4</Level><Task>0</Task><Keywords>0x80000000000000</Keywords><TimeCreated SystemTime='2026-11-13T08:00:01.0000000Z'/><EventRecordID>1234</EventRecordID><Channel>System</Channel><Computer>PC</Computer><Security/></System><EventData><Data>ignored</Data><Binary>EA070B00</Binary></EventData></Event>""");

        Assert.Equal("EventLog", record.Provider);
        Assert.Equal(6005, record.Id);
        Assert.Equal(1234, record.RecordId);
        Assert.Empty(record.Data);
    }

    [Theory]
    [InlineData("<Event><System><TimeCreated SystemTime='2026-01-01T00:00:00Z'/></System></Event>")] // no ID
    [InlineData("<Event><System><EventID>1</EventID></System></Event>")] // no time
    [InlineData("<Event><System><EventID>one</EventID><TimeCreated SystemTime='2026-01-01T00:00:00Z'/></System></Event>")]
    [InlineData("<Event><System><EventID>1</EventID><TimeCreated SystemTime='yesterday'/></System></Event>")]
    [InlineData("<SomethingElse/>")]
    public void XmlThatIsNotAnEvent_IsRefused(string xml)
    {
        Assert.Throws<FormatException>(() => LogRecord.FromXml(xml));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<Event><System>")]
    public void TextThatIsNotXml_IsRefused(string text)
    {
        Assert.ThrowsAny<XmlException>(() => LogRecord.FromXml(text));
    }
}
