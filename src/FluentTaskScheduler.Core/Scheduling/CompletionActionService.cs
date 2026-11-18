using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Settings;
using Serilog;

namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Carries out completion actions: when a run of a task is over, it starts the tasks that task
/// lists for that outcome. It works from construction to disposal, as long as the setting
/// <see cref="AppSettings.EnableTaskPipelines"/> is on and the app is running; runs that end
/// while the app is closed start nothing.
/// </summary>
/// <remarks>
/// <para>Which outcome starts what, and how cycles and storms are kept from happening, is
/// decided by <see cref="CompletionActionRules"/>.</para>
/// <para>A task is started the way a trigger would start it: its conditions apply, and so does
/// its rule for an instance that is already running. Task Scheduler may therefore ignore or
/// queue the request. The user is told that a task was started when its start shows up in the
/// log, not when it was asked for.</para>
/// </remarks>
public sealed class CompletionActionService : IDisposable
{
    private readonly ITaskEventStream _stream;
    private readonly ITaskStore _store;
    private readonly ITaskRunner _runner;
    private readonly ISettingsStore _settings;
    private readonly INotifier _notifier;
    private readonly CompletionActionRules _rules;
    private readonly RunClaims _claims = new();

    public CompletionActionService(ITaskEventStream stream, ITaskStore store, ITaskRunner runner, ISettingsStore settings, INotifier notifier, TimeProvider? time = null)
    {
        _stream = stream;
        _store = store;
        _runner = runner;
        _settings = settings;
        _notifier = notifier;
        _rules = new CompletionActionRules(time ?? TimeProvider.System);
        _stream.RunStarted += OnRunStarted;
        _stream.RunCompleted += OnRunCompleted;
    }

    public void Dispose()
    {
        _stream.RunStarted -= OnRunStarted;
        _stream.RunCompleted -= OnRunCompleted;
        _claims.Dispose();
    }

    private void OnRunStarted(object? sender, TaskRun run)
    {
        ReportOverdue();
        if (_rules.Started(run) is not { } request) return;

        Log.Information("Completion action: {Target} started because {Source} {Outcome}", request.Target, request.Source, OutcomeOf(request.AfterSuccess));
        _notifier.CompletionActionStarted(TaskPaths.NameOf(request.Source), TaskPaths.NameOf(request.Target), request.AfterSuccess);
    }

    private void OnRunCompleted(object? sender, TaskRun run)
    {
        ReportOverdue();

        // Read now rather than cached, so what is acted on is what the task says at this moment.
        var actions = _settings.Current.EnableTaskPipelines ? _store.GetCompletionActions(run.TaskPath) : CompletionActions.None;
        if (actions.IsActive && !_claims.TryClaim(run))
        {
            Log.Debug("The completion actions of {Source} are left to another running copy of the app", run.TaskPath);
            actions = CompletionActions.None;
        }

        var plan = _rules.Decide(run, actions);
        if (plan.Unstarted is { } unstarted) ReportCouldNotStart(unstarted, run);

        foreach (var (target, verdict) in plan.Decisions)
        {
            switch (verdict)
            {
                case CompletionActionRules.Verdict.Start:
                    Start(target, plan);
                    break;
                case CompletionActionRules.Verdict.AlreadyInChain:
                    Log.Warning("Completion action: {Target} is not started after {Source} because it is already part of the chain {Chain}", target, plan.Source, plan.Chain);
                    break;
                case CompletionActionRules.Verdict.StartedTooOften:
                    Log.Warning("Completion action: {Target} is not started after {Source} because completion actions started it {Count} times within {Minutes} minutes",
                        target, plan.Source, CompletionActionRules.MaxStartsPerTask, CompletionActionRules.StartsCountedFor.TotalMinutes);
                    break;
            }
        }
    }

    private void Start(string target, CompletionActionRules.Plan plan)
    {
        RunOutcome outcome;
        try
        {
            outcome = _runner.Run(target, RunOrigin.CompletionAction);
        }
        catch (Exception ex) when (ex is TaskNotFoundException or TaskAccessDeniedException or COMException or IOException)
        {
            // The runner has already logged the cause and told the user. The other tasks are still to be started.
            Log.Warning("Completion action: {Target} could not be started after {Source} {Outcome}: {Reason}", target, plan.Source, OutcomeOf(plan.AfterSuccess), ex.Message);
            return;
        }

        if (outcome == RunOutcome.Suppressed)
        {
            Log.Information("Completion action: {Target} is not started after {Source} because tasks are snoozed", target, plan.Source);
            return;
        }
        _rules.Requested(target, plan);
    }

    private void ReportOverdue()
    {
        foreach (var request in _rules.TakeOverdue())
        {
            Log.Warning("Completion action: {Target} was asked to start after {Source} {Outcome}, but did not start. Task Scheduler ignores or queues a request when the task is already running or its conditions are not met",
                request.Target, request.Source, OutcomeOf(request.AfterSuccess));
        }
    }

    private void ReportCouldNotStart(CompletionActionRules.Request request, TaskRun run)
    {
        string reason = run.ResultCode is { } code ? $"{WindowsMessage(code)} ({HistoryText.Code(code)})" : "";
        Log.Warning("Completion action: {Target} could not be started after {Source} {Outcome}: {Reason}", request.Target, request.Source, OutcomeOf(request.AfterSuccess), reason);
        _notifier.TaskCouldNotStart(TaskPaths.NameOf(request.Target), reason);
    }

    /// <summary>Windows' own text for an error, in the language of Windows.</summary>
    private static string WindowsMessage(uint code)
    {
        // Messages are looked up by plain error number; most failures arrive wrapped as 0x8007nnnn.
        uint error = (code & 0xFFFF0000) == 0x80070000 ? code & 0xFFFF : code;
        return new Win32Exception(unchecked((int)error)).Message;
    }

    private static string OutcomeOf(bool afterSuccess) => afterSuccess ? "succeeded" : "failed";

    /// <summary>
    /// Makes sure a run is acted on once on this computer, also when the app runs in the sessions
    /// of two users at the same time: both see the same log. The first copy to create a mutex
    /// named after the run acts; the other finds it there and stands back.
    /// </summary>
    private sealed class RunClaims : IDisposable
    {
        /// <summary>Both copies see a run within seconds of each other, so a claim only has to outlive that. Keeping this many is far more than enough.</summary>
        private const int Kept = 64;

        private readonly Queue<Mutex> _held = new();

        public bool TryClaim(TaskRun run)
        {
            string identity = $"{run.TaskPath.ToUpperInvariant()}|{run.InstanceId}|{run.Ended?.UtcTicks}";
            string name = @"Global\FluentTaskScheduler.CompletionActions." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)), 0, 16);

            Mutex mutex;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name, out bool createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    return false;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // It exists and belongs to another user's copy of the app.
                return false;
            }

            // Disposal comes from another thread than the claims.
            lock (_held)
            {
                _held.Enqueue(mutex);
                if (_held.Count > Kept) _held.Dequeue().Dispose();
            }
            return true;
        }

        public void Dispose()
        {
            lock (_held)
            {
                while (_held.TryDequeue(out var mutex)) mutex.Dispose();
            }
        }
    }
}
