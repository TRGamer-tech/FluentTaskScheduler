using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// A throw-away folder in the real Windows Task Scheduler. Everything a test registers goes
/// in here and is deleted again when the sandbox is disposed.
/// </summary>
public sealed class SchedulerSandbox : IDisposable
{
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

    public void Dispose()
    {
        try
        {
            DeleteRecursive(Folder);
            Service.RootFolder.DeleteFolder(FolderPath.TrimStart('\\'), exceptionOnNotExists: false);
        }
        finally
        {
            Service.Dispose();
        }
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
