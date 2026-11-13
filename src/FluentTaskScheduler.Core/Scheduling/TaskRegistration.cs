using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Registering a task definition in a folder, shared by everything that writes tasks.</summary>
internal static class TaskRegistration
{
    // HRESULTs Task Scheduler answers with when the account of a task cannot be used.
    private const int LogonFailure = unchecked((int)0x8007052E);        // ERROR_LOGON_FAILURE: wrong or missing password
    private const int NoneMapped = unchecked((int)0x80070534);          // ERROR_NONE_MAPPED: the account does not exist
    private const int AccountRestriction = unchecked((int)0x8007052F);  // ERROR_ACCOUNT_RESTRICTION: e.g. blank password
    private const int AlreadyExists = unchecked((int)0x800700B7);       // ERROR_ALREADY_EXISTS: the name is taken and may not be replaced

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

    /// <param name="creation"><see cref="TaskCreation.Create"/> to refuse a name that is taken instead of replacing that task.</param>
    /// <exception cref="TaskAccessDeniedException">Windows refused the registration.</exception>
    /// <exception cref="TaskAccountException">The account or its password was not accepted.</exception>
    /// <exception cref="TaskExistsException">The name is taken, and <paramref name="creation"/> does not allow replacing that task.</exception>
    public static void Register(TaskFolder folder, string name, TaskDefinition definition, string? userId, string? password, TaskLogonType logonType,
        TaskCreation creation = TaskCreation.CreateOrUpdate)
    {
        try
        {
            folder.RegisterTaskDefinition(name, definition, creation, userId, password, logonType);
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
            throw new TaskExistsException(TaskPaths.Combine(folder.Path, name));
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
