namespace FluentTaskScheduler.Shell;

/// <summary>
/// Makes sure the app runs once per Windows session. A second start asks the first instance to
/// show its window and ends.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequested;
    private RegisteredWaitHandle? _listener;

    /// <param name="waitForPrevious">How long to wait for an instance that is about to exit, as when the app restarts itself.</param>
    public SingleInstance(string name, TimeSpan waitForPrevious)
    {
        _mutex = new Mutex(initiallyOwned: false, name + "_Instance");
        try
        {
            IsFirst = _mutex.WaitOne(waitForPrevious);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance died without releasing it; the mutex is ours now.
            IsFirst = true;
        }
        _showRequested = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name + "_Show");
    }

    public bool IsFirst { get; }

    /// <summary>From a later instance: asks the first one to show itself.</summary>
    public void AskFirstToShow() => _showRequested.Set();

    /// <summary>In the first instance: runs <paramref name="show"/> on a pool thread whenever a later instance asks.</summary>
    public void OnShowRequested(Action show) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(_showRequested, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _listener?.Unregister(null);
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showRequested.Dispose();
    }
}
