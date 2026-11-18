namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// A program that a page runs and shows while it runs: the script in the editor, or a quick
/// action. The run reports to the view model that shows it, on the UI thread, and that view
/// model is the one to end it before its time.
/// </summary>
internal abstract class ProgramRun(IUiThread ui)
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Calls the view model that shows the run; null while there is none.</summary>
    private Action? _viewModel;
    private int _reportQueued;

    /// <summary>True once the program has ended, in whichever way, or has turned out not to start.</summary>
    public bool HasEnded { get; private set; }

    /// <summary>For the program: asked for when the run is to end before its time.</summary>
    protected CancellationToken Cancellation => _cancellation.Token;

    /// <summary>
    /// Makes a view model the one that shows the run. It is called at once, to show the run as it
    /// stands, and from then on whenever there is news of the run. Call on the UI thread.
    /// </summary>
    public void ReportTo(Action viewModel)
    {
        _viewModel = viewModel;
        viewModel();
    }

    /// <summary>Ends the program. The run reports its end once the program has gone.</summary>
    public void Stop()
    {
        if (!HasEnded) _cancellation.Cancel();
    }

    /// <summary>Ends the program for a view model that will not be there to show how it ended.</summary>
    public void Abandon()
    {
        _viewModel = null;
        Stop();
    }

    /// <summary>
    /// Lets the view model know that there is news, from any thread. One call reaches it for all
    /// the news that arrives before the UI thread gets to it.
    /// </summary>
    protected void Report()
    {
        if (Interlocked.Exchange(ref _reportQueued, 1) == 0) ui.Post(ReportQueued);
    }

    /// <summary>
    /// Reports that the program has ended, as the last thing a run does. Call on the UI thread,
    /// which is where a run that was started there continues after it has awaited the program.
    /// </summary>
    protected void ReportEnd()
    {
        HasEnded = true;
        _cancellation.Dispose();
        _viewModel?.Invoke();
    }

    private void ReportQueued()
    {
        Volatile.Write(ref _reportQueued, 0);

        // The end has shown everything, and the view model may have gone on to another run since.
        if (!HasEnded) _viewModel?.Invoke();
    }
}
