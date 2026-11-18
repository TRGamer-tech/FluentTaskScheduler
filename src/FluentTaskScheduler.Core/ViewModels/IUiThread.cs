namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// Brings work to the thread that owns the UI. Background services raise their events on
/// whatever thread they run on; a view model that reacts to them posts its changes here.
/// </summary>
public interface IUiThread
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread.</summary>
    void Post(Action action);
}

/// <summary>Posts to the synchronization context of the thread it was created on.</summary>
public sealed class UiThread : IUiThread
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current
        ?? throw new InvalidOperationException("Create UiThread on the UI thread, after its synchronization context is set.");

    public void Post(Action action) => _context.Post(static state => ((Action)state!)(), action);
}
