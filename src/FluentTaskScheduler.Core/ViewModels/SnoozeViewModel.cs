using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The snooze of all tasks as one window shows it: whether it is on and until when, what it does
/// not stop, starting and ending it, and what earlier snoozes left behind.
/// </summary>
public sealed partial class SnoozeViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// A snooze that ran with administrator rights can leave hundreds of tasks behind that cannot
    /// be enabled again without them; only the first few are named.
    /// </summary>
    internal const int NamedRestores = 5;

    private readonly ISnoozeService _snooze;
    private readonly ISettingsStore _settings;
    private readonly IPrompts _prompts;
    private readonly IUiThread _ui;
    private readonly TimeProvider _time;

    /// <summary>The state on show. It is replaced on the UI thread only, so everything shown is of the same state.</summary>
    private SnoozeState _state;
    private bool _disposed;

    public SnoozeViewModel(ISnoozeService snooze, ISettingsStore settings, IPrompts prompts, IUiThread ui, TimeProvider time)
    {
        _snooze = snooze;
        _settings = settings;
        _prompts = prompts;
        _ui = ui;
        _time = time;
        _state = snooze.State;
        _snooze.Changed += OnSnoozeChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public bool IsSnoozed => _state.Global is not null;

    /// <summary>Until when all tasks are snoozed, e.g. "Global Snooze Active (Ends at 14:30)"; empty while they are not.</summary>
    public string StatusText => _state.Global is { } global ? SnoozeText.AllTasks(global.Window, _time) : "";

    /// <summary>What the snooze stops: everything while it suspends the tasks' triggers, otherwise only the runs this app would start.</summary>
    public string EffectText => Loc.Get(_state.Global is { SuspendsTriggers: true } ? "Snooze.Banner.Suspended" : "Snooze.Banner.ManualOnly");

    /// <summary>The caption of the button that snoozes all tasks.</summary>
    public string Caption => Loc.Get(IsSnoozed ? "Snooze.Toolbar.Active" : "Snooze.Toolbar.Idle");

    /// <summary>True while tasks are being disabled or enabled again. A task list read meanwhile may be out of date.</summary>
    public bool IsBusy => _state.IsBusy;

    public bool HasPendingRestores => _state.PendingRestores.Count > 0;

    /// <summary>The tasks a snooze left disabled because they could not be enabled again, and why, if Windows refused.</summary>
    public string PendingRestoresText
    {
        get
        {
            ValueList<PendingRestore> restores = _state.PendingRestores;
            string named = string.Join(", ", restores.Take(NamedRestores).Select(restore => restore.TaskPath));
            if (restores.Count > NamedRestores) named = Loc.Format("Snooze.Restore.MoreFormat", named, restores.Count - NamedRestores);

            string text = Loc.Format("Snooze.Restore.MessageFormat", named);
            return restores.Any(restore => restore.Reason == RestoreFailure.AccessDenied)
                ? text + Environment.NewLine + Loc.Get("Snooze.Restore.AccessDenied")
                : text;
        }
    }

    public bool HasUnreadableStateFile => _state.UnreadableStateFile is not null;

    public string UnreadableStateText => _state.UnreadableStateFile is { } file ? Loc.Format("Snooze.Unreadable.MessageFormat", file) : "";

    /// <summary>
    /// Snoozing all tasks also disables them in Task Scheduler, so that Windows does not start them
    /// either. Switching this off also switches off <see cref="IncludesMicrosoftTasks"/>: that one
    /// is only ever on because it was chosen while its warning was in view.
    /// </summary>
    public bool SuspendsTriggers
    {
        get => _settings.Current.SnoozeSuspendsScheduledTasks;
        set => _settings.Update(s => s with
        {
            SnoozeSuspendsScheduledTasks = value,
            SnoozeIncludeMicrosoftTasks = value && s.SnoozeIncludeMicrosoftTasks
        });
    }

    /// <summary>Suspending triggers reaches the tasks Windows has under \Microsoft\ as well.</summary>
    public bool IncludesMicrosoftTasks
    {
        get => _settings.Current.SnoozeIncludeMicrosoftTasks;
        set => _settings.Update(s => s with { SnoozeIncludeMicrosoftTasks = value });
    }

    /// <summary>Disabling the tasks of Windows can keep it from scanning for malware and updating itself until the snooze ends.</summary>
    public bool ShowsMicrosoftTasksWarning => SuspendsTriggers && IncludesMicrosoftTasks;

    [RelayCommand]
    private async Task SnoozeAsync(SnoozeChoice choice)
    {
        if (await TrySnoozeAsync(choice.End) is { } problem) await _prompts.ShowErrorAsync(problem);
    }

    [RelayCommand]
    private Task ResumeAsync() => _snooze.ResumeAllAsync();

    [RelayCommand]
    private Task RetryRestoresAsync() => _snooze.RetryRestoresAsync();

    /// <summary>For the dialog that snoozes all tasks until a moment of the user's choosing.</summary>
    public SnoozeUntilViewModel PickMoment() =>
        new(Loc.Get("Snooze.Dialog.Title"), Loc.Get("Snooze.Dialog.Until"), this, _time, TrySnoozeAsync);

    public void Dispose()
    {
        _disposed = true;
        _snooze.Changed -= OnSnoozeChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    /// <returns>Why the snooze did not start; null when it did.</returns>
    private async Task<string?> TrySnoozeAsync(SnoozeEnd end)
    {
        try
        {
            await _snooze.SnoozeAllAsync(end);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Format("Snooze.Error.NotRecorded", ex.Message);
        }
    }

    // The service reports on whatever thread made the change. What was posted before the window
    // closed can still arrive after it.
    private void OnSnoozeChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        if (_disposed) return;
        _state = _snooze.State;

        // No name: all properties have changed.
        OnPropertyChanged(string.Empty);
    });

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        if (change.Previous.SnoozeSuspendsScheduledTasks == change.Current.SnoozeSuspendsScheduledTasks
            && change.Previous.SnoozeIncludeMicrosoftTasks == change.Current.SnoozeIncludeMicrosoftTasks) return;

        _ui.Post(() =>
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(SuspendsTriggers));
            OnPropertyChanged(nameof(IncludesMicrosoftTasks));
            OnPropertyChanged(nameof(ShowsMicrosoftTasksWarning));
        });
    }
}
