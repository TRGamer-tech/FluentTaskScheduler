using System.Threading.Channels;
using Serilog;

namespace FluentTaskScheduler.History;

/// <summary>
/// The one subscription to Task Scheduler's history log, turned into notifications about runs
/// and tasks. Create it once, subscribe to its events, call <see cref="Start"/>, and dispose it
/// when the app exits. Both calls return at once and may be made from the UI thread.
/// </summary>
/// <remarks>
/// Everything happens on one background loop that is fed through a queue: the callbacks of the
/// log and of the timers only add to that queue. So nothing here waits for a handler, and
/// disposing cannot deadlock with a notification that is being delivered.
/// </remarks>
public sealed class TaskEventStream : ITaskEventStream, IDisposable
{
    /// <summary>
    /// How long a run that looks over is held back. Task Scheduler logs the reason for a stop
    /// after the completion, and after an action that could not be launched it may still launch
    /// the next one; only when nothing follows is the run's status final.
    /// </summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    /// <summary>How often the log is asked whether it has been switched on, while it is off.</summary>
    internal static readonly TimeSpan CheckWhileOff = TimeSpan.FromSeconds(3);

    /// <summary>How often the log is asked whether it is still on. A subscription does not survive the log being switched off.</summary>
    internal static readonly TimeSpan CheckWhileOn = TimeSpan.FromSeconds(15);

    private const int FollowedRuns = 1024;
    private const int Created = 0, Started = 1, Disposed = 2;

    private readonly IEventLog _log;
    private readonly TimeProvider _time;
    private readonly Channel<Message> _queue = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RunTracker _tracker = new(FollowedRuns);
    private readonly ITimer _settleTimer;
    private readonly ITimer _checkTimer;
    private IDisposable? _subscription;
    private int _subscriptionNumber;
    private bool _subscribeFailed;
    private LogState? _lastState;
    private int _phase = Created;

    public TaskEventStream(IEventLog log, TimeProvider? time = null)
    {
        _log = log;
        _time = time ?? TimeProvider.System;
        _settleTimer = _time.CreateTimer(_ => Post(new Message(MessageKind.Settle)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _checkTimer = _time.CreateTimer(_ => Post(new Message(MessageKind.CheckLog)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<TaskRun>? RunStarted;
    public event EventHandler<TaskRun>? RunCompleted;
    public event EventHandler<TaskChange>? TaskChanged;

    /// <summary>Begins listening. Calling it again changes nothing.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _phase, Started, Created) != Created) return;
        Post(new Message(MessageKind.CheckLog));
        _ = Task.Run(PumpAsync);
    }

    /// <summary>Ends the stream. A notification that is being delivered right now finishes; none is begun afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _phase, Disposed) == Disposed) return;
        _settleTimer.Dispose();
        _checkTimer.Dispose();
        _queue.Writer.TryComplete();
    }

    /// <summary>Completes when everything that has reached the stream so far has been dealt with. Tests wait on this instead of on the clock.</summary>
    internal Task WhenIdleAsync()
    {
        if (Volatile.Read(ref _phase) != Started) return Task.CompletedTask;

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Post(new Message(MessageKind.Marker, Reached: reached)) ? reached.Task : Task.CompletedTask;
    }

    private bool Post(Message message) => _queue.Writer.TryWrite(message);

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (message.Reached is { } marker)
                {
                    marker.TrySetResult();
                    continue;
                }
                if (IsDisposed) continue;

                try
                {
                    Handle(message);
                }
                // The loop is all that keeps completion actions and the history going: whatever
                // goes wrong with one message, the next one must still be handled.
                catch (Exception ex)
                {
                    Log.Error(ex, "The task event stream failed while handling a {Message} message", message.Kind);
                }
            }
        }
        finally
        {
            _subscription?.Dispose();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _phase) == Disposed;

    private void Handle(Message message)
    {
        switch (message.Kind)
        {
            case MessageKind.Record:
                OnRecord(message.Record!);
                break;
            case MessageKind.CheckLog:
                CheckLog();
                break;
            case MessageKind.SubscriptionFailed when message.SubscriptionNumber == _subscriptionNumber:
                Log.Warning(message.Error, "The subscription to the Task Scheduler history log broke; subscribing again shortly");
                Unsubscribe();
                _checkTimer.Change(CheckWhileOff, Timeout.InfiniteTimeSpan);
                break;
        }

        var now = _time.GetUtcNow();
        foreach (var run in _tracker.TakeFinished(now - SettleTime)) Raise(RunCompleted, run, "the end of a run");
        if (_tracker.OldestSettling is { } oldest) _settleTimer.Change(oldest + SettleTime - now, Timeout.InfiniteTimeSpan);
    }

    private void OnRecord(LogRecord record)
    {
        if (TaskEvent.From(record) is not { } e) return;

        TaskChangeKind? change = e.Kind switch
        {
            TaskEventKind.Registered => TaskChangeKind.Registered,
            TaskEventKind.Updated => TaskChangeKind.Updated,
            TaskEventKind.Deleted => TaskChangeKind.Deleted,
            _ => null
        };
        if (change is { } kind)
        {
            Raise(TaskChanged, new TaskChange(e.TaskPath, kind, e.Time), "a change to a task");
            return;
        }

        if (_tracker.Add(e, _time.GetUtcNow()) is { } started) Raise(RunStarted, started, "the start of a run");
    }

    /// <summary>
    /// Subscribes when the log is on and drops the subscription when it is not. A subscription
    /// made while the log is off is accepted by Windows and then never delivers anything, also
    /// not once the log is switched on, so the state has to be watched from here.
    /// </summary>
    private void CheckLog()
    {
        try
        {
            var state = _log.GetState(TaskSchedulerLog.Channel);
            if (state != _lastState)
            {
                if (state == LogState.Enabled) Log.Information("The Task Scheduler history log is on");
                else Log.Warning("The Task Scheduler history log is {State}: runs are not noticed, so completion actions and the execution history stand still until it is switched on", state);
                _lastState = state;
            }

            if (state != LogState.Enabled) Unsubscribe();
            else if (_subscription is null) Subscribe();
        }
        finally
        {
            // Whatever happened above, the next look must be scheduled, or there never is one.
            _checkTimer.Change(_subscription is null ? CheckWhileOff : CheckWhileOn, Timeout.InfiniteTimeSpan);
        }
    }

    private void Subscribe()
    {
        int number = ++_subscriptionNumber;
        try
        {
            _subscription = _log.Subscribe(
                TaskSchedulerLog.Channel,
                TaskSchedulerLog.LiveEvents,
                record => Post(new Message(MessageKind.Record, Record: record)),
                error => Post(new Message(MessageKind.SubscriptionFailed, Error: error, SubscriptionNumber: number)));
            _subscribeFailed = false;
        }
        catch (HistoryUnavailableException ex)
        {
            // Tried again every few seconds; said once.
            if (!_subscribeFailed) Log.Warning(ex, "The Task Scheduler history log could not be subscribed to; trying again until it can");
            _subscribeFailed = true;
        }
    }

    private void Unsubscribe()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void Raise<T>(EventHandler<T>? handlers, T notification, string what)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            if (IsDisposed) return;
            try
            {
                handler(this, notification);
            }
            // Subscribers are the rest of the app. One that fails must not keep the notification
            // from the others, and must not end the stream.
            catch (Exception ex)
            {
                Log.Error(ex, "A subscriber of the task event stream failed while handling {Notification}", what);
            }
        }
    }

    private enum MessageKind
    {
        Record,
        Settle,
        CheckLog,
        SubscriptionFailed,
        Marker
    }

    private readonly record struct Message(
        MessageKind Kind,
        LogRecord? Record = null,
        Exception? Error = null,
        int SubscriptionNumber = 0,
        TaskCompletionSource? Reached = null);
}
