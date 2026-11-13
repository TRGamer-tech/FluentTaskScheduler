using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

public class EventQueryTests
{
    [Theory]
    [InlineData("Application", "", null)]
    [InlineData("System", "disk", null)]
    [InlineData("System", "", 7)]
    [InlineData("Microsoft-Windows-TaskScheduler/Operational", "Microsoft-Windows-TaskScheduler", 102)]
    [InlineData("Application", "O'Brien & Sons <test>", 1000)]
    public void Build_ThenTryParse_RoundTrips(string log, string source, int? eventId)
    {
        string xml = EventQuery.Build(log, source, eventId);

        Assert.True(EventQuery.TryParse(xml, out string parsedLog, out string parsedSource, out int? parsedId));
        Assert.Equal(log, parsedLog);
        Assert.Equal(source, parsedSource);
        Assert.Equal(eventId, parsedId);
    }

    [Fact]
    public void Build_DefaultsToTheApplicationLog()
    {
        Assert.True(EventQuery.TryParse(EventQuery.Build(" ", "", null), out string log, out _, out _));
        Assert.Equal("Application", log);
    }

    [Fact]
    public void Build_EscapesMarkupInTheLogName()
    {
        string xml = EventQuery.Build("A\"><x/>", "", null);
        Assert.DoesNotContain("<x/>", xml);
        Assert.True(EventQuery.TryParse(xml, out string log, out _, out _));
        Assert.Equal("A\"><x/>", log);
    }

    [Theory]
    [InlineData("a\nb", "")]
    [InlineData("Application", "x\ty")]
    public void Build_RejectsControlCharacters(string log, string source)
    {
        Assert.Throws<System.ArgumentException>(() => EventQuery.Build(log, source, null));
    }

    [Fact]
    public void TryParse_ReadsTheFormWindowsWritesForABasicTrigger()
    {
        const string xml = "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='disk'] and EventID=7]]</Select></Query></QueryList>";

        Assert.True(EventQuery.TryParse(xml, out string log, out string source, out int? id));
        Assert.Equal("System", log);
        Assert.Equal("disk", source);
        Assert.Equal(7, id);
    }

    [Theory]
    // Richer than the editor's three fields: must stay raw XML.
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='disk'] and (Level=1 or Level=2) and (EventID=7 or EventID=11)]]</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[(EventID=7 or EventID=11)]]</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*</Select><Suppress Path=\"System\">*[System[(EventID=7)]]</Suppress></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"A\"><Select Path=\"A\">*</Select></Query><Query Id=\"1\" Path=\"B\"><Select Path=\"B\">*</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[]]</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[ and EventID=7]]</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[Provider[@Name='disk']EventID=7]]</Select></Query></QueryList>")]
    [InlineData("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[EventData[Data='x']]</Select></Query></QueryList>")]
    [InlineData("not xml")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RefusesWhatTheSimpleFormCannotExpress(string? xml)
    {
        Assert.False(EventQuery.TryParse(xml, out _, out _, out _));
    }

    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("it's \"both\"", "concat('it', \"'\", 's \"both\"')")]
    [InlineData("", "''")]
    [InlineData(null, "''")]
    public void ToXPathLiteral_QuotesSafely(string? value, string expected)
    {
        Assert.Equal(expected, EventQuery.ToXPathLiteral(value));
    }
}
