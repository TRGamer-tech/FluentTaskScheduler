using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>The folders tasks are organised in.</summary>
public interface ITaskFolders
{
    /// <summary>The paths of all folders the user can see, the root first and every folder before its sub-folders.</summary>
    IReadOnlyList<string> GetFolders();

    /// <summary>Creates a folder, and its parents if they are missing.</summary>
    void Create(string path);

    /// <summary>Deletes a folder together with every task and folder in it.</summary>
    void Delete(string path);

    /// <summary>Moves a task into another folder.</summary>
    /// <returns>The task's new path.</returns>
    /// <exception cref="TaskExistsException">The folder already has a task of that name.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused a part of the move; the task is where it was.</exception>
    /// <exception cref="TaskDuplicatedException">The task could be removed neither from its folder nor from the new one.</exception>
    string MoveTask(string taskPath, string folderPath);

    /// <summary>
    /// Moves a folder with everything in it to <paramref name="newPath"/>; giving it a new name in
    /// the same place is a rename. A task that cannot be removed from the folder stays there, and
    /// so does the folder; everything else moves, and the first such failure is thrown.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is already a folder at the new path, or the new path lies inside the folder.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused a part of the move.</exception>
    void MoveFolder(string path, string newPath);
}

/// <summary>
/// Task Scheduler cannot move or rename anything. A move registers the task again in the new
/// place, under the account it already runs as, and removes the original only after that worked.
/// </summary>
public sealed class TaskFolders : ITaskFolders
{
    public IReadOnlyList<string> GetFolders()
    {
        using var service = new TaskService();
        var paths = new List<string>();
        Collect(service.RootFolder, paths);
        return paths;
    }

    public void Create(string path)
    {
        using var service = new TaskService();
        try
        {
            TaskPaths.GetOrCreateFolder(service, path);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public void Delete(string path)
    {
        if (IsRoot(path)) throw new InvalidOperationException("The root folder cannot be deleted.");

        using var service = new TaskService();
        TaskFolder folder = Find(service, path);
        try
        {
            DeleteWithContent(folder);
        }
        catch (Exception ex) when (TaskSchedulerErrors.IsAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    public string MoveTask(string taskPath, string folderPath)
    {
        using var service = new TaskService();
        var task = service.GetTask(taskPath) ?? throw new TaskNotFoundException(taskPath);
        if (string.Equals(task.Folder.Path, folderPath, StringComparison.OrdinalIgnoreCase)) return task.Path;

        try
        {
            TaskFolder target = TaskPaths.GetOrCreateFolder(service, folderPath);
            Copy(task, target);
            TaskRegistration.RemoveOriginal(task.Folder, task.Name, target, task.Name);
            return TaskPaths.Combine(target.Path, task.Name);
        }
        catch (Exception ex) when (IsRawAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(taskPath, ex);
        }
    }

    public void MoveFolder(string path, string newPath)
    {
        if (IsRoot(path)) throw new InvalidOperationException("The root folder cannot be moved.");
        if (path == newPath) return;

        // Task Scheduler takes a name that differs only in case for the folder that has it, so
        // such a rename goes by way of a name of its own.
        if (string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase))
        {
            string interim = $"{newPath} ({Guid.NewGuid().ToString("N")[..8]})";
            MoveFolder(path, interim);
            MoveFolder(interim, newPath);
            return;
        }
        if (TaskPaths.IsWithin(newPath, path)) throw new InvalidOperationException("A folder cannot be moved into itself.");

        using var service = new TaskService();
        TaskFolder source = Find(service, path);
        if (Exists(service, newPath)) throw new InvalidOperationException($"There is already a folder '{newPath}'.");

        try
        {
            TaskFolder target = TaskPaths.GetOrCreateFolder(service, newPath);
            var copied = new List<(TaskFolder Folder, string Name, TaskFolder CopyFolder)>();
            try
            {
                CopyContent(source, target, copied);
            }
            catch
            {
                // Nothing was removed from the source yet. Take back the partial copy, so that the
                // failed move leaves no duplicates behind.
                try { DeleteWithContent(target); }
                catch (Exception cleanup) when (cleanup is COMException or UnauthorizedAccessException or IOException)
                {
                    Serilog.Log.Warning(cleanup, "The partial copy in {Folder} could not be removed", newPath);
                }
                throw;
            }

            // Every original goes now that its copy is in place. One that cannot be removed keeps
            // its place and loses its copy instead, and the others still move.
            Exception? failure = null;
            foreach (var (folder, name, copyFolder) in copied)
            {
                try
                {
                    TaskRegistration.RemoveOriginal(folder, name, copyFolder, name);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Task {Name} could not be moved and stays in {Folder}", name, folder.Path);
                    failure ??= ex;
                }
            }
            RemoveIfEmpty(source);
            if (failure is not null) ExceptionDispatchInfo.Throw(failure);
        }
        catch (Exception ex) when (IsRawAccessDenied(ex))
        {
            throw new TaskAccessDeniedException(path, ex);
        }
    }

    private static void Collect(TaskFolder folder, List<string> paths)
    {
        paths.Add(folder.Path);
        TaskFolder[] subFolders;
        try
        {
            subFolders = folder.SubFolders.OrderBy(sub => sub.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The sub-folders of {Folder} could not be listed", folder.Path);
            return;
        }
        foreach (TaskFolder subFolder in subFolders) Collect(subFolder, paths);
    }

    /// <summary>Copies everything in <paramref name="source"/> to <paramref name="target"/> and notes each task copied, with the folder its copy is in.</summary>
    private static void CopyContent(TaskFolder source, TaskFolder target, List<(TaskFolder Folder, string Name, TaskFolder CopyFolder)> copied)
    {
        foreach (var task in source.Tasks.ToList())
        {
            Copy(task, target);
            copied.Add((source, task.Name, target));
        }
        foreach (TaskFolder sub in source.SubFolders.ToList()) CopyContent(sub, target.CreateFolder(sub.Name), copied);
    }

    /// <exception cref="TaskExistsException">The target folder already has a task of that name; it is left as it is.</exception>
    private static void Copy(Microsoft.Win32.TaskScheduler.Task task, TaskFolder target)
    {
        TaskDefinition definition = task.Definition;
        var (userId, logonType) = TaskRegistration.Keeping(definition.Principal);
        TaskRegistration.Register(target, task.Name, definition, userId, password: null, logonType, TaskCreation.Create);
    }

    private static void DeleteWithContent(TaskFolder folder)
    {
        foreach (var task in folder.Tasks.ToList()) folder.DeleteTask(task.Name);
        foreach (TaskFolder sub in folder.SubFolders.ToList()) DeleteWithContent(sub);
        folder.Parent.DeleteFolder(folder.Name);
    }

    /// <summary>Removes <paramref name="folder"/> and the folders in it that hold nothing any more; one that still holds a task stays.</summary>
    private static void RemoveIfEmpty(TaskFolder folder)
    {
        foreach (TaskFolder sub in folder.SubFolders.ToList()) RemoveIfEmpty(sub);
        if (folder.Tasks.Count == 0 && folder.SubFolders.Count == 0) folder.Parent.DeleteFolder(folder.Name);
    }

    /// <summary>Access denied as Windows raises it, not yet turned into a <see cref="TaskAccessDeniedException"/>.</summary>
    private static bool IsRawAccessDenied(Exception ex) => ex is UnauthorizedAccessException or COMException && TaskSchedulerErrors.IsAccessDenied(ex);

    private static TaskFolder Find(TaskService service, string path) =>
        Exists(service, path) ? service.GetFolder(path) : throw new DirectoryNotFoundException($"The folder '{path}' does not exist.");

    private static bool Exists(TaskService service, string path)
    {
        TaskFolder? folder = service.RootFolder;
        foreach (string name in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            folder = folder.SubFolders.FirstOrDefault(sub => string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase));
            if (folder is null) return false;
        }
        return true;
    }

    private static bool IsRoot(string path) => path.Trim('\\').Length == 0;
}
