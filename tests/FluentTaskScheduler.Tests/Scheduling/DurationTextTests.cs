using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

public class DurationTextTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("10m", 600)]
    [InlineData("10 min", 600)]
    [InlineData("2 minutes", 120)]
    [InlineData("1h", 3600)]
    [InlineData("1.5h", 5400)]
    [InlineData("1,5h", 5400)]
    [InlineData("2 Hours", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("1h 30m", 5400)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d 4h 5m 6s", 2 * 86400 + 4 * 3600 + 5 * 60 + 6)]
    [InlineData("  45 secs ", 45)]
    [InlineData("PT10M", 600)]
    [InlineData("pt1h", 3600)]
    [InlineData("P1DT12H", 36 * 3600)]
    public void TryParse_AcceptsShorthandAndIso(string text, int expectedSeconds)
    {
        Assert.True(DurationText.TryParse(text, out var value));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("10")]
    [InlineData("m")]
    [InlineData("ten minutes")]
    [InlineData("10x")]
    [InlineData("1h and 30m")]
    [InlineData("-5m")]
    [InlineData("PT")]
    [InlineData("P1X")]
    [InlineData("１５m")]
    [InlineData("1٣m")]
    [InlineData("P99999Y")]
    [InlineData("P10675200D")]
    [InlineData("P99999999D")]
    [InlineData("P30000Y")]
    public void TryParse_RejectsEverythingElse(string? text)
    {
        Assert.False(DurationText.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(30, "30s")]
    [InlineData(600, "10m")]
    [InlineData(3600, "1h")]
    [InlineData(5400, "1h 30m")]
    [InlineData(86400, "1d")]
    [InlineData(259200, "3d")]
    [InlineData(90061, "1d 1h 1m 1s")]
    public void Format_UsesTheLargestUnits(int seconds, string expected)
    {
        Assert.Equal(expected, DurationText.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(3599)]
    [InlineData(86399)]
    [InlineData(86401)]
    [InlineData(31 * 86400 + 7)]
    public void Format_ThenParse_GivesTheSameDuration(int seconds)
    {
        var original = TimeSpan.FromSeconds(seconds);
        Assert.True(DurationText.TryParse(DurationText.Format(original), out var parsed));
        Assert.Equal(original, parsed);
    }
}
