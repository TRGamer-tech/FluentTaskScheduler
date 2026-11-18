using System.Security.Principal;
using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// A throw-away folder in the real Windows Task Scheduler. Everything a test registers goes
/// in here and is deleted again when the sandbox is disposed.
/// </summary>
public sealed class SchedulerSandbox : IDisposable
{
    // Everyone may do anything with the task except delete it, as with some of Windows' own tasks.
    private const string NoDeleting = "D:(D;;SD;;;WD)(A;;FA;;;WD)";
    private const string Deletable = "D:(A;;FA;;;WD)";

    // As in the file system, the right to delete what a folder holds (DT) lets an administrator
    // delete a task that denies it. Everything else the folder and its contents allow everyone.
    private const string NoDeletingContents = "D:(D;;DT;;;WD)(A;OICI;FA;;;WD)";

    private bool _hasUndeletable;

    public TaskService Service { get; } = new();
    public TaskFolder Folder { get; }
    public string FolderPath { get; }

    public SchedulerSandbox()
    {
        string name = "FtsTests_" + Guid.NewGuid().ToString("N")[..8];
        Folder = Service.RootFolder.CreateFolder(name);
        FolderPath = "\\" + name;
    }

    /// <summary>
    /// Registering tasks for other accounts and reading every task needs an elevated token.
    /// False on anything that is not Windows, where there is no Task Scheduler to test against.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Tests that touch the machine's Task Scheduler run one at a time.</summary>
    public const string Collection = "Task Scheduler";

    public string PathOf(string taskName) => FolderPath + "\\" + taskName;

    /// <summary>
    /// Registers a simple task in <paramref name="folder"/> that nobody may delete until the sandbox
    /// is disposed. The other tasks of the folder stay deletable. Skips the test where this account
    /// may delete it all the same, since the test would then prove nothing.
    /// </summary>
    public ScheduledTask RegisterUndeletable(TaskFolder folder, string name)
    {
        _hasUndeletable = true;
        folder.SetSecurityDescriptorSddlForm(NoDeletingContents, TaskSetSecurityOptions.None);
        var task = folder.RegisterTaskDefinition(name, SampleTasks.Simple(Service));
        // Set on the registered task rather than given at registration, so that the definition a
        // move copies cannot carry it along. Without the flag, Task Scheduler would add an entry
        // that allows the task's own account everything.
        task.SetSecurityDescriptorSddlForm(NoDeleting, TaskSetSecurityOptions.DontAddPrincipalAce);

        string taskSecurity = task.GetSecurityDescriptorSddlForm(), folderSecurity = folder.GetSecurityDescriptorSddlForm();
        Skip.IfNot(TaskSchedulerErrors.IsAccessDenied(Record.Exception(() => folder.DeleteTask(name))),
            $"This account may delete a task with the security descriptor {taskSecurity} in a folder with {folderSecurity}, so no task can be made undeletable here.");
        return task;
    }

    public void Dispose()
    {
        try
        {
            // Copies the code under test made of such a task may have taken its security descriptor along.
            if (_hasUndeletable) AllowDeleting(Folder);
            DeleteRecursive(Folder);
            Service.RootFolder.DeleteFolder(FolderPath.TrimStart('\\'), exceptionOnNotExists: false);
        }
        finally
        {
            Service.Dispose();
        }
    }

    private static void AllowDeleting(TaskFolder folder)
    {
        foreach (var task in folder.Tasks.ToList()) task.SetSecurityDescriptorSddlForm(Deletable);
        foreach (var sub in folder.SubFolders.ToList()) AllowDeleting(sub);
    }

    private static void DeleteRecursive(TaskFolder folder)
    {
        foreach (var task in folder.Tasks.ToList()) folder.DeleteTask(task.Name, exceptionOnNotExists: false);
        foreach (var sub in folder.SubFolders.ToList())
        {
            DeleteRecursive(sub);
            folder.DeleteFolder(sub.Name, exceptionOnNotExists: false);
        }
    }
}

[CollectionDefinition(SchedulerSandbox.Collection, DisableParallelization = true)]
public sealed class SchedulerCollection;
