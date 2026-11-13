using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>Runs posted work at once, on the calling thread.</summary>
public sealed class InlineUiThread : IUiThread
{
    public void Post(Action action) => action();
}

/// <summary>Records every notification as one line of text, e.g. "TaskStarted Backup".</summary>
public sealed class RecordingNotifier : INotifier
{
    public List<string> Shown { get; } = [];

    public void TaskStarted(string taskName) => Shown.Add($"TaskStarted {taskName}");
    public void TaskCouldNotStart(string taskName, string reason) => Shown.Add($"TaskCouldNotStart {taskName}: {reason}");
    public void RunSuppressed(string taskName) => Shown.Add($"RunSuppressed {taskName}");
    public void SnoozeStarted(string status) => Shown.Add($"SnoozeStarted {status}");
    public void SnoozeEnded() => Shown.Add("SnoozeEnded");
    public void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess) =>
        Shown.Add($"CompletionActionStarted {finishedTask} -> {startedTask} ({(afterSuccess ? "success" : "failure")})");
    public void TaskRunsSoon(string taskName, int minutes) => Shown.Add($"TaskRunsSoon {taskName} in {minutes}");
    public void HiddenToTray() => Shown.Add("HiddenToTray");
}

/// <summary>Answers prompts from a script and records what was asked.</summary>
public sealed class ScriptedPrompts : IPrompts
{
    public List<string> Shown { get; } = [];

    /// <summary>What <see cref="ConfirmAsync"/> answers.</summary>
    public bool Confirm { get; set; } = true;

    public Task ShowMessageAsync(string title, string message)
    {
        Shown.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string message)
    {
        Shown.Add($"Error: {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        Shown.Add($"{title}: {message} [{confirmText}]");
        return Task.FromResult(Confirm);
    }
}
