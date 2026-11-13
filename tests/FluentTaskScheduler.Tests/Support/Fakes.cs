using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
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

    /// <summary>What <see cref="AskForTextAsync"/> answers; null stands for cancelling.</summary>
    public string? Text { get; set; }

    public Task<string?> AskForTextAsync(string title, string label, string initialText, string confirmText)
    {
        Shown.Add($"{title}: {label} [{initialText}]");
        return Task.FromResult(Text);
    }
}

/// <summary>Hands out the file it was told to; null means the user cancels.</summary>
public sealed class ScriptedFilePicker : IFilePicker
{
    public string? Choice { get; set; }

    public Task<string?> PickOpenFileAsync(string title, string typeName, string extension) => Task.FromResult(Choice);
    public Task<string?> PickSaveFileAsync(string title, string typeName, string extension, string suggestedFileName) => Task.FromResult(Choice);
}

public sealed class FakeAppLifetime : IAppLifetime
{
    public bool IsElevated { get; set; }
    public bool ExitRequested { get; private set; }
    public bool RestartElevatedRequested { get; private set; }

    /// <summary>What <see cref="RestartElevated"/> answers: false stands for a declined consent prompt.</summary>
    public bool UserConsents { get; set; } = true;

    public void Exit() => ExitRequested = true;

    public bool RestartElevated()
    {
        RestartElevatedRequested = true;
        return UserConsents;
    }
}

public sealed class FakeNetworkProfiles(params NetworkProfile[] profiles) : INetworkProfiles
{
    public IReadOnlyList<NetworkProfile> GetAll() => profiles;
}

/// <summary>A snooze that is on or off as the test says, and remembers the runs it refused.</summary>
public sealed class FakeSnoozeGate : ISnoozeGate
{
    public bool Snoozed { get; set; }
    public List<string> Suppressed { get; } = [];

    public bool BlocksRun(string taskPath) => Snoozed;
    public void RecordSuppressedRun(string taskPath, RunOrigin origin) => Suppressed.Add($"{taskPath} ({origin})");
}

/// <summary>Folders in memory; moving and deleting only change the list of folders.</summary>
public sealed class FakeTaskFolders(params string[] folders) : ITaskFolders
{
    private readonly List<string> _folders = ["\\", .. folders];

    /// <summary>Every change, in order, e.g. "MoveTask \\A\\T -> \\B".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>When set, every change fails with this.</summary>
    public Exception? Fails { get; set; }

    public IReadOnlyList<string> GetFolders() => _folders.ToList();

    public void Create(string path)
    {
        Record($"Create {path}");
        if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase)) _folders.Add(path);
    }

    public void Delete(string path)
    {
        Record($"Delete {path}");
        _folders.RemoveAll(folder => TaskPaths.IsWithin(folder, path));
    }

    public string MoveTask(string taskPath, string folderPath)
    {
        Record($"MoveTask {taskPath} -> {folderPath}");
        return TaskPaths.Combine(folderPath, TaskPaths.NameOf(taskPath));
    }

    public void MoveFolder(string path, string newPath)
    {
        Record($"MoveFolder {path} -> {newPath}");
        for (int i = 0; i < _folders.Count; i++)
            if (TaskPaths.IsWithin(_folders[i], path)) _folders[i] = newPath + _folders[i][path.Length..];
    }

    private void Record(string call)
    {
        if (Fails is { } failure) throw failure;
        Calls.Add(call);
    }
}

/// <summary>Records where a view model sent the window, and answers a request for a new task from a script.</summary>
public sealed class RecordingNavigator : IShellNavigator
{
    /// <summary>What was asked for, in order, e.g. "ShowScriptEditor" or "ShowTask \Backup".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The drafts the task editor was opened with.</summary>
    public List<TaskDraft> Drafts { get; } = [];

    /// <summary>The path <see cref="CreateTaskAsync"/> reports; null as if the user cancelled the editor.</summary>
    public string? CreatedTaskPath { get; set; }

    public void ShowTask(string taskPath) => Calls.Add($"ShowTask {taskPath}");

    public Task<string?> CreateTaskAsync(TaskDraft template)
    {
        Calls.Add($"CreateTask {template.Name}");
        Drafts.Add(template);
        return Task.FromResult(CreatedTaskPath);
    }

    public void ShowScriptEditor() => Calls.Add("ShowScriptEditor");
}

/// <summary>A UI thread that is busy until the test lets it run what was posted to it.</summary>
public sealed class BusyUiThread : IUiThread
{
    private readonly Queue<Action> _posted = new();

    public int PostedCount => _posted.Count;

    public void Post(Action action) => _posted.Enqueue(action);

    public void RunPosted()
    {
        while (_posted.TryDequeue(out Action? action)) action();
    }
}
