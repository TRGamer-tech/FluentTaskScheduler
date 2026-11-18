namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The reads of Task Scheduler a view model asked for, done one at a time and in the order they
/// were asked for. A slow read of all tasks then cannot overwrite what a later read found.
/// </summary>
internal sealed class ReadQueue
{
    private Task _last = Task.CompletedTask;

    /// <summary>Completes when every read asked for so far is done.</summary>
    public Task Pending => _last;

    /// <summary>Does <paramref name="read"/> once the reads asked for before it are done.</summary>
    /// <param name="read">Deals with its own failures and never throws: the reads after it wait for it.</param>
    public Task Enqueue(Func<Task> read)
    {
        Task earlier = _last;
        return _last = ReadAsync();

        async Task ReadAsync()
        {
            await earlier;
            await read();
        }
    }
}
