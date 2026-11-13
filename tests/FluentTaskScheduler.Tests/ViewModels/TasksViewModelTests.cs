using System.Text;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class TasksViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeSnoozeGate _snooze = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly ScriptedFilePicker _files = new();
    private readonly FakeAppLifetime _app = new() { IsElevated = true };
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2031, 5, 6, 14, 17, 0, TimeSpan.Zero));
    private readonly FakeTaskEventStream _stream = new();
    private readonly FakeTaskFolders _taskFolders = new(@"\Reports", @"\Reports\Old");
    private readonly FakeSnoozeService _snoozeService = new();
    private readonly SettingsStore _settings;
    private readonly TasksViewModel _tasks;

    public TasksViewModelTests()
    {
        _settings = new SettingsStore(_folder.Paths, _time);
        _tasks = NewViewModel(new InlineUiThread());

        _store.Add(@"\Backup", draft: new TaskDraft { Category = "Maintenance", Tags = ["night"] })
              .Add(@"\Reports\Monthly", enabled: false)
              .Add(@"\Reports\Weekly", draft: new TaskDraft { Hidden = true })
              .Add(@"\cleanup");
    }

    public void Dispose()
    {
        _tasks.Dispose();
        _settings.Dispose();
        _folder.Dispose();
    }

    private TasksViewModel NewViewModel(IUiThread ui) =>
        new(_store, new TaskRunner(_store, _snooze, _notifier), _settings, _prompts, _files, _app, ui, _time,
            new TaskEditorFactory(_store, _settings, _app, _files, new FakeNetworkProfiles(), _time, _snoozeService),
            _taskFolders, _stream, _snoozeService);

    private string[] Shown => _tasks.Tasks.Select(row => row.Path).ToArray();
    private TaskRow Row(string path) => _tasks.Find(path)!;

    private static TaskChange Change(string path, TaskChangeKind kind) => new(path, kind, DateTimeOffset.UnixEpoch);

    /// <summary>Lets the reload that reported changes have asked for happen, and waits until the list shows it.</summary>
    private async Task AfterTheReloadDelayAsync()
    {
        _time.Advance(TasksViewModel.ReloadDelay);
        await _tasks.PendingReads;
    }

    [Fact]
    public async Task Loading_ShowsEveryTask_InTheOrderTaskSchedulerListsThem()
    {
        await _tasks.ReloadAsync();

        Assert.Equal([@"\Backup", @"\Reports\Monthly", @"\Reports\Weekly", @"\cleanup"], Shown);
        Assert.False(_tasks.IsLoading);
        Assert.False(_tasks.HasLoadError);
    }

    [Fact]
    public async Task ReloadingKeepsTheRows_AndOnlyUpdatesThem()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        _store.SetEnabled(@"\Backup", false);
        _store.Delete(@"\cleanup");
        _store.Add(@"\New");

        await _tasks.ReloadAsync();

        Assert.Same(backup, Row(@"\Backup"));
        Assert.False(backup.IsEnabled);
        Assert.Equal([@"\Backup", @"\Reports\Monthly", @"\Reports\Weekly", @"\New"], Shown);
    }

    [Fact]
    public async Task WhenTheTasksCannotBeRead_TheListKeepsWhatItHad_AndSaysWhy()
    {
        await _tasks.ReloadAsync();
        _store.ListingFails = new InvalidOperationException("The Task Scheduler service is not available.");

        await _tasks.ReloadAsync();

        Assert.Equal("The Task Scheduler service is not available.", _tasks.LoadError);
        Assert.Equal(4, _tasks.Tasks.Count);

        _store.ListingFails = null;
        await _tasks.ReloadAsync();
        Assert.False(_tasks.HasLoadError);
    }

    [Theory]
    [InlineData("back", new[] { @"\Backup" })]
    [InlineData("REPORTS", new[] { @"\Reports\Monthly", @"\Reports\Weekly" })]
    [InlineData("maint", new[] { @"\Backup" })]
    [InlineData("night", new[] { @"\Backup" })]
    [InlineData("  ", new[] { @"\Backup", @"\Reports\Monthly", @"\Reports\Weekly", @"\cleanup" })]
    [InlineData("nothing like it", new string[0])]
    public async Task Searching_LooksAtNamePathCategoryAndTags(string search, string[] expected)
    {
        await _tasks.ReloadAsync();

        _tasks.SearchText = search;

        Assert.Equal(expected, Shown);
    }

    [Fact]
    public async Task AFolder_ShowsOnlyItsOwnTasks()
    {
        await _tasks.ReloadAsync();

        _tasks.FolderPath = @"\reports";
        Assert.Equal([@"\Reports\Monthly", @"\Reports\Weekly"], Shown);

        _tasks.FolderPath = "\\";
        Assert.Equal([@"\Backup", @"\cleanup"], Shown);

        _tasks.FolderPath = null;
        Assert.Equal(4, Shown.Length);
    }

    [Fact]
    public async Task TheStatusFilter_ShowsEnabledDisabledOrRunningTasks()
    {
        await _tasks.ReloadAsync();

        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Disabled;
        Assert.Equal([@"\Reports\Monthly"], Shown);

        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Enabled;
        Assert.Equal([@"\Backup", @"\Reports\Weekly", @"\cleanup"], Shown);

        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Running;
        Assert.Empty(Shown);
        await _tasks.RunCommand.ExecuteAsync(Row(@"\Backup"));
        _tasks.StatusFilterIndex = (int)TaskStatusFilter.All;
        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Running;
        Assert.Equal([@"\Backup"], Shown);
    }

    [Fact]
    public async Task HiddenTasks_FollowTheSetting_AlsoWhenItChangesLater()
    {
        _settings.Update(s => s with { ShowHiddenTasks = false });
        await _tasks.ReloadAsync();
        Assert.DoesNotContain(@"\Reports\Weekly", Shown);

        _settings.Update(s => s with { ShowHiddenTasks = true });

        Assert.Contains(@"\Reports\Weekly", Shown);
    }

    [Fact]
    public async Task SortingByName_AndAgain_ReversesTheOrder()
    {
        await _tasks.ReloadAsync();

        _tasks.SortByCommand.Execute(TaskSort.Name);
        Assert.Equal([@"\Backup", @"\cleanup", @"\Reports\Monthly", @"\Reports\Weekly"], Shown);
        Assert.True(_tasks.SortAscending);

        _tasks.SortByCommand.Execute(TaskSort.Name);
        Assert.Equal([@"\Reports\Weekly", @"\Reports\Monthly", @"\cleanup", @"\Backup"], Shown);

        _tasks.SortByCommand.Execute(TaskSort.None);
        Assert.Equal([@"\Backup", @"\Reports\Monthly", @"\Reports\Weekly", @"\cleanup"], Shown);
    }

    [Fact]
    public async Task SortingByNextRun_PutsTasksWithoutOneLast_BothWays()
    {
        _store.SetRunTimes(@"\Backup", null, new DateTime(2031, 6, 1))
              .SetRunTimes(@"\cleanup", null, new DateTime(2031, 5, 7));
        await _tasks.ReloadAsync();

        _tasks.SortByCommand.Execute(TaskSort.NextRun);
        Assert.Equal([@"\cleanup", @"\Backup", @"\Reports\Monthly", @"\Reports\Weekly"], Shown);

        _tasks.SortByCommand.Execute(TaskSort.NextRun);
        Assert.Equal([@"\Backup", @"\cleanup", @"\Reports\Monthly", @"\Reports\Weekly"], Shown);
    }

    [Fact]
    public async Task TheSortButton_SaysWhatTheListIsSortedBy()
    {
        await _tasks.ReloadAsync();
        Assert.Equal("Main.Toolbar.SortButton", _tasks.SortText);

        var changes = new List<string?>();
        _tasks.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _tasks.SortByCommand.Execute(TaskSort.LastRun);

        Assert.Contains(nameof(TasksViewModel.SortText), changes);
        Assert.Equal(TaskSort.LastRun, _tasks.Sort);
    }

    [Fact]
    public async Task Selection_MarksTheRows_AndASingleSelectionIsTheSelectedTask()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup"), cleanup = Row(@"\cleanup");

        _tasks.Select([backup]);
        Assert.Same(backup, _tasks.SelectedTask);
        Assert.True(backup.IsSelected);
        Assert.True(_tasks.HasSelection);
        Assert.False(_tasks.HasSeveralSelected);

        _tasks.Select([backup, cleanup]);
        Assert.Null(_tasks.SelectedTask);
        Assert.True(_tasks.HasSeveralSelected);

        _tasks.Select([cleanup]);
        Assert.False(backup.IsSelected);
        Assert.True(cleanup.IsSelected);
    }

    [Fact]
    public async Task RunningATask_StartsIt_ShowsItRunning_AndNoticesWhenItHasFinished()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");

        await _tasks.RunCommand.ExecuteAsync(backup);

        Assert.True(_store.IsRunning(@"\Backup"));
        Assert.True(backup.IsRunning);
        Assert.Equal(TaskRowState.Running, backup.State);
        Assert.Contains("TaskStarted Backup", _notifier.Shown);

        await _tasks.CheckWatchedAsync();
        Assert.True(backup.IsRunning);

        _store.Finish(@"\Backup");
        await _tasks.CheckWatchedAsync();
        Assert.False(backup.IsRunning);
        Assert.Equal(TaskRowState.Ready, backup.State);
    }

    [Fact]
    public async Task ATaskThatRunsForever_IsNotWatchedForever()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        await _tasks.RunCommand.ExecuteAsync(backup);

        _time.Advance(TimeSpan.FromMinutes(11));
        await _tasks.CheckWatchedAsync();

        Assert.False(backup.IsRunning);
        Assert.Equal(TaskRowState.Running, backup.State);
    }

    [Fact]
    public async Task WhileTasksAreSnoozed_NothingIsStarted_AndTheUserIsTold()
    {
        await _tasks.ReloadAsync();
        _snooze.Snoozed = true;
        _tasks.Select([Row(@"\Backup"), Row(@"\cleanup")]);

        await _tasks.RunCommand.ExecuteAsync(null);

        Assert.DoesNotContain(_store.Calls, call => call.StartsWith("Run"));
        Assert.False(Row(@"\Backup").IsRunning);
        Assert.Equal(2, _snooze.Suppressed.Count);
        Assert.Single(_prompts.Shown);
    }

    [Theory]
    [InlineData(true, "Tasks.Error.Denied")]
    [InlineData(false, "Tasks.Error.DeniedNotElevated")]
    public async Task ATaskWindowsRefusesToStart_IsReported_AndTheOthersStillStart(bool elevated, string expected)
    {
        _app.IsElevated = elevated;
        await _tasks.ReloadAsync();
        _store.Protected.Add(@"\Backup");
        _tasks.Select([Row(@"\Backup"), Row(@"\cleanup")]);

        await _tasks.RunCommand.ExecuteAsync(null);

        Assert.False(Row(@"\Backup").IsRunning);
        Assert.True(Row(@"\cleanup").IsRunning);
        Assert.Equal([$"Error: {expected}"], _prompts.Shown);
    }

    [Fact]
    public async Task Stopping_StopsTheTask()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        await _tasks.RunCommand.ExecuteAsync(backup);

        await _tasks.StopCommand.ExecuteAsync(backup);
        await _tasks.CheckWatchedAsync();

        Assert.False(_store.IsRunning(@"\Backup"));
        Assert.False(backup.IsRunning);
        Assert.Equal(TaskRowState.Ready, backup.State);
    }

    [Fact]
    public async Task DisablingATask_DisablesIt()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");

        await _tasks.SetEnabledAsync(backup, false);

        Assert.False(_store.IsEnabled(@"\Backup"));
        Assert.False(backup.IsEnabled);
        Assert.Equal(TaskRowState.Disabled, backup.State);
    }

    [Fact]
    public async Task WhenWindowsRefusesToDisableATask_TheRowGoesBack_AndTheUserIsTold()
    {
        await _tasks.ReloadAsync();
        _store.Protected.Add(@"\Backup");
        TaskRow backup = Row(@"\Backup");

        await _tasks.SetEnabledAsync(backup, false);

        Assert.True(backup.IsEnabled);
        Assert.True(_store.IsEnabled(@"\Backup"));
        Assert.Single(_prompts.Shown);
    }

    [Fact]
    public async Task TheSwitchInARow_ChangesThatTask_AndNoOther()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");

        backup.EnabledSwitch = false;

        await Eventually(() => !_store.IsEnabled(@"\Backup"));
        Assert.False(backup.IsEnabled);
        Assert.Equal(["SetEnabled \\Backup False"], _store.Calls.Where(call => call.StartsWith("SetEnabled")));
    }

    [Fact]
    public async Task ASwitchWindowsRefuses_GoesBack()
    {
        await _tasks.ReloadAsync();
        _store.Protected.Add(@"\Backup");
        TaskRow backup = Row(@"\Backup");

        backup.EnabledSwitch = false;

        await Eventually(() => _prompts.Shown.Count == 1);
        Assert.True(backup.IsEnabled);
        Assert.True(backup.EnabledSwitch);
    }

    [Fact]
    public async Task ARowThatAReloadUpdates_AsksForNothing()
    {
        await _tasks.ReloadAsync();
        _store.SetEnabled(@"\Backup", false);
        _store.Calls.Clear();

        await _tasks.ReloadAsync();

        Assert.False(Row(@"\Backup").EnabledSwitch);
        Assert.DoesNotContain(_store.Calls, call => call.StartsWith("SetEnabled"));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "It did not happen within two seconds.");
    }

    [Fact]
    public async Task SeveralTasks_CanBeEnabledAndDisabledTogether()
    {
        await _tasks.ReloadAsync();
        _tasks.Select([Row(@"\Backup"), Row(@"\Reports\Monthly"), Row(@"\cleanup")]);

        await _tasks.DisableSelectedCommand.ExecuteAsync(null);
        Assert.All(new[] { @"\Backup", @"\Reports\Monthly", @"\cleanup" }, path => Assert.False(_store.IsEnabled(path)));
        Assert.Equal(["SetEnabled \\Backup False", "SetEnabled \\cleanup False"], _store.Calls);

        await _tasks.EnableSelectedCommand.ExecuteAsync(null);
        Assert.All(new[] { @"\Backup", @"\Reports\Monthly", @"\cleanup" }, path => Assert.True(Row(path).IsEnabled));
    }

    [Fact]
    public async Task Deleting_AsksFirst_AndDoesNothingOnNo()
    {
        await _tasks.ReloadAsync();
        _prompts.Confirm = false;

        await _tasks.DeleteCommand.ExecuteAsync(Row(@"\Backup"));

        Assert.True(_store.Exists(@"\Backup"));
        Assert.Contains("Dialog.DeleteTask.ContentFormat", Assert.Single(_prompts.Shown));
    }

    [Fact]
    public async Task Deleting_RemovesTheTask_FromTaskSchedulerTheListAndTheSelection()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        _tasks.Select([backup]);

        await _tasks.DeleteCommand.ExecuteAsync(null);

        Assert.False(_store.Exists(@"\Backup"));
        Assert.DoesNotContain(@"\Backup", Shown);
        Assert.False(_tasks.HasSelection);
        Assert.Null(_tasks.Find(@"\Backup"));
    }

    [Fact]
    public async Task WithConfirmationSwitchedOff_DeletingDoesNotAsk()
    {
        _settings.Update(s => s with { ConfirmDelete = false });
        await _tasks.ReloadAsync();
        _tasks.Select([Row(@"\Backup"), Row(@"\cleanup")]);

        await _tasks.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_prompts.Shown);
        Assert.Equal([@"\Reports\Monthly", @"\Reports\Weekly"], Shown);
    }

    [Fact]
    public async Task ATaskThatCannotBeDeleted_StaysInTheList_AndIsReported()
    {
        _settings.Update(s => s with { ConfirmDelete = false });
        await _tasks.ReloadAsync();
        _store.Protected.Add(@"\Backup");

        await _tasks.DeleteCommand.ExecuteAsync(Row(@"\Backup"));

        Assert.Contains(@"\Backup", Shown);
        Assert.Equal(["Error: Tasks.Error.Denied"], _prompts.Shown);
    }

    [Fact]
    public async Task MovingTasksToAnotherFolder_TakesTheirSnoozesAlong()
    {
        await _tasks.ReloadAsync();
        _tasks.Select([Row(@"\Backup"), Row(@"\cleanup")]);

        await _tasks.MoveToFolderAsync(null, @"\Reports\Old");

        Assert.Equal([@"TaskMoved \Backup -> \Reports\Old\Backup", @"TaskMoved \cleanup -> \Reports\Old\cleanup"], _snoozeService.Calls);
    }

    [Fact]
    public async Task ATaskThatCannotBeMoved_KeepsItsSnoozeWhereItIs()
    {
        await _tasks.ReloadAsync();
        _taskFolders.Fails = new TaskAccessDeniedException(@"\Backup", new UnauthorizedAccessException());

        await _tasks.MoveToFolderAsync(Row(@"\Backup"), @"\Reports");

        Assert.Empty(_snoozeService.Calls);
        Assert.Equal(["Error: Tasks.Error.Denied"], _prompts.Shown);
    }

    [Fact]
    public async Task Exporting_WritesTheTaskXml_InTheEncodingItDeclares()
    {
        await _tasks.ReloadAsync();
        _files.Choice = _folder.File("Backup.xml");

        await _tasks.ExportCommand.ExecuteAsync(Row(@"\Backup"));

        byte[] bytes = File.ReadAllBytes(_files.Choice);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        Assert.Equal(_store.Export(@"\Backup"), Encoding.Unicode.GetString(bytes[2..]));
    }

    [Fact]
    public async Task CancellingTheExportDialog_WritesNothing()
    {
        await _tasks.ReloadAsync();
        _files.Choice = null;

        await _tasks.ExportCommand.ExecuteAsync(Row(@"\Backup"));

        Assert.Empty(Directory.GetFiles(_folder.Path, "*.xml"));
    }

    [Fact]
    public async Task EditingATask_OpensAnEditorOnWhatTaskSchedulerHolds()
    {
        await _tasks.ReloadAsync();

        TaskEditorViewModel? editor = await _tasks.EditAsync(Row(@"\Reports\Monthly"));

        Assert.NotNull(editor);
        Assert.False(editor.IsNew);
        Assert.Equal("Monthly", editor.Name);
        Assert.False(editor.Enabled);
        Assert.DoesNotContain(@"\Reports\Monthly", editor.CompletionActions.OtherTasks);
        Assert.Contains(@"\Backup", editor.CompletionActions.OtherTasks);
    }

    [Fact]
    public async Task EditingATaskThatWasDeletedMeanwhile_TellsTheUser_AndDropsTheRow()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        _store.Delete(@"\Backup");

        Assert.Null(await _tasks.EditAsync(backup));

        Assert.DoesNotContain(@"\Backup", Shown);
        Assert.Equal(["Error: Tasks.Error.Gone"], _prompts.Shown);
    }

    [Fact]
    public async Task ANewTask_IsCreatedInTheFolderBeingShown()
    {
        await _tasks.ReloadAsync();
        _tasks.FolderPath = @"\Reports";

        TaskEditorViewModel editor = _tasks.EditNew();
        editor.Name = "Yearly";
        editor.SelectedAction!.Path = "report.exe";

        Assert.True(editor.IsNew);
        Assert.True(await editor.SaveAsync());
        Assert.Equal(@"\Reports\Yearly", editor.SavedPath);
    }

    [Fact]
    public async Task ImportingStartsFromTheChosenFile_TheFoldersThatExist_AndTheFolderBeingShown()
    {
        _files.Choice = _folder.File("Nightly backup.xml");
        File.WriteAllText(_files.Choice, "<Task/>");
        _tasks.FolderPath = @"\Reports";

        ImportTaskViewModel? import = await _tasks.BeginImportAsync();

        Assert.NotNull(import);
        Assert.Equal("Nightly backup", import.Name);
        Assert.Equal(["\\", @"\Reports", @"\Reports\Old"], import.Folders);
        Assert.Equal(1, import.FolderIndex);
    }

    [Fact]
    public async Task AFileThatIsNotATask_IsNotOfferedForImport_AndTheUserIsToldWhy()
    {
        _files.Choice = _folder.File("notes.xml");
        File.WriteAllText(_files.Choice, "<notes/>");

        Assert.Null(await _tasks.BeginImportAsync());

        Assert.Contains("Import.Error.NotATask", Assert.Single(_prompts.Shown));
    }

    [Fact]
    public async Task CancellingTheFileDialog_ImportsNothing()
    {
        _files.Choice = null;

        Assert.Null(await _tasks.BeginImportAsync());
        Assert.Empty(_prompts.Shown);
    }

    [Fact]
    public async Task ARunThatATriggerStarts_ShowsOnItsRow_WithItsNewRunTimes_AndWithoutReadingEveryTask()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        var started = new DateTime(2031, 5, 6, 14, 17, 0);
        var next = new DateTime(2031, 5, 7, 14, 17, 0);

        _store.Run(@"\Backup");
        _store.SetRunTimes(@"\Backup", started, next);
        _stream.Start(FakeTaskEventStream.Running(@"\Backup"));
        await _tasks.PendingReads;

        Assert.Equal(TaskRowState.Running, backup.State);
        Assert.Equal(started, backup.LastRunTime);
        Assert.Equal(next, backup.NextRunTime);
        Assert.Equal(1, _store.Listings);
    }

    [Fact]
    public async Task WhenARunIsOver_ItsRowSettles()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        _store.Run(@"\Backup");
        _stream.Start(FakeTaskEventStream.Running(@"\Backup"));
        await _tasks.PendingReads;

        _store.Finish(@"\Backup");
        _stream.Complete(FakeTaskEventStream.Finished(@"\Backup", RunStatus.Succeeded));
        await _tasks.PendingReads;

        Assert.Equal(TaskRowState.Ready, backup.State);
    }

    [Fact]
    public async Task WhenOneOfTwoInstancesIsOver_TheRowKeepsShowingTheTaskRunning()
    {
        await _tasks.ReloadAsync();
        _store.Run(@"\Backup");

        // Task Scheduler still has the task running: its other instance.
        _stream.Complete(FakeTaskEventStream.Finished(@"\Backup", RunStatus.Succeeded));
        await _tasks.PendingReads;

        Assert.Equal(TaskRowState.Running, Row(@"\Backup").State);
    }

    [Fact]
    public async Task WithTheRunningFilterOn_ATaskEntersTheListWhenItStarts_AndLeavesWhenItIsDone()
    {
        await _tasks.ReloadAsync();
        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Running;
        Assert.Empty(Shown);

        _store.Run(@"\cleanup");
        _stream.Start(FakeTaskEventStream.Running(@"\cleanup"));
        await _tasks.PendingReads;
        Assert.Equal([@"\cleanup"], Shown);

        _store.Finish(@"\cleanup");
        _stream.Complete(FakeTaskEventStream.Finished(@"\cleanup", RunStatus.Failed));
        await _tasks.PendingReads;
        Assert.Empty(Shown);
    }

    [Fact]
    public async Task ATaskThatIsGoneWhenItsRunIsReported_LeavesTheList()
    {
        // A task can be set to delete itself once it will not run again.
        await _tasks.ReloadAsync();
        _store.Delete(@"\cleanup");

        _stream.Complete(FakeTaskEventStream.Finished(@"\cleanup", RunStatus.Succeeded));
        await _tasks.PendingReads;

        Assert.Equal([@"\Backup", @"\Reports\Monthly", @"\Reports\Weekly"], Shown);
        Assert.Null(_tasks.Find(@"\cleanup"));
    }

    [Fact]
    public async Task ATaskThatAnotherProgramRegisters_ShowsUpShortlyAfter_WithoutAProgressBar()
    {
        await _tasks.ReloadAsync();
        bool progressShown = false;
        _tasks.PropertyChanged += (_, e) => progressShown |= e.PropertyName == nameof(TasksViewModel.IsLoading);

        _store.Add(@"\Reports\Yearly");
        _stream.Change(Change(@"\Reports\Yearly", TaskChangeKind.Registered));
        _stream.Change(Change(@"\Reports\Yearly", TaskChangeKind.Updated));
        Assert.DoesNotContain(@"\Reports\Yearly", Shown);

        await AfterTheReloadDelayAsync();

        Assert.Contains(@"\Reports\Yearly", Shown);
        Assert.False(progressShown);
    }

    [Fact]
    public async Task ATaskThatAnotherProgramDeletes_Disappears_AndOneItChanges_ChangesInPlace()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        _tasks.Select([backup]);

        _store.Delete(@"\cleanup");
        _store.SetEnabled(@"\Backup", false);
        _stream.Change(Change(@"\cleanup", TaskChangeKind.Deleted));
        _stream.Change(Change(@"\Backup", TaskChangeKind.Updated));
        await AfterTheReloadDelayAsync();

        Assert.Equal([@"\Backup", @"\Reports\Monthly", @"\Reports\Weekly"], Shown);
        Assert.Same(backup, Row(@"\Backup"));
        Assert.False(backup.IsEnabled);
        Assert.True(backup.IsSelected);
    }

    [Fact]
    public async Task ManyChangesInARow_AreAnsweredWithOneReload_AndALaterChangeWithAnother()
    {
        await _tasks.ReloadAsync();

        for (int i = 0; i < 20; i++) _stream.Change(Change($@"\Setup\Task{i}", TaskChangeKind.Registered));
        await AfterTheReloadDelayAsync();
        Assert.Equal(2, _store.Listings);

        _stream.Change(Change(@"\Backup", TaskChangeKind.Updated));
        await AfterTheReloadDelayAsync();
        Assert.Equal(3, _store.Listings);
    }

    [Fact]
    public async Task ChangesThatKeepComing_DoNotPutTheReloadOff()
    {
        await _tasks.ReloadAsync();
        _store.Add(@"\New");

        _stream.Change(Change(@"\New", TaskChangeKind.Registered));
        _time.Advance(TasksViewModel.ReloadDelay / 2);
        _stream.Change(Change(@"\New", TaskChangeKind.Updated));
        _time.Advance(TasksViewModel.ReloadDelay / 2);
        await _tasks.PendingReads;

        Assert.Contains(@"\New", Shown);
    }

    private static SnoozeState Snoozing(string[] ownSnoozes, string[] suspendedByAll) => new()
    {
        Global = suspendedByAll.Length > 0 ? new GlobalSnooze(SnoozeWindow.UntilRestart, SuspendsTriggers: true, [.. suspendedByAll]) : null,
        Tasks = ownSnoozes.Select(path => new TaskSnooze(path, SnoozeWindow.UntilRestart)).ToValueList()
    };

    private IEnumerable<string> MarkedSnoozed => _tasks.Tasks.Where(row => row.IsSnoozed).Select(row => row.Path);

    [Fact]
    public async Task ARow_ShowsWhetherASnoozeKeepsItsTaskDisabled()
    {
        await _tasks.ReloadAsync();

        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\BACKUP"], suspendedByAll: [@"\cleanup"]));
        Assert.Equal([@"\Backup", @"\cleanup"], MarkedSnoozed);

        _snoozeService.Publish(new SnoozeState());
        Assert.Empty(MarkedSnoozed);
    }

    [Fact]
    public async Task ASnoozeOfAllTasksThatDisablesNone_MarksNoRow()
    {
        await _tasks.ReloadAsync();

        _snoozeService.Publish(new SnoozeState { Global = new GlobalSnooze(SnoozeWindow.UntilRestart, SuspendsTriggers: false, []) });

        Assert.Empty(MarkedSnoozed);
    }

    [Fact]
    public async Task ATaskThatShowsUpLater_IsMarkedToo()
    {
        await _tasks.ReloadAsync();
        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\New"], suspendedByAll: []));
        _store.Add(@"\New");

        await _tasks.ReloadAsync();

        Assert.True(Row(@"\New").IsSnoozed);
    }

    [Fact]
    public async Task TheSnoozedFilter_ShowsTheTasksASnoozeKeepsDisabled_AsTheSnoozeChanges()
    {
        await _tasks.ReloadAsync();
        _tasks.StatusFilterIndex = (int)TaskStatusFilter.Snoozed;
        Assert.Empty(Shown);

        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []));
        Assert.Equal([@"\Backup"], Shown);

        _snoozeService.Publish(new SnoozeState());
        Assert.Empty(Shown);
    }

    [Fact]
    public async Task OnceASnoozeHasDisabledOrEnabledTasks_TheyAreReadAgain()
    {
        await _tasks.ReloadAsync();
        SnoozeState snoozed = Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []);

        _snoozeService.Publish(snoozed with { IsBusy = true });
        _store.SetEnabled(@"\Backup", false);
        await AfterTheReloadDelayAsync();
        Assert.Equal(1, _store.Listings);

        _snoozeService.Publish(snoozed);
        await AfterTheReloadDelayAsync();

        Assert.Equal(2, _store.Listings);
        Assert.False(Row(@"\Backup").IsEnabled);
    }

    [Fact]
    public async Task ASnoozeThatSwitchesNoTask_IsNoReasonToReadThemAgain()
    {
        await _tasks.ReloadAsync();

        _snoozeService.Publish(new SnoozeState { Global = new GlobalSnooze(SnoozeWindow.UntilRestart, SuspendsTriggers: false, []) });
        _snoozeService.Publish(new SnoozeState());
        await AfterTheReloadDelayAsync();

        Assert.Equal(1, _store.Listings);
    }

    [Fact]
    public async Task TasksASnoozeSwitched_AreReadAgain_EvenIfTheUiThreadOnlyGetsToItAfterwards()
    {
        _tasks.Dispose();
        var ui = new BusyUiThread();
        using TasksViewModel late = NewViewModel(ui);
        await late.ReloadAsync();
        SnoozeState snoozed = Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []);

        _snoozeService.Publish(snoozed with { IsBusy = true });
        _snoozeService.Publish(snoozed);
        ui.RunPosted();
        _time.Advance(TasksViewModel.ReloadDelay);
        ui.RunPosted();
        await late.PendingReads;

        Assert.Equal(2, _store.Listings);
        Assert.True(late.Find(@"\Backup")!.IsSnoozed);
    }

    [Fact]
    public async Task ASlowReload_DoesNotOverwriteWhatALaterReportAboutOneTaskFound()
    {
        await _tasks.ReloadAsync();
        _store.Run(@"\Backup");

        // The reload has read the tasks, Backup still running among them. Before it hands them
        // over, the run ends and is reported.
        _store.Listed = () =>
        {
            _store.Listed = null;
            _store.Finish(@"\Backup");
            _stream.Complete(FakeTaskEventStream.Finished(@"\Backup", RunStatus.Succeeded));

            // A list that looked at the one task right away would be done by now, and would then
            // be overwritten with what the reload holds. One that takes turns is still waiting.
            Assert.False(_tasks.PendingReads.Wait(TimeSpan.FromMilliseconds(250)));
        };
        await _tasks.ReloadAsync();
        await _tasks.PendingReads;

        Assert.Equal(TaskRowState.Ready, Row(@"\Backup").State);
        Assert.False(_tasks.HasLoadError);
    }

    [Fact]
    public async Task AReloadAskedForDuringAnother_ReadsTheTasksAgain_AfterIt()
    {
        await _tasks.ReloadAsync();
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _store.Listed = () =>
        {
            _store.Listed = null;
            reading.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };
        Task first = _tasks.ReloadAsync();
        Assert.True(reading.Wait(TimeSpan.FromSeconds(5)));

        // Registered after the first reload has read the tasks, as by a move just made.
        _store.Add(@"\Reports\Yearly");
        Task second = _tasks.ReloadAsync();
        release.Set();
        await second;

        Assert.Contains(@"\Reports\Yearly", Shown);
        await first;
        Assert.False(_tasks.IsLoading);
    }

    [Fact]
    public async Task WhenShowingOneReportFails_TheListStillReadsWhatComesAfter()
    {
        await _tasks.ReloadAsync();
        TaskRow backup = Row(@"\Backup");
        void Fail(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => throw new InvalidOperationException("The view could not show the change.");
        backup.PropertyChanged += Fail;
        _store.Run(@"\Backup");

        _stream.Start(FakeTaskEventStream.Running(@"\Backup"));
        await _tasks.PendingReads;
        backup.PropertyChanged -= Fail;

        _store.Add(@"\New");
        await _tasks.ReloadAsync();
        Assert.Contains(@"\New", Shown);
        Assert.Equal(TaskRowState.Running, backup.State);
    }

    [Fact]
    public async Task AClosedWindow_NoLongerListens()
    {
        await _tasks.ReloadAsync();
        _tasks.Dispose();
        _store.Add(@"\New");
        _store.Run(@"\Backup");

        _stream.Start(FakeTaskEventStream.Running(@"\Backup"));
        _stream.Change(Change(@"\New", TaskChangeKind.Registered));
        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []));
        await AfterTheReloadDelayAsync();

        Assert.Equal(0, _stream.Subscribers);
        Assert.Equal(0, _snoozeService.Subscribers);
        Assert.Equal(1, _store.Listings);
        Assert.Equal(TaskRowState.Ready, Row(@"\Backup").State);
        Assert.False(Row(@"\Backup").IsSnoozed);
    }

    [Fact]
    public async Task AReportThatReachesTheUiThreadAfterTheWindowClosed_ChangesNothing()
    {
        _tasks.Dispose();
        var ui = new BusyUiThread();
        using TasksViewModel closing = NewViewModel(ui);
        await closing.ReloadAsync();
        _store.Run(@"\Backup");

        _stream.Start(FakeTaskEventStream.Running(@"\Backup"));
        _stream.Change(Change(@"\Backup", TaskChangeKind.Updated));
        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []) with { IsBusy = true });
        _snoozeService.Publish(Snoozing(ownSnoozes: [@"\Backup"], suspendedByAll: []));
        closing.Dispose();
        ui.RunPosted();
        _time.Advance(TasksViewModel.ReloadDelay);
        await closing.PendingReads;

        Assert.Equal(TaskRowState.Ready, closing.Find(@"\Backup")!.State);
        Assert.False(closing.Find(@"\Backup")!.IsSnoozed);
        Assert.Equal(1, _store.Listings);
    }

    [Fact]
    public void WithoutAdministratorRights_TheListAdmitsItMayBeIncomplete_AndOffersTheWayOut()
    {
        _app.IsElevated = false;
        Assert.False(_tasks.ShowsEveryTask);

        _tasks.RestartElevatedCommand.Execute(null);

        Assert.True(_app.RestartElevatedRequested);
    }
}
