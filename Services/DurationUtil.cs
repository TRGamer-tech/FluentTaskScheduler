using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace FluentTaskScheduler.Services
{
    /// <summary>Small helpers around the ISO-8601 durations Task Scheduler settings are stored as
    /// (e.g. "PT90M", "P1D"), shared by the edit-dialog validation and <see cref="TaskServiceWrapper"/>.</summary>
    public static class DurationUtil
    {
        /// <summary>Validates an ISO-8601 duration without throwing.</summary>
        public static bool TryParseIsoDuration(string? value, out TimeSpan result)
        {
            if (string.IsNullOrWhiteSpace(value)) { result = TimeSpan.Zero; return false; }
            try { result = XmlConvert.ToTimeSpan(value); return true; }
            catch { result = TimeSpan.Zero; return false; }
        }

        private static readonly Regex ShorthandPattern = new(@"^\s*(\d+(?:\.\d+)?)\s*(s|sec|secs|second|seconds|m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Accepts either a raw ISO-8601 duration ("PT10M") or a human-friendly shorthand
        /// ("30s", "10m", "2h", "1d", "1.5h") — placeholders throughout the edit dialog advertise the
        /// shorthand form, but fields were previously validated with the strict ISO-8601-only
        /// <see cref="XmlConvert.ToTimeSpan(string)"/>, silently discarding anything else (item 2.3).
        /// </summary>
        public static bool TryParseFlexibleDuration(string? value, out TimeSpan result)
        {
            if (string.IsNullOrWhiteSpace(value)) { result = TimeSpan.Zero; return false; }

            var match = ShorthandPattern.Match(value);
            if (match.Success)
            {
                double amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                char unit = char.ToLowerInvariant(match.Groups[2].Value[0]);
                result = unit switch
                {
                    's' => TimeSpan.FromSeconds(amount),
                    'm' => TimeSpan.FromMinutes(amount),
                    'h' => TimeSpan.FromHours(amount),
                    'd' => TimeSpan.FromDays(amount),
                    _ => TimeSpan.Zero
                };
                return true;
            }

            return TryParseIsoDuration(value, out result);
        }

        /// <summary>Canonical wire format for trigger start times: invariant-culture, so it round-trips
        /// identically regardless of the app's display language.</summary>
        public const string ScheduleInfoFormat = "yyyy-MM-dd HH:mm:ss";

        public static string FormatScheduleInfo(DateTime value) =>
            value.ToString(ScheduleInfoFormat, CultureInfo.InvariantCulture);

        public static DateTime? TryParseScheduleInfo(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParseExact(value, ScheduleInfoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact;
            // Tolerate legacy/foreign-culture values written before this fix.
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
                return loose;
            return null;
        }
    }
}
