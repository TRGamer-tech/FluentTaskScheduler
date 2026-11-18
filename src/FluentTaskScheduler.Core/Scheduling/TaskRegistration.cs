using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Registering a task definition in a folder, shared by everything that writes tasks.</summary>
internal static class TaskRegistration
{
    // HRESULTs Task Scheduler answers with when the account of a task cannot be used.
    private const int LogonFailure = unchecked((int)0x8007052E);        // ERROR_LOGON_FAILURE: wrong or missing password
    private const int NoneMapped = unchecked((int)0x80070534);          // ERROR_NONE_MAPPED: the account does not exist
    private const int AccountRestriction = unchecked((int)0x8007052F);  // ERROR_ACCOUNT_RESTRICTION: e.g. blank password
    private const int AlreadyExists = unchecked((int)0x800700B7);       // ERROR_ALREADY_EXISTS: TaskCreation.Create found a task of that name

    /// <summary>
    /// The user ID and logon type that register a definition under the principal it already has,
    /// including logon types the editor has no notion of.
    /// </summary>
    public static (string? UserId, TaskLogonType LogonType) Keeping(TaskPrincipal principal) => principal.LogonType switch
    {
        TaskLogonType.Group => (principal.GroupId, TaskLogonType.Group),
        TaskLogonType.ServiceAccount => (TaskAccounts.ToRegistration(TaskAccounts.FromPrincipal(principal)).UserId, TaskLogonType.ServiceAccount),
        var logonType => (principal.UserId, logonType)
    };

    /// <summary>
    /// True for the logon types Windows keeps the account's password for, which it wants again
    /// whenever the task is registered. A task that runs whether or not its user is logged on
    /// without storing a password (S4U) is not one of them.
    /// </summary>
    public static bool StoresPassword(TaskLogonType logonType) => logonType is TaskLogonType.Password or TaskLogonType.InteractiveTokenOrPassword;

    /// <param name="creation">
    /// <see cref="TaskCreation.Create"/> for a task that must not take the place of another one,
    /// <see cref="TaskCreation.CreateOrUpdate"/> only for saving a task under its own name.
    /// </param>
    /// <returns>The task as Task Scheduler registered it.</returns>
    /// <exception cref="TaskExistsException">The folder already has a task of that name, and <paramref name="creation"/> does not allow replacing it.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused the registration.</exception>
    /// <exception cref="TaskAccountException">The account or its password was not accepted.</exception>
    public static ScheduledTask Register(TaskFolder folder, string name, TaskDefinition definition, string? userId, string? password, TaskLogonType logonType, TaskCreation creation)
    {
        try
        {
            return folder.RegisterTaskDefinition(name, definition, creation, userId, password, logonType);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(TaskPaths.Combine(folder.Path, name), ex);
        }
        catch (COMException ex) when (ex.HResult is LogonFailure or NoneMapped or AccountRestriction)
        {
            throw new TaskAccountException(ex.Message, ex);
        }
        catch (COMException ex) when (ex.HResult == AlreadyExists)
        {
            throw new TaskExistsException(TaskPaths.Combine(folder.Path, name), ex);
        }
    }

    /// <summary>
    /// The second half of a rename or a move: Task Scheduler can do neither, so the task has been
    /// registered again as <paramref name="copyName"/> in <paramref name="copyFolder"/>, and the
    /// original goes now. If it cannot be removed, the copy is taken back instead, so that the task
    /// stays where it was rather than running twice. An original that is gone already is fine.
    /// </summary>
    /// <exception cref="TaskAccessDeniedException">Windows refused to remove the original.</exception>
    /// <exception cref="TaskDuplicatedException">Neither the original nor the copy could be removed.</exception>
    public static void RemoveOriginal(TaskFolder folder, string name, TaskFolder copyFolder, string copyName)
    {
        try
        {
            folder.DeleteTask(name, exceptionOnNotExists: false);
        }
        catch (Exception ex)
        {
            string path = TaskPaths.Combine(folder.Path, name);
            try
            {
                copyFolder.DeleteTask(copyName, exceptionOnNotExists: false);
            }
            catch (Exception rollback)
            {
                string copyPath = TaskPaths.Combine(copyFolder.Path, copyName);
                Serilog.Log.Error(rollback, "Task {Path} could not be removed, and neither could its copy {Copy}", path, copyPath);
                throw new TaskDuplicatedException(path, copyPath, ex);
            }
            if (TaskSchedulerErrors.IsAccessDenied(ex)) throw new TaskAccessDeniedException(path, ex);
            throw;
        }
    }
}

/// <summary>Paths of tasks and folders in Task Scheduler: backslash-separated, "\" for the root.</summary>
public static class TaskPaths
{
    public const string Root = "\\";

    public static string Combine(string folderPath, string name) => folderPath.TrimEnd('\\') + "\\" + name;

    /// <summary>The folder a task or folder is in; the root for anything directly below it.</summary>
    public static string ParentOf(string path)
    {
        int separator = path.TrimEnd('\\').LastIndexOf('\\');
        return separator <= 0 ? Root : path[..separator];
    }

    public static string NameOf(string path) => path.TrimEnd('\\') is var trimmed ? trimmed[(trimmed.LastIndexOf('\\') + 1)..] : path;

    /// <summary>True when <paramref name="path"/> is <paramref name="folderPath"/> or lies somewhere below it.</summary>
    public static bool IsWithin(string path, string folderPath) =>
        string.Equals(path, folderPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folderPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder at <paramref name="path"/>, created along with any missing parents.</summary>
    internal static TaskFolder GetOrCreateFolder(TaskService service, string path)
    {
        TaskFolder folder = service.RootFolder;
        foreach (string name in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            folder = folder.SubFolders.FirstOrDefault(sub => string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                     ?? folder.CreateFolder(name);
        }
        return folder;
    }
}
