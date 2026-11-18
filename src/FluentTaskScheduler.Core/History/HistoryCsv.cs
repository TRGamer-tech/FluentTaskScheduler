using System.Globalization;
using System.Text;

namespace FluentTaskScheduler.History;

/// <summary>A task's history as a CSV file.</summary>
public static class HistoryCsv
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// The content of a CSV file with one row per event, in the order given, under a header row.
    /// It is UTF-8 with a byte order mark, without which Excel misreads names outside ASCII.
    /// Times are ISO 8601 with the UTC offset of <paramref name="zone"/>, numbers have no
    /// grouping, and a field is quoted when it holds a comma, a quote or a line break, so the
    /// file reads the same whatever the regional settings are. Only the Event and Level columns
    /// are in the app's language.
    /// </summary>
    public static byte[] Write(IEnumerable<TaskEvent> events, TimeZoneInfo zone)
    {
        var text = new StringBuilder();
        Row(text, "Time", "EventId", "Event", "Level", "Action", "ResultCode", "User", "InstanceId");
        foreach (var e in events)
        {
            Row(text,
                TimeZoneInfo.ConvertTime(e.Time, zone).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
                e.EventId.ToString(CultureInfo.InvariantCulture),
                HistoryText.Label(e),
                HistoryText.Level(e.Level),
                e.Action,
                e.ResultCode is { } code ? HistoryText.Code(code) : "",
                e.User,
                e.InstanceId?.ToString("D") ?? "");
        }
        return [.. Utf8.GetPreamble(), .. Utf8.GetBytes(text.ToString())];
    }

    private static void Row(StringBuilder text, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) text.Append(',');
            AppendField(text, fields[i]);
        }
        text.Append("\r\n");
    }

    private static void AppendField(StringBuilder text, string field)
    {
        // A spreadsheet runs a cell that starts like this as a formula, and action names come
        // from whoever registered the task. The apostrophe makes it plain text.
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r') field = "'" + field;

        if (field.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            text.Append(field);
            return;
        }
        text.Append('"').Append(field.Replace("\"", "\"\"")).Append('"');
    }
}
