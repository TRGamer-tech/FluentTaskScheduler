using System;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FluentTaskScheduler.Scheduling
{
    /// <summary>
    /// The simple "log, source, event ID" form of an event trigger subscription. Anything richer
    /// than that is only handled as raw query XML.
    /// </summary>
    public static class EventQuery
    {
        // *                                              any event of the log
        // *[System[Provider[@Name='x']]]                 by source
        // *[System[EventID=1]] or *[System[(EventID=1)]] by ID
        // *[System[Provider[@Name='x'] and EventID=1]]   by both
        private static readonly Regex SimpleSelect = new(
            @"^\*(?:\[System\[(?:Provider\[@Name=(?:'(?<s>[^']*)'|""(?<s>[^""]*)"")\])?(?<and>\s+and\s+)?(?:\(?EventID=(?<id>\d+)\)?)?\]\])?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Builds the subscription XML for events of <paramref name="log"/>, optionally narrowed by source and event ID.</summary>
        public static string Build(string log, string source, int? eventId)
        {
            log = string.IsNullOrWhiteSpace(log) ? "Application" : log.Trim();
            source = source?.Trim() ?? "";
            if (log.Any(char.IsControl)) throw new ArgumentException("The event log name contains control characters.", nameof(log));
            if (source.Any(char.IsControl)) throw new ArgumentException("The event source contains control characters.", nameof(source));

            string select = "*";
            if (source.Length > 0 || eventId.HasValue)
            {
                string provider = source.Length > 0 ? $"Provider[@Name={ToXPathLiteral(source)}]" : "";
                string id = eventId.HasValue ? $"(EventID={eventId.Value.ToString(CultureInfo.InvariantCulture)})" : "";
                string and = provider.Length > 0 && id.Length > 0 ? " and " : "";
                select = $"*[System[{provider}{and}{id}]]";
            }

            string logXml = SecurityElement.Escape(log);
            return $"<QueryList><Query Id=\"0\" Path=\"{logXml}\"><Select Path=\"{logXml}\">{SecurityElement.Escape(select)}</Select></Query></QueryList>";
        }

        /// <summary>
        /// Reads log, source and event ID out of a subscription, if it is one <see cref="Build"/>
        /// could have produced (or the equivalent Windows writes for a basic event trigger).
        /// </summary>
        public static bool TryParse(string? subscription, out string log, out string source, out int? eventId)
        {
            log = ""; source = ""; eventId = null;
            if (string.IsNullOrWhiteSpace(subscription)) return false;

            XElement root;
            try { root = XElement.Parse(subscription); }
            catch (System.Xml.XmlException) { return false; }

            if (root.Name.LocalName != "QueryList") return false;
            var queries = root.Elements().ToList();
            if (queries.Count != 1 || queries[0].Name.LocalName != "Query") return false;
            var parts = queries[0].Elements().ToList();
            if (parts.Count != 1 || parts[0].Name.LocalName != "Select") return false;

            string? path = (string?)parts[0].Attribute("Path") ?? (string?)queries[0].Attribute("Path");
            if (string.IsNullOrWhiteSpace(path)) return false;

            var match = SimpleSelect.Match(parts[0].Value.Trim());
            if (!match.Success) return false;

            bool hasSource = match.Groups["s"].Success;
            bool hasId = match.Groups["id"].Success;
            // "and" must sit between two conditions and nowhere else.
            if (match.Groups["and"].Success != (hasSource && hasId)) return false;
            // The pattern also matches "*[System[]]", which is not a valid query.
            if (parts[0].Value.Trim() != "*" && !hasSource && !hasId) return false;

            int? id = null;
            if (hasId)
            {
                if (!int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) return false;
                id = parsed;
            }

            log = path;
            source = hasSource ? match.Groups["s"].Value : "";
            eventId = id;
            return true;
        }

        /// <summary>
        /// Encodes a string as an XPath 1.0 string literal. XPath 1.0 cannot escape a quote inside a
        /// literal, so a value containing both ' and " is assembled with concat().
        /// </summary>
        public static string ToXPathLiteral(string? value)
        {
            value ??= "";
            if (!value.Contains('\'')) return $"'{value}'";
            if (!value.Contains('"')) return $"\"{value}\"";
            return "concat(" + string.Join(", \"'\", ", value.Split('\'').Select(part => $"'{part}'")) + ")";
        }
    }
}
