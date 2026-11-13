using System.Diagnostics;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Snoozing;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;
using TaskInstancesPolicy = Microsoft.Win32.TaskScheduler.TaskInstancesPolicy;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class TaskEditorViewModelTests : IDisposable
{
    private static readonly Guid HomeNetwork = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeAppLifetime _app = new() { IsElevated = true };
    private readonly ScriptedFilePicker _files = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2031, 5, 6, 14, 17, 0, TimeSpan.Zero));
    private readonly FakeSnoozeService _snooze = new();
    private readonly SettingsStore _settings;

    public TaskEditorViewModelTests() => _settings = new SettingsStore(_folder.Paths, _time);

    public void Dispose()
    {
        _settings.Dispose();
        _folder.Dispose();
    }

    private TaskEditorViewModel Edit(TaskDraft task, string? path = @"\Jobs\Nightly", params string[] otherTasks) =>
        new TaskEditorFactory(_store, _settings, _app, _files, new FakeNetworkProfiles(new NetworkProfile(HomeNetwork, "Home")), _time, _snooze)
            .Create(task, @"\Jobs", path, otherTasks);

    /// <summary>A task with nothing left at its default, as an administrator might have set it up elsewhere.</summary>
    private static TaskDraft Elaborate() => new()
    {
        Name = "Nightly",
        Description = "Copies the day's work",
        Author = @"CORP\admin",
        Category = "Maintenance",
        Tags = ["backup", "night"],
        CompletionActions = new CompletionActions { Enabled = true, OnSuccess = [@"\Jobs\Report"], OnFailure = [@"\Jobs\Alert"] },
        Enabled = false,
        Hidden = true,
        Account = new TaskAccount(TaskAccountKind.NetworkService),
        HighestPrivileges = true,
        RunOnlyIfIdle = true,
        IdleDuration = TimeSpan.FromSeconds(605),
        StopOnIdleEnd = true,
        RunOnlyOnAcPower = true,
        StopOnBattery = true,
        RunOnlyIfNetwork = true,
        NetworkId = HomeNetwork,
        NetworkName = "Home (as stored)",
        WakeToRun = true,
        ExecutionTimeLimit = TimeSpan.FromHours(72),
        RunIfMissed = true,
        MultipleInstances = TaskInstancesPolicy.Queue,
        Priority = ProcessPriorityClass.Normal,
        DeleteWhenExpired = true,
        AllowHardTerminate = false,
        RestartCount = 7,
        RestartInterval = TimeSpan.FromSeconds(95),
        Triggers =
        [
            new TriggerDraft { Type = TriggerType.Weekly, SourceIndex = 0, Start = new DateTime(2030, 1, 2, 3, 4, 5), End = new DateTime(2035, 1, 1), DaysOfWeek = DaysOfTheWeek.Friday },
            new TriggerDraft { Type = TriggerType.Unsupported, SourceIndex = 1, UnsupportedSummary = "Registration" }
        ],
        Actions =
        [
            new ActionDraft { SourceIndex = 0, Path = " robocopy ", Arguments = @"C:\a D:\b /MIR", WorkingDirectory = @"C:\" },
            new ActionDraft { SourceIndex = 1, UnsupportedSummary = "ComHandler" }
        ]
    };

    [Fact]
    public void ATaskNobodyTouched_BuildsIntoExactlyWhatWasLoaded()
    {
        TaskDraft loaded = Elaborate();

        Assert.True(Edit(loaded).TryBuild(out TaskDraft built, out var problems), string.Join("; ", problems.Select(p => p.Key)));
        Assert.Equal(loaded, built);
    }

    [Fact]
    public void TurningTheRunTimeLimitOff_ChangesNothingElse()
    {
        TaskDraft loaded = Elaborate();
        var editor = Edit(loaded);
        Assert.True(editor.LimitRunTime);

        editor.LimitRunTime = false;

        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal(loaded with { ExecutionTimeLimit = TimeSpan.Zero }, built);
        Assert.Equal(loaded.Account, built.Account);
    }

    [Fact]
    public void ATaskWithoutARunTimeLimit_OpensWithTheLimitOff_AndStaysWithoutOne()
    {
        TaskDraft loaded = Elaborate() with { ExecutionTimeLimit = TimeSpan.Zero };
        var editor = Edit(loaded);

        Assert.False(editor.LimitRunTime);
        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal(TimeSpan.Zero, built.ExecutionTimeLimit);
    }

    [Fact]
    public void SwitchingTheLimitOn_SuggestsThreeDays()
    {
        var editor = Edit(Elaborate() with { ExecutionTimeLimit = TimeSpan.Zero });

        editor.LimitRunTime = true;

        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal(TimeSpan.FromDays(3), built.ExecutionTimeLimit);
    }

    [Theory]
    [InlineData(ProcessPriorityClass.RealTime)]
    [InlineData(ProcessPriorityClass.High)]
    [InlineData(ProcessPriorityClass.AboveNormal)]
    [InlineData(ProcessPriorityClass.Normal)]
    [InlineData(ProcessPriorityClass.BelowNormal)]
    [InlineData(ProcessPriorityClass.Idle)]
    public void ThePriorityOfTheTask_IsTheOneSelected_AndSurvivesSaving(ProcessPriorityClass priority)
    {
        var editor = Edit(Elaborate() with { Priority = priority });

        Assert.Equal(priority, TaskEditorViewModel.Priorities[editor.PriorityIndex]);
        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal(priority, built.Priority);
    }

    [Theory]
    [InlineData(TaskAccountKind.CurrentUser, "", 0, 3)]
    [InlineData(TaskAccountKind.User, @"CORP\svc", 1, 3)]
    [InlineData(TaskAccountKind.System, "", 2, 3)]
    [InlineData(TaskAccountKind.LocalService, "", 3, 4)]
    [InlineData(TaskAccountKind.NetworkService, "", 3, 4)]
    [InlineData(TaskAccountKind.Group, @"BUILTIN\Users", 3, 4)]
    public void TheAccountOfTheTask_IsTheOneSelected_AndKeptUnlessChanged(TaskAccountKind kind, string name, int expectedIndex, int expectedOptions)
    {
        var account = new TaskAccount(kind, name);
        var editor = Edit(Elaborate() with { Account = account });

        Assert.Equal(expectedIndex, editor.AccountIndex);
        Assert.Equal(expectedOptions, editor.AccountOptions.Count);
        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal(account, built.Account);
    }

    [Fact]
    public void ChoosingAnotherAccount_ChangesTheAccount()
    {
        var editor = Edit(Elaborate());

        editor.AccountIndex = 1;
        editor.UserName = @" CORP\backup ";
        Assert.True(editor.RunsAsOtherUser);
        Assert.True(editor.TryBuild(out TaskDraft asUser, out _));
        Assert.Equal(new TaskAccount(TaskAccountKind.User, @"CORP\backup"), asUser.Account);

        editor.AccountIndex = 2;
        Assert.True(editor.TryBuild(out TaskDraft asSystem, out _));
        Assert.Equal(TaskAccount.System, asSystem.Account);
    }

    [Fact]
    public async Task ATaskWithAStoredPassword_AsksForIt_AndPassesItOn()
    {
        _store.Add(@"\Jobs\Nightly");
        var editor = Edit(Elaborate() with { Account = new TaskAccount(TaskAccountKind.User, @"CORP\svc"), StoresPassword = true });
        Assert.True(editor.NeedsPassword);

        Assert.False(await editor.SaveAsync());
        Assert.Contains("Editor.Error.PasswordNeeded", editor.Problems);
        Assert.Empty(_store.Calls);

        editor.Password = "s3cret";
        Assert.True(await editor.SaveAsync());
        Assert.Equal("s3cret", _store.LastPassword);
    }

    [Fact]
    public void ChangingTheAccount_OfATaskWithAStoredPassword_NeedsNoPassword()
    {
        var editor = Edit(Elaborate() with { Account = new TaskAccount(TaskAccountKind.User, @"CORP\svc"), StoresPassword = true });

        editor.AccountIndex = 0;

        Assert.False(editor.NeedsPassword);
    }

    [Fact]
    public async Task SavingANewTask_CreatesItInTheFolder_AndRemembersItsCategoryAndTags()
    {
        var editor = Edit(NewTask.Draft(_time.GetLocalNow().DateTime), path: null);
        editor.Name = "Report";
        editor.Category = "Finance";
        editor.Tags = "monthly, pdf ,, monthly";
        editor.SelectedAction!.Path = "report.exe";

        Assert.True(await editor.SaveAsync());

        Assert.Equal(@"\Jobs\Report", editor.SavedPath);
        TaskDraft saved = _store.Load(@"\Jobs\Report");
        Assert.Equal(["monthly", "pdf"], saved.Tags);
        Assert.Equal("report.exe", saved.Actions.Single().Path);
        Assert.Contains("Finance", _settings.Current.SavedCategories);
        Assert.Contains("pdf", _settings.Current.SavedTags);
        Assert.False(editor.HasProblems);
        Assert.Empty(_snooze.Calls);
    }

    [Fact]
    public async Task RenamingATask_TakesItsSnoozeAlong()
    {
        _store.Add(@"\Jobs\Nightly");
        var editor = Edit(Elaborate());
        editor.Name = "Daily";

        Assert.True(await editor.SaveAsync());

        Assert.Equal(@"\Jobs\Daily", editor.SavedPath);
        Assert.Equal([@"TaskMoved \Jobs\Nightly -> \Jobs\Daily"], _snooze.Calls);
    }

    [Fact]
    public async Task SavingATaskUnderItsName_LeavesItsSnoozeAlone()
    {
        _store.Add(@"\Jobs\Nightly");
        var editor = Edit(Elaborate());
        editor.Description = "Copies the week's work";

        Assert.True(await editor.SaveAsync());

        Assert.Empty(_snooze.Calls);
    }

    [Fact]
    public async Task ARenameWindowsRefuses_LeavesTheSnoozeWithTheTask()
    {
        _store.Add(@"\Jobs\Nightly").Protected.Add(@"\Jobs\Nightly");
        var editor = Edit(Elaborate());
        editor.Name = "Daily";

        Assert.False(await editor.SaveAsync());

        Assert.Empty(_snooze.Calls);
    }

    [Fact]
    public async Task WhatIsMissing_IsListed_AndNothingIsSaved()
    {
        var editor = Edit(new TaskDraft(), path: null);

        Assert.False(await editor.SaveAsync());

        Assert.Contains("Dialog.Error.EmptyName", editor.Problems);
        Assert.Contains("Editor.Error.NoAction", editor.Problems);
        Assert.True(editor.HasProblems);
        Assert.Empty(_store.Calls);
        Assert.Null(editor.SavedPath);
    }

    [Theory]
    [InlineData(true, "Editor.Error.AccessDenied")]
    [InlineData(false, "Editor.Error.AccessDeniedNotElevated")]
    public async Task WhenWindowsRefusesTheChange_TheEditorSaysSo_AndWhetherElevationWouldHelp(bool elevated, string expected)
    {
        _app.IsElevated = elevated;
        _store.Add(@"\Jobs\Nightly").Protected.Add(@"\Jobs\Nightly");
        var editor = Edit(Elaborate() with { DeleteWhenExpired = false });

        Assert.False(await editor.SaveAsync());

        Assert.Equal(expected, editor.Problems);
    }

    [Fact]
    public async Task AnAccountWindowsDoesNotAccept_IsReportedWithItsReason()
    {
        _store.Add(@"\Jobs\Nightly");
        _store.Before = (_, _) => throw new TaskAccountException("The user name or password is incorrect.", new InvalidOperationException());
        var editor = Edit(Elaborate());

        Assert.False(await editor.SaveAsync());

        Assert.Contains("Editor.Error.Account", editor.Problems);
    }

    [Theory]
    [InlineData(false, 2, false, true)]   // SYSTEM
    [InlineData(false, 0, true, true)]    // highest privileges
    [InlineData(false, 0, false, false)]
    [InlineData(true, 2, true, false)]
    public void WhatNeedsAdministratorRights_IsFlaggedWhenTheAppHasNone(bool elevated, int accountIndex, bool highest, bool expected)
    {
        _app.IsElevated = elevated;
        var editor = Edit(new TaskDraft());

        editor.AccountIndex = accountIndex;
        editor.HighestPrivileges = highest;

        Assert.Equal(expected, editor.NeedsElevation);
    }

    [Fact]
    public void ANewTrigger_StartsAtTheNextFullHour_AndIsSelected()
    {
        var editor = Edit(new TaskDraft());

        editor.AddTriggerCommand.Execute(null);

        var trigger = Assert.Single(editor.Triggers);
        Assert.Same(trigger, editor.SelectedTrigger);
        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(new DateTime(2031, 5, 6, 15, 0, 0), built.Start);
        Assert.Null(built.SourceIndex);
    }

    [Fact]
    public void ATriggerWithoutAStart_GivenATimeOfDay_StartsAtTheNextFullHourOfTheEditorsClock()
    {
        var editor = Edit(Elaborate() with { Triggers = [new TriggerDraft { Type = TriggerType.AtLogon, SourceIndex = 0 }] });
        TriggerViewModel trigger = editor.Triggers[0];

        trigger.TypeIndex = TriggerViewModel.Types.ToList().IndexOf(TriggerType.Daily);

        Assert.True(trigger.TryBuild(out TriggerDraft built, out _));
        Assert.Equal(new DateTime(2031, 5, 6, 15, 0, 0), built.Start);
    }

    [Fact]
    public void TriggersCanBeReorderedAndRemoved()
    {
        var editor = Edit(Elaborate());
        var (weekly, registration) = (editor.Triggers[0], editor.Triggers[1]);
        Assert.Same(weekly, editor.SelectedTrigger);

        editor.MoveTriggerUpCommand.Execute(null);
        Assert.Equal([weekly, registration], editor.Triggers);
        Assert.Same(weekly, editor.SelectedTrigger);

        editor.MoveTriggerDownCommand.Execute(null);
        Assert.Equal([registration, weekly], editor.Triggers);
        Assert.Same(weekly, editor.SelectedTrigger);

        editor.RemoveTriggerCommand.Execute(null);
        Assert.Equal([registration], editor.Triggers);
        Assert.Same(registration, editor.SelectedTrigger);

        editor.RemoveTriggerCommand.Execute(null);
        Assert.Empty(editor.Triggers);
        Assert.False(editor.HasSelectedTrigger);
        Assert.False(editor.RemoveTriggerCommand.CanExecute(null));
    }

    [Fact]
    public async Task ActionsCanBeAdded_Browsed_Reordered_AndRemoved()
    {
        var editor = Edit(new TaskDraft { Name = "T" }, path: null);

        editor.AddActionCommand.Execute(null);
        _files.Choice = @"C:\Tools\sync.exe";
        await editor.BrowseForProgramCommand.ExecuteAsync(null);
        editor.AddMessageActionCommand.Execute(null);
        editor.AddEmailActionCommand.Execute(null);

        Assert.Equal(3, editor.Actions.Count);
        Assert.Equal(@"C:\Tools\sync.exe", editor.Actions[0].Path);
        Assert.Contains("MessageBox", editor.Actions[1].Arguments);
        Assert.Contains("Send-MailMessage", editor.Actions[2].Arguments);

        editor.MoveActionUpCommand.Execute(null);
        Assert.Contains("Send-MailMessage", editor.Actions[1].Arguments);
        editor.RemoveActionCommand.Execute(null);
        Assert.Equal(2, editor.Actions.Count);

        Assert.True(editor.TryBuild(out TaskDraft built, out _));
        Assert.Equal([@"C:\Tools\sync.exe", "powershell.exe"], built.Actions.Select(action => action.Path));
    }

    [Fact]
    public async Task CancellingTheFilePicker_LeavesTheProgramAlone()
    {
        var editor = Edit(new TaskDraft { Actions = [new ActionDraft { Path = "old.exe" }] });
        _files.Choice = null;

        await editor.BrowseForProgramCommand.ExecuteAsync(null);

        Assert.Equal("old.exe", editor.SelectedAction!.Path);
    }

    [Fact]
    public void TheNetworkCondition_OffersAnyNetworkAndTheKnownOnes()
    {
        var editor = Edit(new TaskDraft { Name = "T", Actions = [new ActionDraft { Path = "a.exe" }] });
        Assert.Equal(["Main.Network.Any", "Home"], editor.NetworkOptions);
        Assert.Equal(0, editor.NetworkIndex);

        editor.RunOnlyIfNetwork = true;
        editor.NetworkIndex = 1;
        Assert.True(editor.TryBuild(out TaskDraft specific, out _));
        Assert.Equal((HomeNetwork, "Home"), (specific.NetworkId, specific.NetworkName));

        editor.RunOnlyIfNetwork = false;
        Assert.True(editor.TryBuild(out TaskDraft none, out _));
        Assert.Equal(Guid.Empty, none.NetworkId);
    }

    [Fact]
    public void ANetworkKeptWhileTheNetworkConditionIsOff_StaysWithTheTask()
    {
        TaskDraft loaded = Elaborate() with { RunOnlyIfNetwork = false };

        Assert.True(Edit(loaded).TryBuild(out TaskDraft built, out _));

        Assert.Equal(loaded, built);
    }

    [Fact]
    public void ANetworkTheMachineDoesNotList_StaysSelectable_SoTheTaskKeepsIt()
    {
        var elsewhere = Guid.NewGuid();
        var editor = Edit(Elaborate() with { NetworkId = elsewhere, NetworkName = "Office" });

        Assert.Equal(["Main.Network.Any", "Home", "Office"], editor.NetworkOptions);
        Assert.Equal(2, editor.NetworkIndex);
    }

    [Fact]
    public void RestartOnFailure_IsACountAndAnInterval()
    {
        var editor = Edit(new TaskDraft { Name = "T", Actions = [new ActionDraft { Path = "a.exe" }] });
        Assert.False(editor.RestartOnFailure);

        editor.RestartOnFailure = true;
        editor.RestartCount = 5;
        editor.RestartInterval = "2m";
        Assert.True(editor.TryBuild(out TaskDraft on, out _));
        Assert.Equal((5, TimeSpan.FromMinutes(2)), (on.RestartCount, on.RestartInterval));

        editor.RestartOnFailure = false;
        Assert.True(editor.TryBuild(out TaskDraft off, out _));
        Assert.Equal(0, off.RestartCount);
    }

    [Theory]
    [InlineData("idle", "Dialog.Error.InvalidIdleDuration")]
    [InlineData("restart", "Dialog.Error.InvalidRestartInterval")]
    [InlineData("limit", "Dialog.Error.InvalidStopAfter")]
    public void ADurationThatCannotBeRead_IsReportedForItsField(string field, string expected)
    {
        var editor = Edit(Elaborate());
        if (field == "idle") editor.IdleDuration = "a bit";
        if (field == "restart") editor.RestartInterval = "soon";
        if (field == "limit") editor.TimeLimit = "forever";

        Assert.False(editor.TryBuild(out _, out var problems));
        Assert.Equal([expected], problems.Select(problem => problem.Key));
    }

    [Fact]
    public void AnEmptyRunTimeLimit_IsListedAlongWithTheOtherProblems()
    {
        var editor = Edit(Elaborate());
        editor.TimeLimit = "";
        editor.IdleDuration = "a bit";

        Assert.False(editor.TryBuild(out _, out var problems));

        Assert.Equal(["Dialog.Error.InvalidIdleDuration", "Dialog.Error.InvalidStopAfter"], problems.Select(problem => problem.Key));
    }

    [Fact]
    public void AProblemInATrigger_NamesTheTrigger()
    {
        var editor = Edit(Elaborate());
        editor.AddTriggerCommand.Execute(null);
        editor.SelectedTrigger!.RepetitionInterval = "often";

        Assert.False(editor.TryBuild(out _, out var problems));
        Assert.Equal(new DraftProblem("Editor.Error.RepetitionText", "3"), Assert.Single(problems));
    }

    [Fact]
    public void TheTaskCannotStartItselfWhenItFinishes()
    {
        var editor = Edit(Elaborate(), @"\Jobs\Nightly", @"\Jobs\Nightly", @"\Jobs\Report", @"\Other");

        Assert.Equal([@"\Jobs\Report", @"\Other"], editor.CompletionActions.OtherTasks);
    }

    [Fact]
    public void TheTitle_SaysWhetherATaskIsCreatedOrChanged()
    {
        Assert.True(Edit(new TaskDraft(), path: null).IsNew);
        Assert.Equal("Editor.Title.New", Edit(new TaskDraft(), path: null).Title);
        Assert.False(Edit(Elaborate()).IsNew);
    }
}

/// <summary>
/// What the editor says when Task Scheduler turns a save down, with the names filled in. The texts
/// are installed in <see cref="Loc"/>, so these tests run apart from the others.
/// </summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class TaskEditorSaveFailureTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTaskStore _store = new();
    private readonly FakeTimeProvider _time = new();
    private readonly SettingsStore _settings;

    public TaskEditorSaveFailureTests()
    {
        Loc.Use(key => key switch
        {
            "Editor.Error.NameTaken" => "{0} is taken",
            "Tasks.Error.Gone" => "{0} is gone",
            "Tasks.Error.Duplicated" => "{0} and {1} both exist",
            _ => null
        });
        _settings = new SettingsStore(_folder.Paths, _time);
    }

    public void Dispose()
    {
        _settings.Dispose();
        _folder.Dispose();
        Loc.Use(static _ => null);
    }

    private TaskEditorViewModel Edit(string name, string? path) =>
        new TaskEditorFactory(_store, _settings, new FakeAppLifetime(), new ScriptedFilePicker(), new FakeNetworkProfiles(), _time, new FakeSnoozeService())
            .Create(new TaskDraft { Name = name, Actions = [new ActionDraft { SourceIndex = 0, Path = "nightly.exe" }] }, @"\Jobs", path, []);

    [Fact]
    public async Task ANameAnotherTaskInTheFolderHas_IsReportedAsTaken()
    {
        _store.Add(@"\Jobs\Report");
        var editor = Edit("Report", path: null);

        Assert.False(await editor.SaveAsync());

        Assert.Equal("Report is taken", editor.Problems);
    }

    [Theory]
    [InlineData("Nightly")]
    [InlineData("Daily")]
    public async Task ATaskDeletedWhileItWasBeingEdited_IsReportedGone_UnderTheNameItWasOpenedWith(string savedAs)
    {
        var editor = Edit("Nightly", @"\Jobs\Nightly");
        editor.Name = savedAs;

        Assert.False(await editor.SaveAsync());

        Assert.Equal("Nightly is gone", editor.Problems);
    }

    [Fact]
    public async Task ARenameThatLeftTheTaskTwice_SaysWhereBothAre()
    {
        _store.Add(@"\Jobs\Nightly").Before = (operation, _) =>
        {
            if (operation == "Save") throw new TaskDuplicatedException(@"\Jobs\Nightly", @"\Jobs\Daily", new UnauthorizedAccessException());
        };
        var editor = Edit("Nightly", @"\Jobs\Nightly");
        editor.Name = "Daily";

        Assert.False(await editor.SaveAsync());

        Assert.Equal(@"\Jobs\Nightly and \Jobs\Daily both exist", editor.Problems);
    }
}
