using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>
/// Tasks in memory, behaving like <see cref="TaskStore"/> as far as callers can tell: paths
/// compare without regard to case, a missing task throws <see cref="TaskNotFoundException"/>,
/// and a path listed in <see cref="Protected"/> throws <see cref="TaskAccessDeniedException"/>
/// for anything that would change or start it.
/// </summary>
public sealed class FakeTaskStore : ITaskStore
{
    private readonly Dictionary<string, TaskDraft> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tasks Windows would refuse to change or start.</summary>
    public HashSet<string> Protected { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call that changed or started something, in order, e.g. "Run \Backup" or "SetEnabled \Backup False".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The password the last <see cref="Save"/> was given.</summary>
    public string? LastPassword { get; private set; }

    /// <summary>Called before each operation with its name and task path; throw from it to make the operation fail.</summary>
    public Action<string, string>? Before { get; set; }

    public FakeTaskStore Add(string path, bool enabled = true, TaskDraft? draft = null)
    {
        _tasks[path] = (draft ?? new TaskDraft()) with { Name = NameOf(path), Enabled = enabled };
        return this;
    }

    public bool IsEnabled(string path) => Get(path).Enabled;
    public bool IsRunning(string path) => _running.Contains(path);
    public IEnumerable<string> Paths => _tasks.Keys;

    public IReadOnlyList<TaskRow> GetTasks() => _tasks.Select(entry => new TaskRow(entry.Key)
    {
        IsEnabled = entry.Value.Enabled,
        State = StateOf(entry.Key),
        IsHidden = entry.Value.Hidden,
        Description = entry.Value.Description,
        Author = entry.Value.Author,
        Category = entry.Value.Category,
        Tags = entry.Value.Tags,
        CompletionActions = entry.Value.CompletionActions
    }).ToList();

    public bool Exists(string path) => _tasks.ContainsKey(path);

    public IReadOnlyDictionary<string, TaskRowState> GetStates(IEnumerable<string> paths) =>
        paths.Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase)
             .ToDictionary(path => path, StateOf, StringComparer.OrdinalIgnoreCase);

    public TaskDraft Load(string path) => Get(path);

    public string Save(TaskDraft draft, string folderPath, string? originalPath = null, string? password = null)
    {
        string path = folderPath.TrimEnd('\\') + "\\" + draft.Name;
        Change("Save", originalPath ?? path, mustExist: originalPath is not null);
        LastPassword = password;
        if (originalPath is not null && !string.Equals(originalPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _tasks.Remove(originalPath);
            _running.Remove(originalPath);
        }
        _tasks[path] = draft;
        return path;
    }

    public void SetEnabled(string path, bool enabled)
    {
        Change($"SetEnabled", path, detail: enabled.ToString());
        _tasks[path] = _tasks[path] with { Enabled = enabled };
    }

    public void Run(string path)
    {
        Change("Run", path);
        _running.Add(path);
    }

    public void Stop(string path)
    {
        Change("Stop", path);
        _running.Remove(path);
    }

    public void Delete(string path)
    {
        Change("Delete", path);
        _tasks.Remove(path);
        _running.Remove(path);
    }

    public string Export(string path) => $"<Task name=\"{Get(path).Name}\" />";

    public TaskImportInfo Inspect(string xml) => new(TaskAccount.CurrentUser, AccountExistsHere: true, HighestPrivileges: false, StoresPassword: false);

    public string Import(string xml, TaskImportOptions options) =>
        Save(new TaskDraft { Name = options.Name }, options.FolderPath);

    /// <summary>Marks a running task as finished, as Task Scheduler would.</summary>
    public void Finish(string path) => _running.Remove(path);

    private TaskRowState StateOf(string path) =>
        _running.Contains(path) ? TaskRowState.Running : _tasks[path].Enabled ? TaskRowState.Ready : TaskRowState.Disabled;

    private TaskDraft Get(string path) =>
        _tasks.TryGetValue(path, out var draft) ? draft : throw new TaskNotFoundException(path);

    private void Change(string operation, string path, bool mustExist = true, string? detail = null)
    {
        Before?.Invoke(operation, path);
        if (mustExist && !_tasks.ContainsKey(path)) throw new TaskNotFoundException(path);
        if (Protected.Contains(path)) throw new TaskAccessDeniedException(path, new UnauthorizedAccessException());
        Calls.Add(detail is null ? $"{operation} {path}" : $"{operation} {path} {detail}");
    }

    private static string NameOf(string path) => path[(path.LastIndexOf('\\') + 1)..];
}
