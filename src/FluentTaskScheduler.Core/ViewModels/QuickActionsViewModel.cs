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
    private readonly QuickAction _action;
    private readonly IProcessRunner _runner;
    private readonly IUiThread _ui;
    private readonly IPrompts _prompts;

    /// <summary>The run the card shows, in progress or ended; null while it shows none.</summary>
    private QuickActionRun? _run;

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
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CancelCommand))]
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

    private bool CanRun => IsAvailable && IsNotRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (_action.ConfirmFirst)
        {
            bool confirmed = await _prompts.ConfirmAsync(Title, Loc.Get($"QuickActions.{Id}.Confirm"), Loc.Get("QuickActions.RunBtn"), isDestructive: true);
            if (!confirmed) return;
        }

        Show(QuickActionRun.Start(_action, _runner, _ui));
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _run?.Stop();

    /// <summary>Takes over the run that the card of this action showed on the page this one replaces.</summary>
    internal void ContinueFrom(QuickActionViewModel previous)
    {
        if (previous._run is not { } run) return;

        previous._run = null;
        Show(run);
    }

    /// <summary>Ends the action if it is running and no other card has taken its run over.</summary>
    internal void EndRun() => _run?.Abandon();

    /// <summary>Shows a run from now on: how it is doing, and then how it ended.</summary>
    private void Show(QuickActionRun run)
    {
        _run = run;
        run.ReportTo(() =>
        {
            State = run.State;
            StatusText = StatusOf(run);
            Detail = run.Detail;
        });
    }

    /// <summary>How a run is doing or how it ended, in the language the app has now.</summary>
    private static string StatusOf(QuickActionRun run) => run.State switch
    {
        QuickActionState.Running => Loc.Get("QuickActions.Status.Running"),
        QuickActionState.Succeeded => Loc.Get("QuickActions.Status.Success"),
        QuickActionState.Failed when run.ExitCode is { } exitCode => Loc.Format("QuickActions.Status.ExitCode", exitCode),
        QuickActionState.Failed => Loc.Get("QuickActions.Status.Error"),
        // Idle again, after a run: it was stopped.
        _ => Loc.Get("QuickActions.Status.Cancelled")
    };
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
    /// Takes over from the page this one replaces what its actions are running, and how the ones
    /// that have ended went. A window gets new pages and view models when, for one, its language
    /// changes. The page that was replaced shows nothing more after this, and disposing of it
    /// ends nothing.
    /// </summary>
    public void ContinueFrom(QuickActionsViewModel previous)
    {
        foreach (var (action, replaced) in Actions.Zip(previous.Actions)) action.ContinueFrom(replaced);
    }

    /// <summary>Ends the actions that are running and that no other page has taken over.</summary>
    public void Dispose()
    {
        foreach (QuickActionViewModel action in Actions) action.EndRun();
    }
}
