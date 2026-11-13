namespace FluentTaskScheduler.Scheduling;

/// <summary>The tasks registered in the Windows Task Scheduler.</summary>
public interface ITaskStore
{
    /// <summary>Every task the current user can see, in all folders.</summary>
    IReadOnlyList<TaskRow> GetTasks();

    /// <summary>One task as <see cref="GetTasks"/> lists it, read afresh; null if it does not exist or this user cannot see it.</summary>
    TaskRow? GetTask(string path);

    bool Exists(string path);

    /// <summary>The current state of each of the given tasks; a task that no longer exists is left out.</summary>
    IReadOnlyDictionary<string, TaskRowState> GetStates(IEnumerable<string> paths);

    /// <summary>How many tasks are running at this moment, hidden ones included. A task with several instances counts once.</summary>
    int CountRunningTasks();

    /// <summary>Reads a task for editing.</summary>
    /// <exception cref="TaskNotFoundException">There is no task at <paramref name="path"/>.</exception>
    TaskDraft Load(string path);

    /// <summary>
    /// What the task starts when it finishes. Unlike <see cref="Load"/> this reads no more than
    /// that and never fails: it is asked for any task on the computer, including ones this user
    /// may not read. For those, and for a task that does not exist, the answer is
    /// <see cref="CompletionActions.None"/>.
    /// </summary>
    CompletionActions GetCompletionActions(string path);

    /// <summary>
    /// Creates a task in <paramref name="folderPath"/>, or changes the task at
    /// <paramref name="originalPath"/> when one is given. Renaming is saving under a new name.
    /// </summary>
    /// <param name="password">The password of the task's account; only needed when Windows stores one for the task.</param>
    /// <returns>The path of the saved task.</returns>
    string Save(TaskDraft draft, string folderPath, string? originalPath = null, string? password = null);

    void SetEnabled(string path, bool enabled);

    /// <summary>Asks Task Scheduler to start the task.</summary>
    /// <param name="force">
    /// True starts it now whatever the task itself says: its conditions (idle, power, network)
    /// are skipped, and so is its rule for an instance that is already running, so a second
    /// instance starts next to the first. False leaves both to Task Scheduler, as with a trigger:
    /// the request can then be ignored or queued, and that is not an error.
    /// </param>
    void Run(string path, bool force = true);

    void Stop(string path);
    void Delete(string path);

    /// <summary>The task as XML, in the format Task Scheduler itself exports and imports.</summary>
    string Export(string path);

    /// <summary>What an exported task says about the account it runs as.</summary>
    TaskImportInfo Inspect(string xml);

    /// <summary>Registers an exported task. It never replaces a task that is already there.</summary>
    /// <returns>The path of the new task.</returns>
    /// <exception cref="TaskExistsException">The folder already has a task of that name.</exception>
    string Import(string xml, TaskImportOptions options);
}

/// <summary>The account settings found in an exported task.</summary>
/// <param name="Account">The account the task was exported with.</param>
/// <param name="AccountExistsHere">False when that account cannot be found on this computer, as for a task exported elsewhere.</param>
/// <param name="HighestPrivileges">Whether the task asks to run elevated.</param>
/// <param name="StoresPassword">Whether the task needs its account's password to be registered.</param>
public sealed record TaskImportInfo(TaskAccount Account, bool AccountExistsHere, bool HighestPrivileges, bool StoresPassword);

/// <summary>How to register an exported task.</summary>
/// <param name="FolderPath">Folder to put the task in.</param>
/// <param name="Name">Name to give the task.</param>
/// <param name="Account">The account to run it as; null keeps the one in the file.</param>
/// <param name="HighestPrivileges">Whether it runs elevated; null keeps what the file says.</param>
/// <param name="Password">Password of the account, when the task stores one.</param>
public sealed record TaskImportOptions(string FolderPath, string Name, TaskAccount? Account = null, bool? HighestPrivileges = null, string? Password = null);

/// <summary>There is no task at the given path (any more).</summary>
public sealed class TaskNotFoundException : Exception
{
    public string Path { get; }
    public TaskNotFoundException(string path) : base($"The task '{path}' does not exist.") => Path = path;
}

/// <summary>There already is a task at the given path, and it was not to be replaced.</summary>
public sealed class TaskExistsException : Exception
{
    public string Path { get; }
    public TaskExistsException(string path) : base($"There already is a task '{path}'.") => Path = path;
}

/// <summary>Windows refused the operation: the task is protected, or the change needs administrator rights.</summary>
public sealed class TaskAccessDeniedException : Exception
{
    public string Path { get; }
    public TaskAccessDeniedException(string path, Exception inner) : base($"Access to the task '{path}' was denied.", inner) => Path = path;
}

/// <summary>The task's account could not be used: its password is missing or wrong, or the account does not exist.</summary>
public sealed class TaskAccountException : Exception
{
    public TaskAccountException(string message, Exception inner) : base(message, inner) { }
}
