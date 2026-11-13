using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>
/// Event logs in memory, behaving like <see cref="WindowsEventLog"/> where callers depend on it:
/// records come back in log order or newest first, are only read as far as the caller
/// enumerates, and a subscription made while the log is off stays silent for good, as does one
/// whose log is switched off later. Queries are recorded, not evaluated: a test puts in what the
/// query would select.
/// </summary>
public sealed class FakeEventLog : IEventLog
{
    private readonly Dictionary<string, List<LogRecord>> _records = [];
    private readonly List<Subscription> _subscriptions = [];
    private readonly object _gate = new();
    private LogState _state = LogState.Enabled;

    /// <summary>The state of every log. Switching a log off silences the subscriptions it has.</summary>
    public LogState State
    {
        get => _state;
        set
        {
            lock (_gate)
            {
                _state = value;
                if (value != LogState.Enabled) _subscriptions.ForEach(subscription => subscription.Silence());
            }
        }
    }

    /// <summary>What <see cref="Enable"/> answers. Only <see cref="EnableLogResult.Enabled"/> also switches the log on.</summary>
    public EnableLogResult EnableResult { get; set; } = EnableLogResult.Enabled;

    /// <summary>Makes reading and subscribing fail the way an unreadable log does.</summary>
    public bool Unreadable { get; set; }

    /// <summary>Makes asking for the state throw, which the real log is not supposed to do.</summary>
    public Exception? StateFailure { get; set; }

    /// <summary>Every query that was read, in order.</summary>
    public List<(string Log, string Query, bool NewestFirst)> Queries { get; } = [];

    /// <summary>How many records readers have taken so far.</summary>
    public int RecordsRead { get; private set; }

    /// <summary>Called for each record just before a reader gets it.</summary>
    public Action<LogRecord>? Reading { get; set; }

    /// <summary>How often a subscription was made, successful or not.</summary>
    public int SubscribeCalls { get; private set; }

    /// <summary>The subscriptions that would deliver a record right now.</summary>
    public int LiveSubscriptions
    {
        get { lock (_gate) return _subscriptions.Count(subscription => subscription.IsLive); }
    }

    /// <summary>The query of the most recent subscription.</summary>
    public string? SubscribedQuery { get; private set; }

    /// <summary>Puts records into a log for readers to find.</summary>
    public FakeEventLog Add(string log, params LogRecord[] records)
    {
        if (!_records.TryGetValue(log, out var list)) _records[log] = list = [];
        list.AddRange(records);
        return this;
    }

    /// <summary>Writes a record now: live subscriptions receive it, on the calling thread.</summary>
    public void Write(LogRecord record)
    {
        Subscription[] live;
        lock (_gate) live = _subscriptions.Where(subscription => subscription.IsLive).ToArray();
        foreach (var subscription in live) subscription.Deliver(record);
    }

    /// <summary>Breaks every live subscription the way the log does when it is cleared or its service restarts.</summary>
    public void BreakSubscriptions(Exception error)
    {
        Subscription[] live;
        lock (_gate) live = _subscriptions.Where(subscription => subscription.IsLive).ToArray();
        foreach (var subscription in live) subscription.Break(error);
    }

    public LogState GetState(string log) => StateFailure is null ? State : throw StateFailure;

    public EnableLogResult Enable(string log)
    {
        if (EnableResult == EnableLogResult.Enabled) State = LogState.Enabled;
        return EnableResult;
    }

    public IEnumerable<LogRecord> Read(string log, string query, bool newestFirst)
    {
        Queries.Add((log, query, newestFirst));
        if (Unreadable) throw new HistoryUnavailableException(log, new UnauthorizedAccessException());

        var records = _records.GetValueOrDefault(log, []).OrderBy(record => record.RecordId).ToList();
        if (newestFirst) records.Reverse();
        foreach (var record in records)
        {
            RecordsRead++;
            Reading?.Invoke(record);
            yield return record;
        }
    }

    public IDisposable Subscribe(string log, string query, Action<LogRecord> onRecord, Action<Exception> onFailure)
    {
        SubscribeCalls++;
        SubscribedQuery = query;
        if (Unreadable) throw new HistoryUnavailableException(log, new UnauthorizedAccessException());

        var subscription = new Subscription(onRecord, onFailure);
        lock (_gate)
        {
            if (_state != LogState.Enabled) subscription.Silence();
            _subscriptions.Add(subscription);
        }
        return subscription;
    }

    private sealed class Subscription(Action<LogRecord> onRecord, Action<Exception> onFailure) : IDisposable
    {
        private volatile bool _silent;

        public bool IsLive => !_silent;
        public void Silence() => _silent = true;
        public void Dispose() => _silent = true;

        public void Deliver(LogRecord record)
        {
            if (!_silent) onRecord(record);
        }

        public void Break(Exception error)
        {
            _silent = true;
            onFailure(error);
        }
    }
}
