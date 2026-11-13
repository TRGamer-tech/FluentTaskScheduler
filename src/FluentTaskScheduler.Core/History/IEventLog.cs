namespace FluentTaskScheduler.History;

/// <summary>Whether an event log is recording.</summary>
public enum LogState
{
    Enabled,

    /// <summary>
    /// The log exists but is switched off, so nothing is written to it. Windows ships Task
    /// Scheduler's history that way, and Task Scheduler can switch it off again on its own.
    /// </summary>
    Disabled,

    /// <summary>The log does not exist, or its settings cannot be read.</summary>
    Unavailable
}

/// <summary>What became of an attempt to switch an event log on.</summary>
public enum EnableLogResult
{
    /// <summary>The log is recording now (or already was).</summary>
    Enabled,

    /// <summary>Windows refused: switching a log on takes administrator rights.</summary>
    AccessDenied,

    /// <summary>It did not work for another reason, which is in the app's log.</summary>
    Failed
}

/// <summary>
/// The Windows event log, as far as this app uses it. It exists so that everything built on the
/// log can be tested without Windows; <see cref="WindowsEventLog"/> is the real one.
/// </summary>
public interface IEventLog
{
    /// <summary>Never throws: a log that cannot be asked is <see cref="LogState.Unavailable"/>.</summary>
    LogState GetState(string log);

    EnableLogResult Enable(string log);

    /// <summary>
    /// Reads the records an XPath query selects. They are read as the result is enumerated, so a
    /// caller that stops early does not pay for the rest.
    /// </summary>
    /// <exception cref="HistoryUnavailableException">The log cannot be read; thrown while enumerating.</exception>
    IEnumerable<LogRecord> Read(string log, string query, bool newestFirst);

    /// <summary>
    /// Has <paramref name="onRecord"/> called for every record that is written from now on and
    /// matches the query, one call at a time, on a thread of its own choosing. Dispose the result
    /// to end that. <paramref name="onFailure"/> is called if the subscription breaks later; it
    /// then delivers nothing more.
    /// </summary>
    /// <remarks>A subscription to a log that is switched off succeeds and stays silent for good, even after the log is switched on. Check <see cref="GetState"/> first.</remarks>
    /// <exception cref="HistoryUnavailableException">The log cannot be subscribed to.</exception>
    IDisposable Subscribe(string log, string query, Action<LogRecord> onRecord, Action<Exception> onFailure);
}

/// <summary>An event log could not be read: it does not exist, access was denied, or Windows rejected the query.</summary>
public sealed class HistoryUnavailableException : Exception
{
    public HistoryUnavailableException(string log, Exception inner)
        : base($"The event log '{log}' could not be read: {inner.Message}", inner)
    {
    }
}
