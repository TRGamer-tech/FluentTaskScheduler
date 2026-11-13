using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Scheduling;

/// <inheritdoc cref="ITaskStore"/>
public sealed class TaskStore : ITaskStore
{
    // HRESULTs Task Scheduler answers with when the account of a task cannot be used.
    private const int LogonFailure = unchecked((int)0x8007052E);        // ERROR_LOGON_FAILURE: wrong or missing password
    private const int NoneMapped = unchecked((int)0x80070534);          // ERROR_NONE_MAPPED: the account does not exist
    private const int AccountRestriction = unchecked((int)0x8007052F);  // ERROR_ACCOUNT_RESTRICTION: e.g. blank password

    private readonly TaskSchedulerConnection _connection;

    public TaskStore(TaskSchedulerConnection connection) => _connection = connection;

    public IReadOnlyList<TaskRow> GetTasks()
    {
        using var service = _connection.Open();
        var rows = new List<TaskRow>();
        Collect(service.RootFolder, rows);
        return rows;
    }

    /// <summary>
    /// Walks the folder tree by hand instead of using <c>TaskFolder.AllTasks</c>: that enumeration
    /// stops at the first folder or task it cannot read, and everything after it would be missing.
    /// </summary>
    private static void Collect(TaskFolder folder, List<TaskRow> rows)
    {
        try
        {
            foreach (var task in folder.Tasks) rows.Add(TaskRowReader.Read(task));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The tasks in folder {Folder} could not be listed", folder.Path);
        }

        TaskFolder[] subFolders;
        try
        {
            subFolders = folder.SubFolders.ToArray();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The sub-folders of {Folder} could not be listed", folder.Path);
            return;
        }

        foreach (var subFolder in subFolders) Collect(subFolder, rows);
    }

    public bool Exists(string path)
    {
        using var service = _connection.Open();
        return service.GetTask(path) is not null;
    }

    public IReadOnlyDictionary<string, TaskRowState> GetStates(IEnumerable<string> paths)
    {
        using var service = _connection.Open();
        var states = new Dictionary<string, TaskRowState>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (service.GetTask(path) is { } task) states[path] = TaskRowReader.ToRowState(task.State);
        }
        return states;
    }

    public TaskDraft Load(string path)
    {
        using var service = _connection.Open();
        return TaskDraftReader.Read(Find(service, path));
    }

    public string Save(TaskDraft draft, string folderPath, string? originalPath = null, string? password = null)
    {
        using var service = _connection.Open();

        ScheduledTask? original = originalPath is null ? null : Find(service, originalPath);
        TaskDraft? baseline = original is null ? null : TaskDraftReader.Read(original);
        TaskDefinition definition = original?.Definition ?? service.NewTask();
        TaskDraftWriter.Apply(draft, baseline, definition);

        // The account is only rewritten when it was chosen in the editor. An untouched task keeps
        // its principal exactly, including logon types the editor has no notion of.
        string? userId;
        TaskLogonType logonType;
        if (baseline is null || draft.Account != baseline.Account)
        {
            (userId, logonType) = TaskAccounts.ToRegistration(draft.Account);
            password = null;
        }
        else
        {
            (userId, logonType) = Registration(definition.Principal, baseline.Account);
        }

        TaskFolder folder = original?.Folder ?? TaskSchedulerConnection.GetOrCreateFolder(service, folderPath);
        string savedPath = Combine(folder.Path, draft.Name);
        Register(folder, draft.Name, definition, userId, password, logonType, savedPath);

        bool renamed = original is not null && !string.Equals(original.Name, draft.Name, StringComparison.OrdinalIgnoreCase);
        if (renamed) DeleteAfterRename(folder, original!.Name, draft.Name);

        return savedPath;
    }

    /// <summary>The user ID and logon type that register a definition under the principal it already has.</summary>
    private static (string? UserId, TaskLogonType LogonType) Registration(TaskPrincipal principal, TaskAccount account) => principal.LogonType switch
    {
        TaskLogonType.Group => (principal.GroupId, TaskLogonType.Group),
        TaskLogonType.ServiceAccount => (TaskAccounts.ToRegistration(account).UserId, TaskLogonType.ServiceAccount),
        var logonType => (principal.UserId, logonType)
    };

    private static void Register(TaskFolder folder, string name, TaskDefinition definition, string? userId, string? password, TaskLogonType logonType, string path)
    {
        try
        {
            folder.RegisterTaskDefinition(name, definition, TaskCreation.CreateOrUpdate, userId, password, logonType);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
        catch (COMException ex) when (ex.HResult is LogonFailure or NoneMapped or AccountRestriction)
        {
            throw new TaskAccountException(ex.Message, ex);
        }
    }

    /// <summary>
    /// A rename registers the task under the new name first and removes the old one afterwards.
    /// If the old one cannot be removed, the new copy is taken back so the task does not end up twice.
    /// </summary>
    private static void DeleteAfterRename(TaskFolder folder, string oldName, string newName)
    {
        try
        {
            folder.DeleteTask(oldName);
        }
        catch (Exception ex)
        {
            try
            {
                folder.DeleteTask(newName);
            }
            catch (Exception rollback)
            {
                throw new AggregateException(
                    $"The task was saved as '{newName}', but the old task '{oldName}' could not be removed, and neither could the new copy. Both exist now.",
                    ex, rollback);
            }
            throw new InvalidOperationException($"The task could not be renamed because '{oldName}' could not be removed: {ex.Message}", ex);
        }
    }

    public void SetEnabled(string path, bool enabled)
    {
        using var service = _connection.Open();
        var task = Find(service, path);
        try
        {
            task.Enabled = enabled;
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public void Run(string path)
    {
        using var service = _connection.Open();
        var task = Find(service, path);
        try
        {
            // A run started by hand happens now, whatever the task's conditions (idle, power, network) say.
            task.RunEx(TaskRunFlags.IgnoreConstraints, 0, null);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public void Stop(string path)
    {
        using var service = _connection.Open();
        Find(service, path).Stop();
    }

    public void Delete(string path)
    {
        using var service = _connection.Open();
        var task = Find(service, path);
        try
        {
            task.Folder.DeleteTask(task.Name);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public string Export(string path)
    {
        using var service = _connection.Open();
        return Find(service, path).Xml;
    }

    public TaskImportInfo Inspect(string xml)
    {
        using var service = _connection.Open();
        var principal = Parse(service, xml).Principal;
        string id = principal.LogonType == TaskLogonType.Group ? principal.GroupId ?? "" : principal.UserId ?? "";

        return new TaskImportInfo(
            TaskAccounts.FromPrincipal(principal),
            AccountExistsHere: id.Length == 0 || TaskAccounts.Exists(id),
            HighestPrivileges: principal.RunLevel == TaskRunLevel.Highest,
            StoresPassword: principal.RequiresPassword());
    }

    public string Import(string xml, TaskImportOptions options)
    {
        using var service = _connection.Open();
        var definition = Parse(service, xml);
        var principal = definition.Principal;

        if (options.HighestPrivileges is bool highest)
            principal.RunLevel = highest ? TaskRunLevel.Highest : TaskRunLevel.LUA;

        string? userId;
        TaskLogonType logonType;
        if (options.Account is { } account)
        {
            string? previousUser = principal.UserId;
            (userId, logonType) = TaskAccounts.ToRegistration(account);
            Retarget(definition, previousUser, TaskAccounts.TriggerUser(account));
        }
        else
        {
            (userId, logonType) = Registration(principal, TaskAccounts.FromPrincipal(principal));
        }

        var folder = TaskSchedulerConnection.GetOrCreateFolder(service, options.FolderPath);
        string path = Combine(folder.Path, options.Name);
        Register(folder, options.Name, definition, userId, logonType is TaskLogonType.Password or TaskLogonType.InteractiveTokenOrPassword ? options.Password : null, logonType, path);
        return path;
    }

    /// <summary>
    /// Logon and session triggers can name the user they watch. In a task brought over from
    /// another account that is usually the account the task ran as, or one unknown here; both
    /// would stop the task from being registered or from ever firing.
    /// </summary>
    private static void Retarget(TaskDefinition definition, string? previousUser, string? newUser)
    {
        bool ShouldReplace(string? triggerUser) =>
            !string.IsNullOrEmpty(triggerUser)
            && (string.Equals(triggerUser, previousUser, StringComparison.OrdinalIgnoreCase) || !TaskAccounts.Exists(triggerUser));

        foreach (var trigger in definition.Triggers)
        {
            switch (trigger)
            {
                case LogonTrigger logon when ShouldReplace(logon.UserId): logon.UserId = newUser; break;
                case SessionStateChangeTrigger session when ShouldReplace(session.UserId): session.UserId = newUser; break;
            }
        }
    }

    private static TaskDefinition Parse(TaskService service, string xml)
    {
        var definition = service.NewTask();
        try
        {
            definition.XmlText = xml;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException or COMException)
        {
            throw new FormatException("This is not a task exported from Task Scheduler: " + ex.Message, ex);
        }
        return definition;
    }

    private static ScheduledTask Find(TaskService service, string path) =>
        service.GetTask(path) ?? throw new TaskNotFoundException(path);

    private static string Combine(string folderPath, string name) =>
        folderPath.TrimEnd('\\') + "\\" + name;
}
