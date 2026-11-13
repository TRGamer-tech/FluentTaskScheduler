using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class FoldersViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeTaskFolders _taskFolders;
    private readonly ScriptedPrompts _prompts = new();
    private readonly FakeAppLifetime _app = new() { IsElevated = true };
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSnoozeService _snooze = new();
    private readonly SettingsStore _settings;
    private readonly List<TasksViewModel> _opened = [];
    private readonly TasksViewModel _tasks;
    private readonly FoldersViewModel _folders;

    public FoldersViewModelTests()
    {
        _settings = new SettingsStore(_folder.Paths, _time);
        _taskFolders = new FakeTaskFolders(_store, @"\Jobs", @"\Jobs\Nightly", @"\Reports");
        (_tasks, _folders) = NewWindow();
    }

    public void Dispose()
    {
        foreach (TasksViewModel tasks in _opened) tasks.Dispose();
        _settings.Dispose();
        _folder.Dispose();
    }

    /// <summary>The task list and the folder tree of a window. All windows share the settings, as in the app.</summary>
    private (TasksViewModel Tasks, FoldersViewModel Folders) NewWindow()
    {
        var files = new ScriptedFilePicker();
        var tasks = new TasksViewModel(_store, new TaskRunner(_store, new FakeSnoozeGate(), new RecordingNotifier()), _settings, _prompts, files, _app,
            new InlineUiThread(), _time, new TaskEditorFactory(_store, _settings, _app, files, new FakeNetworkProfiles(), _time, _snooze), _taskFolders,
            new FakeTaskEventStream(), _snooze);
        _opened.Add(tasks);
        return (tasks, new FoldersViewModel(_taskFolders, tasks, _settings, _prompts, _app, _snooze));
    }

    /// <summary>Opens a window the way the app does: on what the user chose last, with the folders read.</summary>
    private async Task<(TasksViewModel Tasks, FoldersViewModel Folders)> OpenWindowAsync()
    {
        var window = NewWindow();
        window.Folders.ShowLastChoice();
        await window.Folders.ReloadAsync();
        return window;
    }

    private FolderNode Node(string path) => Find(_folders.Roots, path) ?? throw new InvalidOperationException($"No node for {path}.");

    private static FolderNode? Find(IEnumerable<FolderNode> nodes, string path) =>
        nodes.Select(node => node.Path == path ? node : Find(node.Children, path)).FirstOrDefault(found => found is not null);

    [Fact]
    public async Task TheTree_HasTheLibraryOnTop_AndEveryFolderUnderItsParent()
    {
        await _folders.ReloadAsync();

        FolderNode root = Assert.Single(_folders.Roots);
        Assert.True(root.IsRoot);
        Assert.True(root.IsExpanded);
        Assert.Equal(["Jobs", "Reports"], root.Children.Select(node => node.Name));
        Assert.Equal([@"\Jobs\Nightly"], Node(@"\Jobs").Children.Select(node => node.Path));
        Assert.Equal(4, _folders.AllPaths.Count);
    }

    [Fact]
    public async Task SelectingAFolder_ShowsItsTasks()
    {
        await _folders.ReloadAsync();

        _folders.Selected = Node(@"\Jobs\Nightly");

        Assert.Equal(@"\Jobs\Nightly", _tasks.FolderPath);
    }

    [Fact]
    public async Task RevealingATaskOfAnotherFolder_ShowsAllFolders_AndTheFolderCanBeChosenAgain()
    {
        await _folders.ReloadAsync();
        _folders.Selected = Node(@"\Jobs");

        _folders.Reveal(@"\Reports\Monthly");

        Assert.Null(_tasks.FolderPath);
        Assert.Null(_folders.Selected);
        _folders.Selected = Node(@"\Jobs");
        Assert.Equal(@"\Jobs", _tasks.FolderPath);
    }

    [Fact]
    public async Task RevealingATaskOfTheFolderShown_KeepsShowingThatFolder()
    {
        await _folders.ReloadAsync();
        _folders.Selected = Node(@"\Jobs");

        _folders.Reveal(@"\JOBS\Backup");

        Assert.Equal(@"\Jobs", _tasks.FolderPath);
        Assert.Equal(@"\Jobs", _folders.Selected?.Path);
    }

    [Fact]
    public async Task ReloadingKeepsWhatWasExpandedAndSelected()
    {
        await _folders.ReloadAsync();
        Node(@"\Jobs").IsExpanded = true;
        _folders.Selected = Node(@"\Jobs\Nightly");

        await _folders.ReloadAsync();

        Assert.True(Node(@"\Jobs").IsExpanded);
        Assert.False(Node(@"\Reports").IsExpanded);
        Assert.Equal(@"\Jobs\Nightly", _folders.Selected?.Path);
    }

    [Fact]
    public async Task CreatingAFolder_AsksForItsName_AndShowsItUnderItsParent()
    {
        await _folders.ReloadAsync();
        _prompts.Text = "Archive";

        await _folders.CreateAsync(Node(@"\Reports"));

        Assert.Equal([@"Create \Reports\Archive"], _taskFolders.Calls);
        Assert.NotNull(Find(_folders.Roots, @"\Reports\Archive"));
        Assert.True(Node(@"\Reports").IsExpanded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"bad\name")]
    public async Task WithoutAUsableName_NoFolderIsCreated(string? name)
    {
        await _folders.ReloadAsync();
        _prompts.Text = name;

        await _folders.CreateAsync(Node("\\"));

        Assert.Empty(_taskFolders.Calls);
    }

    [Fact]
    public async Task RenamingTheFolderBeingShown_KeepsShowingIt_UnderItsNewName()
    {
        await _folders.ReloadAsync();
        _folders.Selected = Node(@"\Jobs\Nightly");
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal([@"MoveFolder \Jobs -> \Work"], _taskFolders.Calls);
        Assert.Equal(@"\Work\Nightly", _tasks.FolderPath);
        Assert.Equal(@"\Work\Nightly", _folders.Selected?.Path);
    }

    [Fact]
    public async Task RenamingAFolder_TakesTheSnoozesOfItsTasksAlong()
    {
        _store.Add(@"\Jobs\Backup").Add(@"\Jobs\Nightly\Sync").Add(@"\Reports\Monthly");
        await _folders.ReloadAsync();
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal([@"TaskMoved \Jobs\Backup -> \Work\Backup", @"TaskMoved \Jobs\Nightly\Sync -> \Work\Nightly\Sync"], _snooze.Calls);
    }

    private static TaskDraft Starting(params string[] onSuccess) => new()
    {
        CompletionActions = new CompletionActions { Enabled = true, OnSuccess = [.. onSuccess] }
    };

    [Fact]
    public async Task RenamingAFolder_TakesTheCompletionActionsThatStartItsTasksAlong_AlsoBetweenTasksInIt()
    {
        _store.Add(@"\Jobs\Backup", draft: Starting(@"\Jobs\Nightly\Sync"))
              .Add(@"\Jobs\Nightly\Sync")
              .Add(@"\Reports\Monthly", draft: Starting(@"\Jobs\Backup", @"\Reports\Weekly"))
              .Add(@"\Reports\Weekly");
        await _folders.ReloadAsync();
        await _tasks.ReloadAsync();
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal([@"\Work\Nightly\Sync"], _store.GetCompletionActions(@"\Work\Backup").OnSuccess);
        Assert.Equal([@"\Work\Backup", @"\Reports\Weekly"], _store.GetCompletionActions(@"\Reports\Monthly").OnSuccess);
        Assert.Equal([@"Save \Work\Backup", @"Save \Reports\Monthly"], _store.Calls.Where(call => call.StartsWith("Save")));
    }

    [Fact]
    public async Task RenamingAFolder_BeforeTheTaskListWasRead_StillTakesTheCompletionActionsAlong()
    {
        _store.Add(@"\Jobs\Backup").Add(@"\Reports\Monthly", draft: Starting(@"\Jobs\Backup"));
        await _folders.ReloadAsync();
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal([@"\Work\Backup"], _store.GetCompletionActions(@"\Reports\Monthly").OnSuccess);
    }

    [Fact]
    public async Task AFolderWindowsMovesOnlyInPart_ShowsWhereEachTaskIsNow_AndSaysWhatStayedBehind()
    {
        _store.Add(@"\Jobs\Backup").Add(@"\Jobs\Kept").Protected.Add(@"\Jobs\Kept");
        await _folders.ReloadAsync();
        await _tasks.ReloadAsync();
        _folders.Selected = Node(@"\Jobs");
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        // Only the task that moved takes its snooze along; the other one is still where its snooze is.
        Assert.Equal([@"TaskMoved \Jobs\Backup -> \Work\Backup"], _snooze.Calls);
        Assert.NotNull(Find(_folders.Roots, @"\Jobs"));
        Assert.Equal(@"\Work", _folders.Selected?.Path);
        Assert.Equal([@"\Work\Backup"], _tasks.Tasks.Select(row => row.Path));
        Assert.NotNull(_tasks.Find(@"\Jobs\Kept"));
        Assert.Equal($"Error: Folders.Error.PartlyMoved{Environment.NewLine}Editor.Error.AccessDenied", _prompts.Shown.Last());
    }

    [Fact]
    public async Task AFolderWindowsDeletesOnlyInPart_LeavesTheTreeAndTheListShowingWhatIsLeft()
    {
        _store.Add(@"\Jobs\Backup").Add(@"\Jobs\Kept").Protected.Add(@"\Jobs\Kept");
        await _folders.ReloadAsync();
        await _tasks.ReloadAsync();

        await _folders.DeleteAsync(Node(@"\Jobs"));

        Assert.Null(_tasks.Find(@"\Jobs\Backup"));
        Assert.NotNull(_tasks.Find(@"\Jobs\Kept"));
        Assert.NotNull(Find(_folders.Roots, @"\Jobs"));
        Assert.Equal("Error: Editor.Error.AccessDenied", _prompts.Shown.Last());
    }

    [Fact]
    public async Task AFolderDeletedElsewhereMeanwhile_LeavesTheTree_WithoutAComplaint()
    {
        await _folders.ReloadAsync();
        _taskFolders.Delete(@"\Reports");

        await _folders.DeleteAsync(Node(@"\Reports"));

        Assert.Null(Find(_folders.Roots, @"\Reports"));
        Assert.DoesNotContain(_prompts.Shown, shown => shown.StartsWith("Error"));
    }

    [Fact]
    public async Task AFolderThatCannotBeRenamed_KeepsTheSnoozesOfItsTasksWhereTheyAre()
    {
        await _folders.ReloadAsync();
        _taskFolders.Fails = new TaskAccessDeniedException(@"\Jobs", new UnauthorizedAccessException());
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Empty(_snooze.Calls);
    }

    [Fact]
    public async Task DeletingAFolder_AsksFirst_AndFallsBackToItsParentWhenItWasShown()
    {
        await _folders.ReloadAsync();
        _folders.Selected = Node(@"\Jobs\Nightly");

        _prompts.Confirm = false;
        await _folders.DeleteAsync(Node(@"\Jobs\Nightly"));
        Assert.Empty(_taskFolders.Calls);

        _prompts.Confirm = true;
        await _folders.DeleteAsync(Node(@"\Jobs\Nightly"));
        Assert.Equal([@"Delete \Jobs\Nightly"], _taskFolders.Calls);
        Assert.Null(Find(_folders.Roots, @"\Jobs\Nightly"));
        Assert.Equal(@"\Jobs", _tasks.FolderPath);
    }

    [Fact]
    public async Task DeletingAFolderTheShownOneIsIn_FallsBackToTheFolderThatHeldIt_NotToANamesake()
    {
        _taskFolders.Create(@"\Jobs\Nightly\Late");
        _taskFolders.Create(@"\Jobs\Late");
        await _folders.ReloadAsync();
        _folders.Selected = Node(@"\Jobs\Nightly\Late");

        await _folders.DeleteAsync(Node(@"\Jobs\Nightly"));

        Assert.Equal(@"\Jobs", _tasks.FolderPath);
        Assert.Equal(@"\Jobs", _folders.Selected?.Path);
    }

    [Fact]
    public async Task TheLibraryItself_CanNeitherBeRenamedNorDeleted()
    {
        await _folders.ReloadAsync();
        _prompts.Text = "Mine";

        await _folders.RenameAsync(Node("\\"));
        await _folders.DeleteAsync(Node("\\"));

        Assert.Empty(_taskFolders.Calls);
        Assert.Empty(_prompts.Shown);
    }

    [Theory]
    [InlineData(true, "Editor.Error.AccessDenied")]
    [InlineData(false, "Editor.Error.AccessDeniedNotElevated")]
    public async Task WhenWindowsRefuses_TheUserIsTold_AndTheTreeStaysAsItIs(bool elevated, string expected)
    {
        _app.IsElevated = elevated;
        await _folders.ReloadAsync();
        _taskFolders.Fails = new TaskAccessDeniedException(@"\Jobs", new UnauthorizedAccessException());
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal($"Error: {expected}", _prompts.Shown.Last());
        Assert.NotNull(Find(_folders.Roots, @"\Jobs"));
    }

    [Theory]
    [InlineData("name taken", "Folders.Error.NameTaken")]
    [InlineData("deleted elsewhere", "Tasks.Error.Gone")]
    public async Task ARenameTaskSchedulerTurnsDown_IsReportedInTheUsersLanguage(string refusal, string expected)
    {
        await _folders.ReloadAsync();
        _taskFolders.Fails = refusal == "name taken"
            ? new FolderExistsException(@"\Work")
            : new DirectoryNotFoundException(@"The folder '\Jobs' does not exist.");
        _prompts.Text = "Work";

        await _folders.RenameAsync(Node(@"\Jobs"));

        Assert.Equal($"Error: {expected}", _prompts.Shown.Last());
    }

    [Fact]
    public async Task MovingTasksToAFolder_MovesEachSelectedTask()
    {
        _store.Add(@"\A").Add(@"\B").Add(@"\C");
        await _tasks.ReloadAsync();
        _tasks.Select([_tasks.Find(@"\A")!, _tasks.Find(@"\C")!]);

        await _tasks.MoveToFolderAsync(null, @"\Jobs");

        Assert.Equal([@"MoveTask \A -> \Jobs", @"MoveTask \C -> \Jobs"], _taskFolders.Calls);
    }

    [Fact]
    public async Task AFolderPickedInTheTree_IsWhereTheNextWindowOpens_WithTheTreeOpenedUpToIt()
    {
        await _folders.ReloadAsync();

        _folders.Choose(Node(@"\Jobs\Nightly"));

        var (tasks, folders) = await OpenWindowAsync();
        Assert.Equal(@"\Jobs\Nightly", tasks.FolderPath);
        Assert.Equal(@"\Jobs\Nightly", folders.Selected?.Path);
        Assert.True(Find(folders.Roots, @"\Jobs")!.IsExpanded);
    }

    [Fact]
    public async Task MovingTheTreesSelectionToAFolder_IsAChoiceToo()
    {
        await _folders.ReloadAsync();

        // As the tree does when its selection moves without the folder being invoked, by a screen
        // reader for one.
        _folders.Selected = Node(@"\Reports");

        Assert.Equal(@"\Reports", _settings.Current.LastFolderPath);
    }

    [Fact]
    public async Task ChoosingAllTasks_IsRememberedAsWell()
    {
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs"));

        _folders.ChooseAllTasks();

        Assert.Null(_tasks.FolderPath);
        Assert.Null(_folders.Selected);
        var (tasks, folders) = await OpenWindowAsync();
        Assert.Null(tasks.FolderPath);
        Assert.Null(folders.Selected);
    }

    [Fact]
    public async Task WhatEarlierVersionsSaved_OpensOnAllTasks_AsTheFirstStartDoes()
    {
        // The default, and so what a settings file of an earlier version holds, unless version 1.9
        // remembered a folder there.
        _settings.Update(s => s with { LastFolderPath = "\\" });

        var (tasks, folders) = await OpenWindowAsync();

        Assert.Null(tasks.FolderPath);
        Assert.Null(folders.Selected);
    }

    [Fact]
    public async Task TheRootFolder_IsRememberedApartFromAllTasks()
    {
        await _folders.ReloadAsync();

        _folders.Choose(Node("\\"));

        var (tasks, folders) = await OpenWindowAsync();
        Assert.Equal("\\", tasks.FolderPath);
        Assert.True(folders.Selected?.IsRoot);
    }

    [Fact]
    public async Task AChosenFolderThatIsGone_OpensAsAllTasks_AndIsForgotten()
    {
        _settings.Update(s => s with { LastFolderPath = @"\Archive" });

        var (tasks, folders) = await OpenWindowAsync();

        Assert.Null(tasks.FolderPath);
        Assert.Null(folders.Selected);
        Assert.Equal("\\", _settings.Current.LastFolderPath);
    }

    [Fact]
    public async Task WhileTheFoldersCannotBeRead_TheChosenFolderIsShownAndKept()
    {
        _settings.Update(s => s with { LastFolderPath = @"\Jobs" });
        _taskFolders.ListingFails = new InvalidOperationException("The Task Scheduler service is not available.");

        var (tasks, _) = await OpenWindowAsync();

        Assert.Equal(@"\Jobs", tasks.FolderPath);
        Assert.Equal(@"\Jobs", _settings.Current.LastFolderPath);
    }

    [Fact]
    public async Task RenamingTheChosenFolderOrOneItIsIn_TakesTheChoiceAlong_WhicheverWindowDoesIt()
    {
        var (_, other) = await OpenWindowAsync();
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs\Nightly"));
        _prompts.Text = "Work";

        await other.RenameAsync(Find(other.Roots, @"\Jobs")!);

        Assert.Equal(@"\Work\Nightly", _settings.Current.LastFolderPath);
    }

    [Fact]
    public async Task DeletingTheChosenFolderOrOneItIsIn_ForgetsTheChoice()
    {
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs\Nightly"));

        await _folders.DeleteAsync(Node(@"\Reports"));
        Assert.Equal(@"\Jobs\Nightly", _settings.Current.LastFolderPath);

        await _folders.DeleteAsync(Node(@"\Jobs"));
        Assert.Equal("\\", _settings.Current.LastFolderPath); // all tasks
        Assert.Equal("\\", _tasks.FolderPath); // the root folder, which held \Jobs
    }

    [Fact]
    public async Task RevealingATaskOfAnotherFolder_LeavesTheUsersChoiceAsItWas()
    {
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs"));

        _folders.Reveal(@"\Reports\Monthly");

        var (tasks, _) = await OpenWindowAsync();
        Assert.Equal(@"\Jobs", tasks.FolderPath);
    }

    [Fact]
    public async Task OfTwoWindows_TheLatestChoiceCounts_EvenOfAFolderShownAlready()
    {
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs"));
        var (otherTasks, other) = await OpenWindowAsync();

        other.Choose(Find(other.Roots, @"\Reports")!);
        Assert.Equal(@"\Jobs", _tasks.FolderPath);
        Assert.Equal(@"\Reports", _settings.Current.LastFolderPath);

        _folders.Choose(Node(@"\Jobs"));
        Assert.Equal(@"\Reports", otherTasks.FolderPath);
        Assert.Equal(@"\Jobs", _settings.Current.LastFolderPath);
    }

    [Fact]
    public async Task ReadingTheFoldersAgain_IsNoChoice()
    {
        await _folders.ReloadAsync();
        _folders.Choose(Node(@"\Jobs"));
        var (_, other) = await OpenWindowAsync();
        other.Choose(Find(other.Roots, @"\Reports")!);

        await _folders.ReloadAsync();

        Assert.Equal(@"\Jobs", _folders.Selected?.Path);
        Assert.Equal(@"\Reports", _settings.Current.LastFolderPath);
    }
}
