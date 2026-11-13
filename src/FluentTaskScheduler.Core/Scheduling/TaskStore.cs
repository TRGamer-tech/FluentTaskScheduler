using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Scheduling;

/// <inheritdoc cref="ITaskStore"/>
public sealed class TaskStore : ITaskStore
{
    // Every operation opens its own short-lived session with the Task Scheduler service.
    private static TaskService Open() => new();

    public IReadOnlyList<TaskRow> GetTasks()
    {
        using var service = Open();
        var rows = new List<TaskRow>();
        Collect(service.RootFolder, rows);
        return rows;
    }

    /// <summary>
    /// Walks the folder tree by hand instead of using <c>TaskFolder.AllTasks</c>: that enumeration
    /// stops at the first folder or task it cannot read, and everything after it would be missing.
    /// A folder that is deleted while the tree is walked is skipped the same way; Task Scheduler
    /// reports it as a file or path that does not exist.
    /// </summary>
    internal static void Collect(TaskFolder folder, List<TaskRow> rows)
    {
        try
        {
            foreach (var task in folder.Tasks) rows.Add(TaskRowReader.Read(task));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
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

    public TaskRow? GetTask(string path)
    {
        using var service = Open();
        return service.GetTask(path) is { } task ? TaskRowReader.Read(task) : null;
    }

    public bool Exists(string path)
    {
        using var service = Open();
        return service.GetTask(path) is not null;
    }

    public IReadOnlyDictionary<string, TaskRowState> GetStates(IEnumerable<string> paths)
    {
        using var service = Open();
        var states = new Dictionary<string, TaskRowState>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (service.GetTask(path) is { } task) states[path] = TaskRowReader.ToRowState(task.State);
        }
        return states;
    }

    public int CountRunningTasks()
    {
        using var service = Open();
        return service.GetRunningTasks(includeHidden: true).Select(instance => instance.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    public TaskDraft Load(string path)
    {
        using var service = Open();
        return TaskDraftReader.Read(Find(service, path));
    }

    public CompletionActions GetCompletionActions(string path)
    {
        using var service = Open();
        if (service.GetTask(path) is not { } task) return CompletionActions.None;

        // Only the description is wanted, so the task's XML is read as text. Loading the
        // definition would fail for tasks this user may not read or this library cannot interpret.
        try
        {
            var description = System.Xml.Linq.XElement.Parse(task.Xml)
                .Elements().FirstOrDefault(element => element.Name.LocalName == "RegistrationInfo")
                ?.Elements().FirstOrDefault(element => element.Name.LocalName == "Description");
            return TaskMetadata.Split(description?.Value).CompletionActions;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or System.Xml.XmlException)
        {
            // Not logged: this is asked for every run that ends on the computer, and a task whose
            // description cannot be read, or that was deleted just now, is simply one without
            // completion actions.
            return CompletionActions.None;
        }
    }

    public string Save(TaskDraft draft, string folderPath, string? originalPath = null, string? password = null)
    {
        using var service = Open();

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
            (userId, logonType) = TaskRegistration.Keeping(definition.Principal);
        }

        TaskFolder folder = original?.Folder ?? TaskPaths.GetOrCreateFolder(service, folderPath);
        ScheduledTask Register(string name, TaskCreation creation) =>
            TaskRegistration.Register(folder, name, definition, userId, password, logonType, creation);

        // Only a task saved under its own name replaces what is registered there. A new task, or
        // one given a new name, must not take the place of a task that already has that name.
        if (original is null) return Register(draft.Name, TaskCreation.Create).Path;
        if (original.Name == draft.Name) return Register(draft.Name, TaskCreation.CreateOrUpdate).Path;

        // Task Scheduler takes a name that differs only in case for the task that has it, so such
        // a rename goes by way of a name of its own.
        string oldName = original.Name;
        if (string.Equals(oldName, draft.Name, StringComparison.OrdinalIgnoreCase))
        {
            string interim = $"{draft.Name} ({Guid.NewGuid().ToString("N")[..8]})";
            Register(interim, TaskCreation.Create);
            TaskRegistration.RemoveOriginal(folder, oldName, folder, interim);
            oldName = interim;
        }

        ScheduledTask renamed = Register(draft.Name, TaskCreation.Create);
        TaskRegistration.RemoveOriginal(folder, oldName, folder, draft.Name);
        return renamed.Path;
    }

    public void SetEnabled(string path, bool enabled)
    {
        using var service = Open();
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

    public void Run(string path, bool force = true)
    {
        using var service = Open();
        var task = Find(service, path);
        try
        {
            // Ignoring constraints does more than the name says: besides the conditions it skips
            // the task's rule for multiple instances, so a second instance starts next to one
            // that is already running. A plain run leaves both to Task Scheduler.
            if (force) task.RunEx(TaskRunFlags.IgnoreConstraints, 0, null);
            else task.Run();
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public void Stop(string path)
    {
        using var service = Open();
        Find(service, path).Stop();
    }

    public void Delete(string path)
    {
        using var service = Open();
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
        using var service = Open();
        return Find(service, path).Xml;
    }

    public TaskImportInfo Inspect(string xml)
    {
        using var service = Open();
        var definition = Parse(service, xml);
        var principal = definition.Principal;
        string id = principal.LogonType == TaskLogonType.Group ? principal.GroupId ?? "" : principal.UserId ?? "";

        // Read the way the editor reads a task, so the two agree on the account and on whether
        // Windows keeps a password for it. (The library's RequiresPassword also says yes for "do
        // not store password", S4U, whenever the account is not the current user's own name.)
        TaskDraft task = TaskDraftReader.Read(definition);
        return new TaskImportInfo(
            task.Account,
            AccountExistsHere: id.Length == 0 || TaskAccounts.Exists(id),
            task.HighestPrivileges,
            task.StoresPassword)
        {
            Triggers = task.Triggers,
            Actions = task.Actions
        };
    }

    public string Import(string xml, TaskImportOptions options)
    {
        using var service = Open();
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
            (userId, logonType) = TaskRegistration.Keeping(principal);
        }

        var folder = TaskPaths.GetOrCreateFolder(service, options.FolderPath);
        string? password = TaskRegistration.StoresPassword(logonType) ? options.Password : null;
        return TaskRegistration.Register(folder, options.Name, definition, userId, password, logonType, TaskCreation.Create).Path;
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
}
