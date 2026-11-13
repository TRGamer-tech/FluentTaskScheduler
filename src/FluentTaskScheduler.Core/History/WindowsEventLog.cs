using System.Diagnostics.Eventing.Reader;
using System.Xml;
using Serilog;

namespace FluentTaskScheduler.History;

/// <summary>The event log of this computer. The only code in the app that talks to it.</summary>
public sealed class WindowsEventLog : IEventLog
{
    public LogState GetState(string log)
    {
        try
        {
            using var configuration = new EventLogConfiguration(log);
            return configuration.IsEnabled ? LogState.Enabled : LogState.Disabled;
        }
        catch (Exception ex) when (IsLogFailure(ex))
        {
            // Asked every few seconds while the log is unusable, so this must not fill the app's log.
            Log.Debug(ex, "The state of the event log {Log} could not be read", log);
            return LogState.Unavailable;
        }
    }

    public EnableLogResult Enable(string log)
    {
        try
        {
            using var configuration = new EventLogConfiguration(log);
            if (!configuration.IsEnabled)
            {
                configuration.IsEnabled = true;
                configuration.SaveChanges();
            }
            return EnableLogResult.Enabled;
        }
        catch (UnauthorizedAccessException)
        {
            return EnableLogResult.AccessDenied;
        }
        catch (EventLogException ex)
        {
            Log.Error(ex, "The event log {Log} could not be switched on", log);
            return EnableLogResult.Failed;
        }
    }

    public IEnumerable<LogRecord> Read(string log, string query, bool newestFirst)
    {
        using var reader = Open(log, query, newestFirst);
        while (Next(reader, log) is { } record)
        {
            LogRecord? converted;
            using (record) converted = Convert(record);
            if (converted is not null) yield return converted;
        }
    }

    public IDisposable Subscribe(string log, string query, Action<LogRecord> onRecord, Action<Exception> onFailure) =>
        new Subscription(log, query, onRecord, onFailure);

    private static EventLogReader Open(string log, string query, bool newestFirst)
    {
        try
        {
            return new EventLogReader(new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = newestFirst });
        }
        catch (Exception ex) when (IsLogFailure(ex))
        {
            throw new HistoryUnavailableException(log, ex);
        }
    }

    private static EventRecord? Next(EventLogReader reader, string log)
    {
        try
        {
            return reader.ReadEvent();
        }
        catch (Exception ex) when (IsLogFailure(ex))
        {
            throw new HistoryUnavailableException(log, ex);
        }
    }

    /// <summary>Null for a record that cannot be read: one damaged record must not hide the rest of the log.</summary>
    private static LogRecord? Convert(EventRecord record)
    {
        try
        {
            return LogRecord.FromXml(record.ToXml());
        }
        catch (Exception ex) when (ex is EventLogException or XmlException or FormatException)
        {
            Log.Warning(ex, "A record of the event log was skipped because it could not be read");
            return null;
        }
    }

    private static bool IsLogFailure(Exception ex) => ex is EventLogException or UnauthorizedAccessException;

    private sealed class Subscription : IDisposable
    {
        private readonly EventLogWatcher _watcher;
        private readonly Action<LogRecord> _onRecord;
        private readonly Action<Exception> _onFailure;
        private Exception? _refusal;
        private volatile bool _accepted;

        public Subscription(string log, string query, Action<LogRecord> onRecord, Action<Exception> onFailure)
        {
            _onRecord = onRecord;
            _onFailure = onFailure;
            _watcher = new EventLogWatcher(new EventLogQuery(log, PathType.LogName, query));
            _watcher.EventRecordWritten += OnWritten;

            try
            {
                _watcher.Enabled = true;
            }
            catch (Exception ex) when (IsLogFailure(ex))
            {
                _watcher.Dispose();
                throw new HistoryUnavailableException(log, ex);
            }

            // The watcher does not check whether Windows accepted the subscription. A refused one
            // shows as a callback that carries the error, made on this thread before Enabled returns.
            if (_refusal is { } refused)
            {
                _watcher.Dispose();
                throw new HistoryUnavailableException(log, refused);
            }
            _accepted = true;
        }

        public void Dispose() => _watcher.Dispose();

        private void OnWritten(object? sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventRecord is not { } record)
            {
                var failure = e.EventException ?? new EventLogException("The event log ended the subscription without giving a reason.");
                if (_accepted) _onFailure(failure);
                else _refusal = failure;
                return;
            }

            LogRecord? converted;
            using (record) converted = Convert(record);
            if (converted is not null) _onRecord(converted);
        }
    }
}
