using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>Folder operations against the real Task Scheduler, inside a throw-away folder.</summary>
[Collection(SchedulerSandbox.Collection)]
public class TaskFoldersTests
{
    private const string NeedsElevation = "needs an elevated token";
    private readonly TaskFolders _folders = new();
    private readonly TaskStore _store = new();

    [SkippableFact]
    public void Folders_AreListedParentsFirst_AndCanBeCreatedNested()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();

        _folders.Create(sandbox.FolderPath + @"\A\B");

        var listed = _folders.GetFolders().ToList();
        Assert.Equal("\\", listed[0]);
        int parent = listed.IndexOf(sandbox.FolderPath + @"\A"), child = listed.IndexOf(sandbox.FolderPath + @"\A\B");
        Assert.True(parent > 0 && child > parent, "The nested folders are missing or out of order.");
    }

    [SkippableFact]
    public void MovingATask_KeepsItsDefinitionAndItsAccount()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        var task = sandbox.Folder.RegisterTaskDefinition("Mover", SampleTasks.Rich(sandbox.Service), TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);
        TaskDraft before = _store.Load(task.Path);

        string moved = _folders.MoveTask(task.Path, sandbox.FolderPath + @"\Target");

        Assert.Equal(sandbox.FolderPath + @"\Target\Mover", moved);
        Assert.False(_store.Exists(task.Path));
        Assert.Equal(before, _store.Load(moved));
        Assert.Equal(TaskAccount.System, _store.Load(moved).Account);
    }

    [SkippableFact]
    public void MovingATask_OntoAnotherOfTheSameName_IsRefused_AndBothStay()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        var task = sandbox.Folder.RegisterTaskDefinition("Twin", SampleTasks.Simple(sandbox.Service));
        var other = SampleTasks.Simple(sandbox.Service);
        other.RegistrationInfo.Description = "Was there first";
        sandbox.Folder.CreateFolder("Target").RegisterTaskDefinition("Twin", other);

        Assert.Throws<TaskExistsException>(() => _folders.MoveTask(task.Path, sandbox.FolderPath + @"\Target"));

        Assert.True(_store.Exists(task.Path));
        Assert.Equal("Was there first", _store.Load(sandbox.FolderPath + @"\Target\Twin").Description);
    }

    [SkippableFact]
    public void RenamingAFolder_TakesItsTasksAndSubFoldersAlong()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        var old = sandbox.Folder.CreateFolder("Old");
        old.RegisterTaskDefinition("One", SampleTasks.Simple(sandbox.Service));
        old.CreateFolder("Inner").RegisterTaskDefinition("Two", SampleTasks.Rich(sandbox.Service));

        _folders.MoveFolder(sandbox.FolderPath + @"\Old", sandbox.FolderPath + @"\New");

        Assert.True(_store.Exists(sandbox.FolderPath + @"\New\One"));
        Assert.True(_store.Exists(sandbox.FolderPath + @"\New\Inner\Two"));
        Assert.DoesNotContain(sandbox.FolderPath + @"\Old", _folders.GetFolders());
    }

    [SkippableFact]
    public void ChangingOnlyTheCaseOfItsName_RenamesTheFolder()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        sandbox.Folder.CreateFolder("jobs").RegisterTaskDefinition("One", SampleTasks.Simple(sandbox.Service));

        _folders.MoveFolder(sandbox.FolderPath + @"\jobs", sandbox.FolderPath + @"\Jobs");

        Assert.Equal([sandbox.FolderPath + @"\Jobs"], _folders.GetFolders().Where(path => path.StartsWith(sandbox.FolderPath + @"\", StringComparison.Ordinal)));
        Assert.True(_store.Exists(sandbox.FolderPath + @"\Jobs\One"));
    }

    [SkippableFact]
    public void AFolderCannotBeMovedIntoItself_OrOntoAnExistingOne()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        sandbox.Folder.CreateFolder("A").RegisterTaskDefinition("Keep", SampleTasks.Simple(sandbox.Service));
        sandbox.Folder.CreateFolder("B");

        Assert.Throws<InvalidOperationException>(() => _folders.MoveFolder(sandbox.FolderPath + @"\A", sandbox.FolderPath + @"\A\Sub"));
        Assert.Throws<InvalidOperationException>(() => _folders.MoveFolder(sandbox.FolderPath + @"\A", sandbox.FolderPath + @"\B"));

        Assert.True(_store.Exists(sandbox.FolderPath + @"\A\Keep"));
    }

    [SkippableFact]
    public void DeletingAFolder_RemovesItWithEverythingInIt()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, NeedsElevation);
        using var sandbox = new SchedulerSandbox();
        var doomed = sandbox.Folder.CreateFolder("Doomed");
        doomed.RegisterTaskDefinition("One", SampleTasks.Simple(sandbox.Service));
        doomed.CreateFolder("Inner").RegisterTaskDefinition("Two", SampleTasks.Simple(sandbox.Service));

        _folders.Delete(sandbox.FolderPath + @"\Doomed");

        Assert.DoesNotContain(_folders.GetFolders(), path => path.StartsWith(sandbox.FolderPath + @"\Doomed", StringComparison.OrdinalIgnoreCase));
        Assert.False(_store.Exists(sandbox.FolderPath + @"\Doomed\One"));
    }

    [Fact]
    public void TheRootFolder_CanNeitherBeDeletedNorMoved()
    {
        Assert.Throws<InvalidOperationException>(() => _folders.Delete("\\"));
        Assert.Throws<InvalidOperationException>(() => _folders.MoveFolder("\\", @"\Elsewhere"));
    }
}
