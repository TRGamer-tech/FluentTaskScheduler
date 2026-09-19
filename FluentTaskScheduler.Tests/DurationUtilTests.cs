using System;
using System.Globalization;
using System.Threading;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests
{
    // Covers items 1.3, 1.4, 1.15: ISO-8601 duration validation and culture-invariant schedule
    // round-tripping.
    public class DurationUtilTests
    {
        [Theory]
        [InlineData("PT15M")]
        [InlineData("PT90M")]
        [InlineData("P1D")]
        [InlineData("PT1H")]
        public void TryParseIsoDuration_AcceptsValidIsoDurations(string input)
        {
            Assert.True(DurationUtil.TryParseIsoDuration(input, out var result));
            Assert.True(result > TimeSpan.Zero);
        }

        [Theory]
        [InlineData("1 hour")]
        [InlineData("90 minutes")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not a duration")]
        public void TryParseIsoDuration_RejectsNonIsoInput(string? input)
        {
            Assert.False(DurationUtil.TryParseIsoDuration(input, out _));
        }

        [Fact]
        public void FormatScheduleInfo_RoundTripsThroughTryParseScheduleInfo()
        {
            var original = new DateTime(2026, 3, 5, 14, 30, 0);
            string formatted = DurationUtil.FormatScheduleInfo(original);
            var parsed = DurationUtil.TryParseScheduleInfo(formatted);

            Assert.Equal(original, parsed);
        }

        [Fact]
        public void FormatScheduleInfo_IsInvariantAcrossCultures()
        {
            var original = new DateTime(2026, 3, 5, 14, 30, 0);
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string germanFormatted = DurationUtil.FormatScheduleInfo(original);

                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                string englishFormatted = DurationUtil.FormatScheduleInfo(original);

                Assert.Equal(germanFormatted, englishFormatted);
                Assert.Equal(original, DurationUtil.TryParseScheduleInfo(germanFormatted));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }
        }

        [Fact]
        public void TryParseScheduleInfo_ReturnsNullInsteadOfSilentlyDefaultingOnGarbageInput()
        {
            // The old behavior (plain DateTime.TryParse swallowing failures) silently produced
            // "today at 9 AM" on bad input; the fixed helper must surface the failure as null so
            // the caller can show a validation error instead (see item 1.15).
            Assert.Null(DurationUtil.TryParseScheduleInfo("not a date"));
            Assert.Null(DurationUtil.TryParseScheduleInfo(""));
            Assert.Null(DurationUtil.TryParseScheduleInfo(null));
        }

        // Covers item 2.3: IdleDuration/RestartInterval placeholders advertise shorthand like "10m",
        // but were previously validated with the strict ISO-8601-only parser.
        [Theory]
        [InlineData("30s", 30)]
        [InlineData("10m", 10 * 60)]
        [InlineData("2h", 2 * 60 * 60)]
        [InlineData("1d", 24 * 60 * 60)]
        [InlineData("1.5h", 90 * 60)]
        [InlineData("90 sec", 90)]
        [InlineData("2 hours", 2 * 60 * 60)]
        public void TryParseFlexibleDuration_AcceptsShorthand(string input, double expectedSeconds)
        {
            Assert.True(DurationUtil.TryParseFlexibleDuration(input, out var result));
            Assert.Equal(expectedSeconds, result.TotalSeconds, precision: 3);
        }

        [Theory]
        [InlineData("PT10M")]
        [InlineData("P1D")]
        public void TryParseFlexibleDuration_StillAcceptsIso(string input)
        {
            Assert.True(DurationUtil.TryParseFlexibleDuration(input, out var result));
            Assert.True(result > TimeSpan.Zero);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("garbage")]
        [InlineData("10 bananas")]
        public void TryParseFlexibleDuration_RejectsInvalidInput(string? input)
        {
            Assert.False(DurationUtil.TryParseFlexibleDuration(input, out _));
        }
    }
}
