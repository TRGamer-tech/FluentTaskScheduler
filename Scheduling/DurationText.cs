using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FluentTaskScheduler.Scheduling
{
    /// <summary>
    /// Durations as people type them ("30s", "10m", "1h 30m", "2d") and as Task Scheduler stores
    /// them (ISO 8601, "PT10M").
    /// </summary>
    public static class DurationText
    {
        private static readonly Regex Part = new(
            @"\G\s*(?<n>\d+(?:[.,]\d+)?)\s*(?<u>days?|d|hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Parses the shorthand form or an ISO 8601 duration. An empty string is not a duration.</summary>
        public static bool TryParse(string? text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            if (text[0] is 'P' or 'p')
            {
                try { value = XmlConvert.ToTimeSpan(text.ToUpperInvariant()); return value >= TimeSpan.Zero; }
                catch (FormatException) { return false; }
            }

            double seconds = 0;
            int position = 0;
            while (position < text.Length)
            {
                var match = Part.Match(text, position);
                if (!match.Success || match.Length == 0) return false;

                double amount = double.Parse(match.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                seconds += char.ToLowerInvariant(match.Groups["u"].Value[0]) switch
                {
                    'd' => amount * 86400,
                    'h' => amount * 3600,
                    'm' => amount * 60,
                    _ => amount
                };
                position = match.Index + match.Length;
            }

            if (seconds > TimeSpan.MaxValue.TotalSeconds) return false;
            value = TimeSpan.FromSeconds(Math.Round(seconds));
            return true;
        }

        /// <summary>Writes a duration in the shorthand form, e.g. "1h 30m". Zero gives an empty string.</summary>
        public static string Format(TimeSpan value)
        {
            if (value <= TimeSpan.Zero) return "";
            var text = new StringBuilder();
            void Append(int amount, char unit)
            {
                if (amount == 0) return;
                if (text.Length > 0) text.Append(' ');
                text.Append(amount.ToString(CultureInfo.InvariantCulture)).Append(unit);
            }
            Append(value.Days, 'd');
            Append(value.Hours, 'h');
            Append(value.Minutes, 'm');
            Append(value.Seconds, 's');
            return text.ToString();
        }
    }
}
