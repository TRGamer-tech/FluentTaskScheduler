using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

public sealed class HistoryCsvTests
{
    private const string Header = "Time,EventId,Event,Level,Action,ResultCode,User,InstanceId";

    private static readonly TimeZoneInfo OneHourAhead = TimeZoneInfo.CreateCustomTimeZone("Test +1", TimeSpan.FromHours(1), "Test +1", "Test +1");

    private static TaskEvent Event(int id = 201, string action = "", string user = "", uint? code = null) => new()
    {
        Kind = TaskEventKind.Other,
        EventId = id,
        Time = new DateTimeOffset(2026, 3, 1, 23, 30, 0, 123, TimeSpan.Zero),
        TaskPath = @"\Any",
        Action = action,
        User = user,
        ResultCode = code
    };

    /// <summary>The rows of the file, without the byte order mark.</summary>
    private static string[] Rows(IEnumerable<TaskEvent> events, TimeZoneInfo? zone = null)
    {
        byte[] file = HistoryCsv.Write(events, zone ?? TimeZoneInfo.Utc);
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(file, 3, file.Length - 3);
        Assert.EndsWith("\r\n", text);
        return text[..^2].Split("\r\n");
    }

    [Fact]
    public void TheFile_BeginsWithAByteOrderMark_AndTheHeaderRow()
    {
        byte[] file = HistoryCsv.Write([], TimeZoneInfo.Utc);

        Assert.Equal([0xEF, 0xBB, 0xBF], file[..3]);
        Assert.Equal(Header + "\r\n", Encoding.UTF8.GetString(file, 3, file.Length - 3));
    }

    [Fact]
    public void EveryEvent_IsOneRow_InTheOrderGiven()
    {
        // The real events of the task that exits with 3, newest first as the history returns them.
        var events = Evidence.Of("B_ExitThree").Select(TaskEvent.From).OfType<TaskEvent>().Reverse().ToList();

        string[] rows = Rows(events);

        Assert.Equal(Header, rows[0]);
        Assert.Equal(events.Count + 1, rows.Length);
        Assert.Equal(@"2026-11-13T14:26:46.090+00:00,141,EventResult.141,EventLevel.Information,,,runnervmm7ud1\runneradmin,", rows[1]);
        Assert.Equal(@"2026-11-13T14:25:38.951+00:00,102,EventResult.102,EventLevel.Information,,,runnervmm7ud1\runneradmin,362395ca-4710-453e-aea6-4d0dd8bcc7b3", rows[2]);
        Assert.Equal("2026-11-13T14:25:38.951+00:00,201,EventResult.201,EventLevel.Information,cmd.exe,3,,362395ca-4710-453e-aea6-4d0dd8bcc7b3", rows[3]);
    }

    [Fact]
    public void Times_AreWrittenAsIso8601_WithTheOffsetOfTheZoneAskedFor()
    {
        Assert.StartsWith("2026-03-01T23:30:00.123+00:00,", Rows([Event()])[1]);
        Assert.StartsWith("2026-03-02T00:30:00.123+01:00,", Rows([Event()], OneHourAhead)[1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")] // another calendar and other digits
    [InlineData("ja-JP")]
    public void TheFile_IsTheSame_WhateverTheRegionalSettingsAre(string culture)
    {
        var events = new[] { Event(code: 0x80070002), Event(id: 100, code: 70000) };
        byte[] expected = HistoryCsv.Write(events, OneHourAhead);

        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, HistoryCsv.Write(events, OneHourAhead));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(0u, "0")]
    [InlineData(3u, "3")]
    [InlineData(0x80070002u, "0x80070002")]
    public void AResultCode_IsWrittenTheWayItIsShown(uint code, string expected)
    {
        Assert.Equal(expected, Rows([Event(code: code)])[1].Split(',')[5]);
    }

    [Fact]
    public void AnEventWithoutAResultCode_LeavesTheColumnEmpty()
    {
        Assert.Equal("", Rows([Event(code: null)])[1].Split(',')[5]);
    }

    [Fact]
    public void AFieldWithACommaAQuoteOrALineBreak_IsQuoted_AndItsQuotesAreDoubled()
    {
        string row = string.Join("\r\n", Rows([Event(action: "C:\\Tools\\a, b\\\"my tool\".exe", user: "line one\r\nline two")])[1..]);

        Assert.Equal("2026-03-01T23:30:00.123+00:00,201,EventResult.201,EventLevel.Information,\"C:\\Tools\\a, b\\\"\"my tool\"\".exe\",,\"line one\r\nline two\",", row);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("C:\\Windows\\cmd.exe", "C:\\Windows\\cmd.exe")]
    public void AnActionNameThatASpreadsheetWouldRunAsAFormula_IsWrittenAsPlainText(string action, string written)
    {
        Assert.Equal(written, Rows([Event(action: action)])[1].Split(',')[4]);
    }

    [Fact]
    public void NamesOutsideAscii_AreWrittenAsUtf8()
    {
        var e = Event(action: "C:\\Übung\\日本語.exe", user: "PC\\Zoë");

        string row = Rows([e])[1];

        Assert.Contains("C:\\Übung\\日本語.exe", row);
        Assert.Contains("PC\\Zoë", row);
    }

    [Fact]
    public void TheLevel_IsNamedAfterWhatWindowsLogged()
    {
        Assert.Equal("EventLevel.Error", Rows([Event() with { Level = EventLevel.Error }])[1].Split(',')[3]);
    }
}
