using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// One run of a quick action: the program, the last lines it wrote, and how it ended. The card
/// of the action keeps the run after its end as well: the card that replaces it on a new page
/// then says in its own language how the run went.
/// </summary>
internal sealed class QuickActionRun : ProgramRun
{
    /// <summary>How many lines of what the program wrote are shown once it has ended.</summary>
    private const int LinesKept = 4;

    private readonly QuickAction _action;
    private readonly List<string> _lastLines = [];
    private bool _lastLineIsUnfinished;
    private string _findings = "";

    private QuickActionRun(QuickAction action, IUiThread ui) : base(ui) => _action = action;

    /// <summary>Starts the program of an action. Call on the UI thread.</summary>
    public static QuickActionRun Start(QuickAction action, IProcessRunner runner, IUiThread ui)
    {
        var run = new QuickActionRun(action, ui);
        _ = run.RunAsync(runner);
        return run;
    }

    /// <summary>Running until the program has ended; idle afterwards for a run that was stopped.</summary>
    public QuickActionState State { get; private set; } = QuickActionState.Running;

    /// <summary>What the program ended with; null while it runs, and when it was stopped or could not be started.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// The program's own words: the line it wrote last while it runs, and afterwards its last
    /// lines if they are its findings or the reason it failed.
    /// </summary>
    public string Detail => HasEnded ? _findings : NewestLine();

    private async Task RunAsync(IProcessRunner runner)
    {
        try
        {
            int exitCode = await runner.RunAsync(_action.Request, Keep, Cancellation);
            ExitCode = exitCode;
            if (exitCode == 0) End(QuickActionState.Succeeded, _action.ReportsFindings ? LastLines() : "");
            else End(QuickActionState.Failed, LastLines());
        }
        catch (OperationCanceledException)
        {
            End(QuickActionState.Idle, findings: "");
        }
        catch (ProcessStartException ex)
        {
            Serilog.Log.Error(ex, "Quick action {Id} could not start {Program}", _action.Id, ex.FileName);
            End(QuickActionState.Failed, ex.Message);
        }
    }

    private void End(QuickActionState state, string findings)
    {
        State = state;
        _findings = findings;
        ReportEnd();
    }

    /// <summary>Takes a line the program wrote, on whatever thread it arrives.</summary>
    private void Keep(ProcessOutput line)
    {
        string text = line.Text.Trim();
        lock (_lastLines)
        {
            // A line the program had not finished (its progress, usually) gives way to what it wrote next.
            if (_lastLineIsUnfinished) _lastLines.RemoveAt(_lastLines.Count - 1);
            _lastLineIsUnfinished = false;
            if (text.Length == 0) return;

            _lastLines.Add(text);
            if (_lastLines.Count > LinesKept) _lastLines.RemoveAt(0);
            _lastLineIsUnfinished = !line.IsComplete;
        }
        Report();
    }

    private string NewestLine()
    {
        lock (_lastLines) return _lastLines.LastOrDefault() ?? "";
    }

    private string LastLines()
    {
        lock (_lastLines) return string.Join('\n', _lastLines);
    }
}
