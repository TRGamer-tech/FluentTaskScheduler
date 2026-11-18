using System.Diagnostics.Tracing;
using System.Globalization;
using System.Numerics;
using System.Xml;

namespace FluentTaskScheduler.History;

/// <summary>
/// One record of a Windows event log, reduced to what this app reads from it and not yet
/// interpreted. <see cref="TaskEvent"/> and <see cref="SystemEvent"/> give it a meaning.
/// </summary>
/// <param name="Provider">Name of the event source that wrote the record.</param>
/// <param name="Id">The event ID. It only means something together with the provider.</param>
/// <param name="Time">When the event was logged.</param>
/// <param name="Data">The event's named data fields.</param>
public sealed record LogRecord(string Provider, int Id, DateTimeOffset Time, IReadOnlyDictionary<string, string> Data)
{
    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        // Data fields hold whatever a task's action is called; an odd character must not cost the record.
        CheckCharacters = false
    };

    /// <summary>Position in its log: a later record has a higher number.</summary>
    public long RecordId { get; init; }

    public EventLevel Level { get; init; } = EventLevel.Informational;

    /// <summary>Reads a record from the XML Windows renders an event as.</summary>
    /// <exception cref="XmlException">The text is not XML.</exception>
    /// <exception cref="FormatException">The XML is not an event: it lacks an ID or a time, or holds one that cannot be read.</exception>
    public static LogRecord FromXml(string xml)
    {
        string provider = "";
        int? id = null;
        DateTimeOffset? time = null;
        long recordId = 0;
        var level = EventLevel.Informational;
        var data = new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = XmlReader.Create(new StringReader(xml), XmlSettings);
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            // Reading an element's text leaves the reader on the node after it, so only the
            // branches that do not read text move on explicitly.
            switch (reader.LocalName)
            {
                case "Provider":
                    provider = reader.GetAttribute("Name") ?? "";
                    reader.Read();
                    break;
                case "EventID":
                    id = Number<int>(reader.ReadElementContentAsString());
                    break;
                case "Level":
                    level = (EventLevel)Number<byte>(reader.ReadElementContentAsString());
                    break;
                case "EventRecordID":
                    recordId = Number<long>(reader.ReadElementContentAsString());
                    break;
                case "TimeCreated":
                    time = ParseTime(reader.GetAttribute("SystemTime"));
                    reader.Read();
                    break;
                case "Data":
                    string? name = reader.GetAttribute("Name");
                    string value = reader.ReadElementContentAsString();
                    if (name is not null) data[name] = value;
                    break;
                default:
                    reader.Read();
                    break;
            }
        }

        if (id is null || time is null) throw new FormatException("The XML does not describe an event: it has no event ID or no time.");
        return new LogRecord(provider, id.Value, time.Value, data) { RecordId = recordId, Level = level };
    }

    private static T Number<T>(string text) where T : INumber<T> =>
        T.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out T? value)
            ? value
            : throw new FormatException($"The event holds '{text}' where a number is expected.");

    /// <summary>
    /// Windows writes the time in UTC with seven or, on older versions, nine fractional digits.
    /// .NET keeps seven, so anything beyond that is cut before parsing.
    /// </summary>
    private static DateTimeOffset ParseTime(string? text)
    {
        if (text is null) throw new FormatException("The event has no time.");

        int dot = text.IndexOf('.');
        if (dot >= 0)
        {
            int end = dot + 1;
            while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
            if (end - dot - 1 > 7) text = string.Concat(text.AsSpan(0, dot + 8), text.AsSpan(end));
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : throw new FormatException($"The event holds '{text}' where its time is expected.");
    }
}
