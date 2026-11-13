using System.Text.RegularExpressions;
using FluentTaskScheduler.Tests.Library;
using FluentTaskScheduler.Tests.Processes;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class QuickActionsViewModelTests
{
    private readonly FakeProcessRunner _runner = new();
    private readonly ScriptedPrompts _prompts = new();

    private QuickActionsViewModel NewPage(bool elevated, FakeAppLifetime? lifetime = null) =>
        new(_runner, lifetime ?? new FakeAppLifetime { IsElevated = elevated }, new InlineUiThread(), _prompts);

    private QuickActionViewModel Action(string id, bool elevated = true) => NewPage(elevated).Actions.Single(action => action.Id == id);

    [Fact]
    public void WithoutAdministratorRights_ActionsThatNeedThemCannotRun_AndARestartIsOffered()
    {
        var lifetime = new FakeAppLifetime { IsElevated = false };
        QuickActionsViewModel page = NewPage(elevated: false, lifetime);

        Assert.Equal(["IPReleaseRenew", "SfcScannow", "DismHealth"], page.Actions.Where(action => action.RequiresElevation).Select(action => action.Id));
        Assert.All(page.Actions, action =>
        {
            Assert.Equal(!action.RequiresElevation, action.IsAvailable);
            Assert.Equal(action.IsAvailable, action.RunCommand.CanExecute(null));
        });
        Assert.True(page.OffersRestartAsAdministrator);

        page.RestartAsAdministratorCommand.Execute(null);

        Assert.True(lifetime.RestartElevatedRequested);
    }

    [Fact]
    public void WithAdministratorRights_EveryActionCanRun_AndNoRestartIsOffered()
    {
        QuickActionsViewModel page = NewPage(elevated: true);

        Assert.All(page.Actions, action => Assert.True(action.RunCommand.CanExecute(null)));
        Assert.False(page.OffersRestartAsAdministrator);
    }

    [Fact]
    public async Task WhileAnActionRuns_ItShowsWhatTheProgramWroteLast_AndAfterwardsItsFindings()
    {
        QuickActionViewModel action = Action("FlushDNS");

        Task run = action.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("Windows IP Configuration").Writes("").Writes("  Successfully flushed the DNS Resolver Cache.  ");

        Assert.Equal(QuickAction.All.Single(definition => definition.Id == "FlushDNS").Request, _runner.Last.Request);
        Assert.Equal((QuickActionState.Running, "QuickActions.Status.Running", "Successfully flushed the DNS Resolver Cache."), (action.State, action.StatusText, action.Detail));
        Assert.True(action.IsRunning);
        Assert.False(action.RunCommand.CanExecute(null));
        Assert.True(action.CancelCommand.CanExecute(null));

        _runner.Last.Exits();
        await run;

        Assert.Equal(
            (QuickActionState.Succeeded, "QuickActions.Status.Success", "Windows IP Configuration\nSuccessfully flushed the DNS Resolver Cache."),
            (action.State, action.StatusText, action.Detail));
        Assert.True(action.HasSucceeded);
        Assert.True(action.RunCommand.CanExecute(null));
        Assert.False(action.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheProgressOfAProgram_IsOneLineThatChanges_AndIsNotCountedAmongItsFindings()
    {
        QuickActionViewModel action = Action("SfcScannow");
        Task run = action.RunCommand.ExecuteAsync(null);

        _runner.Last.Writes("Beginning verification phase of system scan.").WritesUnfinished("Verification 12% complete.");
        Assert.Equal("Verification 12% complete.", action.Detail);

        _runner.Last.WritesUnfinished("Verification 57% complete.");
        Assert.Equal("Verification 57% complete.", action.Detail);

        _runner.Last.Writes("Verification 100% complete.").Writes("").Writes("Windows Resource Protection did not find any integrity violations.").Exits();
        await run;

        Assert.Equal(
            "Beginning verification phase of system scan.\nVerification 100% complete.\nWindows Resource Protection did not find any integrity violations.",
            action.Detail);
    }

    [Fact]
    public async Task AnActionWhoseOutputIsNotAFinding_ShowsOnlyThatItSucceeded()
    {
        QuickActionViewModel action = Action("IPReleaseRenew");

        Task run = action.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("Ethernet adapter Ethernet:").Writes("Default Gateway . . . : 192.168.1.1").Exits();
        await run;

        Assert.Equal((QuickActionState.Succeeded, ""), (action.State, action.Detail));
        Assert.False(action.HasDetail);
    }

    [Fact]
    public async Task AFailedAction_ShowsThatItFailed_AndTheLastThingsTheProgramSaid()
    {
        QuickActionViewModel action = Action("IPReleaseRenew");

        Task run = action.RunCommand.ExecuteAsync(null);
        for (int line = 1; line <= 6; line++) _runner.Last.Writes($"line {line}");
        _runner.Last.WritesError("unable to contact your DHCP server").Exits(1);
        await run;

        Assert.Equal(
            (QuickActionState.Failed, "QuickActions.Status.ExitCode", "line 4\nline 5\nline 6\nunable to contact your DHCP server"),
            (action.State, action.StatusText, action.Detail));
        Assert.True(action.HasFailed);
    }

    [Fact]
    public async Task AProgramThatCannotBeStarted_IsAFailure_WithTheReasonWindowsGave()
    {
        QuickActionViewModel action = Action("FlushDNS");
        _runner.StartFailure = new InvalidOperationException("The system cannot find the file specified.");

        await action.RunCommand.ExecuteAsync(null);

        Assert.Equal(
            (QuickActionState.Failed, "QuickActions.Status.Error", "The system cannot find the file specified."),
            (action.State, action.StatusText, action.Detail));
    }

    [Fact]
    public async Task Cancel_EndsTheProgram_AndTheActionCanBeRunAgain()
    {
        QuickActionViewModel action = Action("SfcScannow");
        Task run = action.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("Verification 12% complete.");

        action.CancelCommand.Execute(null);
        await run;

        Assert.True(_runner.Last.WasStopped);
        Assert.Equal((QuickActionState.Idle, "QuickActions.Status.Cancelled", ""), (action.State, action.StatusText, action.Detail));
        Assert.True(action.RunCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("ClearTemp")]
    [InlineData("RestartExplorer")]
    public async Task AnActionThatDeletesOrInterrupts_AsksFirst_AndDoesNothingWhenTheAnswerIsNo(string id)
    {
        QuickActionViewModel action = Action(id);
        _prompts.Confirm = false;

        await action.RunCommand.ExecuteAsync(null);

        Assert.Equal([$"QuickActions.{id}.Title: QuickActions.{id}.Confirm [QuickActions.RunBtn]"], _prompts.Shown);
        Assert.Empty(_runner.Started);
        Assert.Equal(QuickActionState.Idle, action.State);

        _prompts.Confirm = true;
        Task run = action.RunCommand.ExecuteAsync(null);

        Assert.Single(_runner.Started);
        _runner.Last.Exits();
        await run;
    }

    [Fact]
    public async Task OtherActions_RunWithoutAsking()
    {
        foreach (QuickActionViewModel action in NewPage(elevated: true).Actions.Where(action => action.Id is not ("ClearTemp" or "RestartExplorer")))
        {
            Task run = action.RunCommand.ExecuteAsync(null);
            _runner.Last.Exits();
            await run;
        }

        Assert.Empty(_prompts.Shown);
        Assert.Equal(4, _runner.Started.Count);
    }

    [Fact]
    public async Task ClosingThePage_EndsWhatIsStillRunning()
    {
        QuickActionsViewModel page = NewPage(elevated: true);
        Task run = page.Actions.Single(action => action.Id == "SfcScannow").RunCommand.ExecuteAsync(null);

        page.Dispose();
        await run;

        Assert.True(_runner.Last.WasStopped);
    }

    [Theory]
    [MemberData(nameof(RepoFiles.Languages), MemberType = typeof(RepoFiles))]
    public void EveryAction_HasItsTexts_InEveryLanguage(string language)
    {
        var texts = RepoFiles.Texts(language);

        var needed = QuickAction.All.SelectMany(action => action.ConfirmFirst
            ? [$"QuickActions.{action.Id}.Title", $"QuickActions.{action.Id}.Desc", $"QuickActions.{action.Id}.Confirm"]
            : new[] { $"QuickActions.{action.Id}.Title", $"QuickActions.{action.Id}.Desc" });

        string[] missing = needed.Where(name => !texts.ContainsKey(name)).ToArray();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(QuickAction.ClearTempScript)]
    [InlineData(QuickAction.RestartExplorerScript)]
    public void PowerShellOfAnAction_FitsIntoThePairOfQuotesItTravelsIn(string script)
    {
        Assert.DoesNotContain('"', script);
        // cmd.exe replaces %NAME% even inside quotes.
        Assert.DoesNotContain('%', script);
        Assert.Contains(QuickAction.All, action => action.Request.Arguments.Contains($"-Command \"{script}\""));
    }

    [SkippableTheory]
    [InlineData(QuickAction.ClearTempScript)]
    [InlineData(QuickAction.RestartExplorerScript)]
    public async Task PowerShellOfAnAction_IsAcceptedByWindowsPowerShell(string script)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);

        Assert.Empty(await WindowsPowerShell.SyntaxErrorsInAsync(script));
    }

    [SkippableFact]
    public void EveryProgramAnActionNames_IsNamedByItsFullPath_AndExists()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);

        foreach (QuickAction action in QuickAction.All)
        {
            var programs = Regex.Matches(action.Request.Arguments, "\"([^\"]+\\.(?:exe|com))\"").Select(match => match.Groups[1].Value).Prepend(action.Request.FileName);

            Assert.All(programs, program => Assert.True(Path.IsPathFullyQualified(program) && File.Exists(program), $"{action.Id}: {program}"));
        }
    }
}
