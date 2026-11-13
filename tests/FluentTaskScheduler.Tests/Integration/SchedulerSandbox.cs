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

    private readonly List<ScheduledTask> _undeletable = [];

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

    /// <summary>Registers a simple task in <paramref name="folder"/> that nobody may delete until the sandbox is disposed.</summary>
    public ScheduledTask RegisterUndeletable(TaskFolder folder, string name)
    {
        var task = folder.RegisterTaskDefinition(name, SampleTasks.Simple(Service), TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken, NoDeleting);
        _undeletable.Add(task);
        Assert.True(TaskSchedulerErrors.IsAccessDenied(Record.Exception(() => folder.DeleteTask(name))),
            "Task Scheduler deleted a task whose security descriptor denies that, so the test proves nothing.");
        return task;
    }

    public void Dispose()
    {
        try
        {
            foreach (var task in _undeletable) AllowDeleting(task);
            DeleteRecursive(Folder);
            Service.RootFolder.DeleteFolder(FolderPath.TrimStart('\\'), exceptionOnNotExists: false);
        }
        finally
        {
            Service.Dispose();
        }
    }

    private static void AllowDeleting(ScheduledTask task)
    {
        try
        {
            task.SetSecurityDescriptorSddlForm(Deletable);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // The test removed it already.
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
