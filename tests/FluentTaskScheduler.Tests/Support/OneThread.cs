using System.Collections.Concurrent;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>
/// Runs a test on one thread, the way the app runs its view models on the UI thread. Under xUnit,
/// what follows an await can run on any of several threads at once, so two steps of a view model
/// that the app takes one after the other can overlap, and the older can finish last.
/// </summary>
public static class OneThread
{
    public static void Run(Func<Task> test)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        using var thread = new Context();
        SynchronizationContext.SetSynchronizationContext(thread);
        try
        {
            Task done = test();
            done.ContinueWith(_ => thread.Complete(), TaskScheduler.Default);
            thread.RunUntilComplete();
            done.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class Context : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            // What is posted once the test is over has nobody left to run it.
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) { }
        }

        public void Complete() => _queue.CompleteAdding();

        public void RunUntilComplete()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }

        public void Dispose() => _queue.Dispose();
    }
}
