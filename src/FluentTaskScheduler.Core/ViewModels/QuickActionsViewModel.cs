using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.ViewModels;

public enum QuickActionState
{
    /// <summary>Not run yet, or stopped by the user.</summary>
    Idle,
    Running,
    Succeeded,
    Failed
}

/// <summary>One quick action on the page: what it is, whether it can run here, and how its last run went.</summary>
public sealed partial class QuickActionViewModel : ObservableObject
{
    /// <summary>How many lines of what the program wrote are shown once it has ended.</summary>
    private const int LinesKept = 4;

    private readonly QuickAction _action;
    private readonly IProcessRunner _runner;
    private readonly IUiThread _ui;
    private readonly IPrompts _prompts;

    private readonly List<string> _lastLines = [];
    private bool _lastLineIsUnfinished;
    private bool _refreshQueued;
    private CancellationTokenSource? _run;

    internal QuickActionViewModel(QuickAction action, bool appIsElevated, IProcessRunner runner, IUiThread ui, IPrompts prompts)
    {
        _action = action;
        _runner = runner;
        _ui = ui;
        _prompts = prompts;
        IsAvailable = appIsElevated || !action.RequiresElevation;
    }

    public string Id => _action.Id;
    public string Glyph => _action.Glyph;
    public string Title => Loc.Get($"QuickActions.{Id}.Title");
    public string Description => Loc.Get($"QuickActions.{Id}.Desc");
    public bool RequiresElevation => _action.RequiresElevation;

    /// <summary>False for an action that needs administrator rights while the app runs without them.</summary>
    public bool IsAvailable { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsNotRunning), nameof(HasSucceeded), nameof(HasFailed))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private QuickActionState _state;

    /// <summary>How the action is doing or how it ended, in a few words.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// The program's own words: the line it wrote last while it runs, and afterwards its last
    /// lines if they are its findings or the reason it failed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detail = "";

    public bool IsRunning => State == QuickActionState.Running;
    public bool IsNotRunning => !IsRunning;
    public bool HasSucceeded => State == QuickActionState.Succeeded;
    public bool HasFailed => State == QuickActionState.Failed;
    public bool HasDetail => Detail.Length > 0;

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private async Task RunAsync()
    {
        if (_action.ConfirmFirst)
        {
            bool confirmed = await _prompts.ConfirmAsync(Title, Loc.Get($"QuickActions.{Id}.Confirm"), Loc.Get("QuickActions.RunBtn"), isDestructive: true);
            if (!confirmed) return;
        }

        using var run = new CancellationTokenSource();
        _run = run;
        lock (_lastLines)
        {
            _lastLines.Clear();
            _lastLineIsUnfinished = false;
        }
        Show(QuickActionState.Running, Loc.Get("QuickActions.Status.Running"), detail: "");
        try
        {
            int exitCode = await _runner.RunAsync(_action.Request, Keep, run.Token);
            if (exitCode == 0) Show(QuickActionState.Succeeded, Loc.Get("QuickActions.Status.Success"), _action.ReportsFindings ? LastLines() : "");
            else Show(QuickActionState.Failed, Loc.Format("QuickActions.Status.ExitCode", exitCode), LastLines());
        }
        catch (OperationCanceledException)
        {
            Show(QuickActionState.Idle, Loc.Get("QuickActions.Status.Cancelled"), detail: "");
        }
        catch (ProcessStartException ex)
        {
            Serilog.Log.Error(ex, "Quick action {Id} could not start {Program}", Id, ex.FileName);
            Show(QuickActionState.Failed, Loc.Get("QuickActions.Status.Error"), ex.Message);
        }
        finally
        {
            _run = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _run?.Cancel();

    private void Show(QuickActionState state, string statusText, string detail)
    {
        State = state;
        StatusText = statusText;
        Detail = detail;
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

            // One refresh shows the newest line, however many arrived before the UI thread got to it.
            if (_refreshQueued) return;
            _refreshQueued = true;
        }
        _ui.Post(ShowNewestLine);
    }

    private void ShowNewestLine()
    {
        string newest;
        lock (_lastLines)
        {
            _refreshQueued = false;
            newest = _lastLines.LastOrDefault() ?? "";
        }
        if (IsRunning) Detail = newest;
    }

    private string LastLines()
    {
        lock (_lastLines) return string.Join('\n', _lastLines);
    }
}

/// <summary>The Quick Actions page of one window.</summary>
public sealed partial class QuickActionsViewModel : IDisposable
{
    private readonly IAppLifetime _lifetime;

    public QuickActionsViewModel(IProcessRunner runner, IAppLifetime lifetime, IUiThread ui, IPrompts prompts)
    {
        _lifetime = lifetime;
        Actions = QuickAction.All
            .Select(action => new QuickActionViewModel(action, lifetime.IsElevated, runner, ui, prompts))
            .ToList();
    }

    public IReadOnlyList<QuickActionViewModel> Actions { get; }

    /// <summary>True when actions are unavailable for lack of administrator rights, which a restart can provide.</summary>
    public bool OffersRestartAsAdministrator => Actions.Any(action => !action.IsAvailable);

    /// <summary>Ends this instance of the app and starts it again as administrator, if the user agrees to that.</summary>
    [RelayCommand]
    private void RestartAsAdministrator() => _lifetime.RestartElevated();

    /// <summary>
    /// Ends the actions that are still running. The window is closing, or it is getting new pages
    /// (in another language, for one), which know nothing of these runs.
    /// </summary>
    public void Dispose()
    {
        foreach (QuickActionViewModel action in Actions) action.CancelCommand.Execute(null);
    }
}
