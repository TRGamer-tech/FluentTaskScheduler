using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;
using Xunit.Abstractions;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>Exercises <see cref="TaskStore"/> against the real Task Scheduler.</summary>
[Collection(SchedulerSandbox.Collection)]
public class TaskStoreTests
{
    private const string ForeignSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private readonly ITestOutputHelper _out;
    private readonly TaskStore _store = new();

    public TaskStoreTests(ITestOutputHelper output) => _out = output;

    private static TaskDraft NewDraft(string name) => new()
    {
        Name = name,
        Description = "Created by a test",
        Triggers = new[] { new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2031, 1, 1, 8, 0, 0) } },
        Actions = new[] { new ActionDraft { Path = "cmd.exe", Arguments = "/c exit 0" } }
    };

    [SkippableFact]
    public void ANewTask_IsListedAndLoadsBackAsSaved()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        string path = _store.Save(NewDraft("Fresh") with { Category = "Work", Tags = new[] { "one", "two" } }, sandbox.FolderPath);

        Assert.Equal(sandbox.PathOf("Fresh"), path);
        Assert.True(_store.Exists(path));

        var row = Assert.Single(_store.GetTasks(), r => r.Path == path);
        Assert.Equal("Fresh", row.Name);
        Assert.Equal(sandbox.FolderPath, row.FolderPath);
        Assert.Equal("Created by a test", row.Description);
        Assert.Equal("Work", row.Category);
        Assert.Equal(new[] { "one", "two" }, row.Tags);
        Assert.True(row.IsEnabled);
        Assert.Null(row.LoadError);
        Assert.NotNull(row.NextRunTime);
        Assert.Null(row.LastRunTime); // it has never run

        var draft = _store.Load(path);
        Assert.Equal("Fresh", draft.Name);
        Assert.Equal(TaskAccount.CurrentUser, draft.Account);
        Assert.Equal(TriggerType.Daily, Assert.Single(draft.Triggers).Type);
        Assert.Equal("cmd.exe", Assert.Single(draft.Actions).Path);
    }

    [SkippableFact]
    public void OneTask_IsReadTheWayTheListShowsIt_AndATaskThatDoesNotExistIsNull()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string path = _store.Save(NewDraft("One") with { Category = "Work", Tags = new[] { "one" } }, sandbox.FolderPath);

        var listed = Assert.Single(_store.GetTasks(), r => r.Path == path);
        var row = _store.GetTask(path);

        Assert.NotNull(row);
        Assert.Equal(path, row.Path);
        Assert.Equal(listed.State, row.State);
        Assert.Equal(listed.IsEnabled, row.IsEnabled);
        Assert.Equal(listed.LastRunTime, row.LastRunTime);
        Assert.Equal(listed.NextRunTime, row.NextRunTime);
        Assert.Equal("Work", row.Category);
        Assert.Equal(new[] { "one" }, row.Tags);
        Assert.Null(row.LoadError);

        Assert.Null(_store.GetTask(sandbox.PathOf("Missing")));
    }

    [SkippableFact]
    public void TasksCreatedByWindowsTools_AreListedAndCanBeLoaded()
    {
        // Issue #29: tasks made outside the app must show up.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        _out.WriteLine(Run("schtasks.exe", $"/create /tn \"{sandbox.PathOf("ByCli")}\" /tr \"cmd.exe /c exit 0\" /sc daily /st 09:00 /f"));
        _out.WriteLine(Run("schtasks.exe", $"/create /tn \"{sandbox.PathOf("ByCliSystem")}\" /tr \"cmd.exe /c exit 0\" /sc onstart /ru SYSTEM /f"));
        _out.WriteLine(Run("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Register-ScheduledTask -TaskName ByPowerShell -TaskPath '" + sandbox.FolderPath + "\\' " +
            "-Action (New-ScheduledTaskAction -Execute cmd.exe -Argument '/c exit 0') " +
            "-Trigger (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Friday -At 7am) | Out-Null\""));

        var rows = _store.GetTasks().Where(r => r.FolderPath == sandbox.FolderPath).ToList();
        Assert.Equal(new[] { "ByCli", "ByCliSystem", "ByPowerShell" }, rows.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(rows, r => Assert.Null(r.LoadError));

        var daily = Assert.Single(_store.Load(sandbox.PathOf("ByCli")).Triggers);
        Assert.Equal(TriggerType.Daily, daily.Type);
        Assert.Equal(new TimeSpan(9, 0, 0), daily.Start!.Value.TimeOfDay);

        Assert.Equal(TaskAccount.System, _store.Load(sandbox.PathOf("ByCliSystem")).Account);

        var weekly = Assert.Single(_store.Load(sandbox.PathOf("ByPowerShell")).Triggers);
        Assert.Equal(TriggerType.Weekly, weekly.Type);
        Assert.Equal(DaysOfTheWeek.Monday | DaysOfTheWeek.Friday, weekly.DaysOfWeek);
    }

    [SkippableFact]
    public void Renaming_ReplacesTheTaskInsteadOfDuplicatingIt()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string oldPath = _store.Save(NewDraft("Before"), sandbox.FolderPath);

        string newPath = _store.Save(_store.Load(oldPath) with { Name = "After" }, sandbox.FolderPath, oldPath);

        Assert.Equal(sandbox.PathOf("After"), newPath);
        Assert.False(_store.Exists(oldPath));
        Assert.True(_store.Exists(newPath));
        Assert.Equal("Created by a test", _store.Load(newPath).Description);
    }

    [SkippableFact]
    public void ChangingOnlyTheCaseOfItsName_RenamesTheTask()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string oldPath = _store.Save(NewDraft("backup"), sandbox.FolderPath);

        string newPath = _store.Save(_store.Load(oldPath) with { Name = "Backup" }, sandbox.FolderPath, oldPath);

        Assert.Equal(sandbox.PathOf("Backup"), newPath);
        var row = Assert.Single(_store.GetTasks(), r => r.FolderPath == sandbox.FolderPath);
        Assert.Equal("Backup", row.Name);
        Assert.Equal("Created by a test", row.Description);
    }

    [SkippableFact]
    public void ANewTask_NeverReplacesATaskOfTheSameName()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string path = _store.Save(NewDraft("Twin"), sandbox.FolderPath);
        string before = _store.Export(path);

        var refused = Assert.Throws<TaskExistsException>(() => _store.Save(NewDraft("Twin") with { Description = "The second one" }, sandbox.FolderPath));

        Assert.Equal(path, refused.Path);
        Assert.Empty(XmlDiff.Diff(before, _store.Export(path), "Date", "URI"));
    }

    [SkippableFact]
    public void RenamingATaskOntoAnother_IsRefused_AndBothStayAsTheyWere()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string backup = _store.Save(NewDraft("Backup") with { Description = "Backs up" }, sandbox.FolderPath);
        string cleanup = _store.Save(NewDraft("Cleanup") with { Description = "Cleans up" }, sandbox.FolderPath);

        Assert.Throws<TaskExistsException>(() => _store.Save(_store.Load(cleanup) with { Name = "Backup" }, sandbox.FolderPath, cleanup));

        Assert.Equal("Backs up", _store.Load(backup).Description);
        Assert.Equal("Cleans up", _store.Load(cleanup).Description);
    }

    [SkippableFact]
    public void ImportingUnderTheNameOfAnExistingTask_IsRefused_AndLeavesThatTaskAlone()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string path = _store.Save(NewDraft("Backup"), sandbox.FolderPath);
        string before = _store.Export(path);
        string other = _store.Export(_store.Save(NewDraft("Other") with { Description = "Something else" }, sandbox.FolderPath));

        Assert.Throws<TaskExistsException>(() => _store.Import(other, new TaskImportOptions(sandbox.FolderPath, "Backup")));

        Assert.Empty(XmlDiff.Diff(before, _store.Export(path), "Date", "URI"));
    }

    [SkippableTheory]
    [InlineData("SYSTEM", TaskLogonType.ServiceAccount)]
    [InlineData(@"NT AUTHORITY\LOCAL SERVICE", TaskLogonType.ServiceAccount)]
    [InlineData(@"NT AUTHORITY\NETWORK SERVICE", TaskLogonType.ServiceAccount)]
    [InlineData(@"BUILTIN\Users", TaskLogonType.Group)]
    [InlineData(null, TaskLogonType.S4U)]
    [InlineData(null, TaskLogonType.InteractiveToken)]
    public void Editing_KeepsTheAccountTheTaskRunsAs(string? userId, TaskLogonType logonType)
    {
        // Issue #28: saving used to re-register every task for an interactive user.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        userId ??= TaskAccounts.CurrentUserName;
        var task = sandbox.Folder.RegisterTaskDefinition("Owned", SampleTasks.Simple(sandbox.Service), TaskCreation.CreateOrUpdate, userId, null, logonType);
        string before = task.Xml;

        _store.Save(_store.Load(task.Path) with { Hidden = true }, sandbox.FolderPath, task.Path);

        var diff = XmlDiff.Diff(before, sandbox.Service.GetTask(task.Path).Xml, "Date", "URI");
        foreach (string line in diff) _out.WriteLine(line);
        Assert.All(diff, line => Assert.Contains("/Task/Settings/Hidden", line));
        Assert.NotEmpty(diff);
    }

    [SkippableFact]
    public void ChangingTheAccount_ReRegistersTheTaskForIt()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string path = _store.Save(NewDraft("Mine"), sandbox.FolderPath);

        _store.Save(_store.Load(path) with { Account = TaskAccount.System }, sandbox.FolderPath, path);
        Assert.Equal(TaskAccount.System, _store.Load(path).Account);
        Assert.Equal(TaskLogonType.ServiceAccount, sandbox.Service.GetTask(path).Definition.Principal.LogonType);

        _store.Save(_store.Load(path) with { Account = TaskAccount.CurrentUser }, sandbox.FolderPath, path);
        Assert.Equal(TaskAccount.CurrentUser, _store.Load(path).Account);
        Assert.Equal(TaskLogonType.InteractiveToken, sandbox.Service.GetTask(path).Definition.Principal.LogonType);
    }

    [SkippableFact]
    public void EnablingRunningStoppingAndDeleting_Work()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var draft = NewDraft("Runner") with
        {
            Actions = new[] { new ActionDraft { Path = "cmd.exe", Arguments = "/c ping -n 30 127.0.0.1 > nul" } }
        };
        string path = _store.Save(draft, sandbox.FolderPath);

        _store.SetEnabled(path, false);
        Assert.Equal(TaskRowState.Disabled, _store.GetStates(new[] { path })[path]);
        _store.SetEnabled(path, true);
        Assert.Equal(TaskRowState.Ready, _store.GetStates(new[] { path })[path]);

        _store.Run(path);
        Assert.True(WaitFor(() => _store.GetStates(new[] { path })[path] == TaskRowState.Running), "The task did not start.");
        Assert.True(_store.CountRunningTasks() >= 1, "The running task was not counted.");
        _store.Stop(path);
        Assert.True(WaitFor(() => _store.GetStates(new[] { path })[path] != TaskRowState.Running), "The task did not stop.");

        _store.Delete(path);
        Assert.False(_store.Exists(path));
        Assert.Empty(_store.GetStates(new[] { path }));
    }

    [SkippableFact]
    public void ATaskDisabledWhileItRuns_IsReadAsDisabled()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var draft = NewDraft("Sleeper") with
        {
            Actions = new[] { new ActionDraft { Path = "cmd.exe", Arguments = "/c ping -n 30 127.0.0.1 > nul" } }
        };
        string path = _store.Save(draft, sandbox.FolderPath);
        Assert.True(_store.GetTask(path)?.IsEnabled);

        _store.Run(path);
        Assert.True(WaitFor(() => _store.GetStates(new[] { path })[path] == TaskRowState.Running), "The task did not start.");
        _store.SetEnabled(path, false);

        // The state is of no help while the task runs, which is why snoozing goes by IsEnabled.
        var row = _store.GetTask(path);
        _out.WriteLine($"State of a running task after it was disabled: {row?.State}");
        Assert.False(row?.IsEnabled);

        _store.Stop(path);
        _store.SetEnabled(path, true);
        Assert.True(_store.GetTask(path)?.IsEnabled);
    }

    [SkippableFact]
    public void AMissingTask_IsReportedAsSuch()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string path = sandbox.PathOf("Nope");

        Assert.Equal(path, Assert.Throws<TaskNotFoundException>(() => _store.Load(path)).Path);
        Assert.Throws<TaskNotFoundException>(() => _store.Run(path));
        Assert.Throws<TaskNotFoundException>(() => _store.Delete(path));
        Assert.Throws<TaskNotFoundException>(() => _store.SetEnabled(path, true));
        Assert.False(_store.Exists(path));
    }

    [SkippableFact]
    public void ExportThenImport_RecreatesTheTask()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = sandbox.Folder.RegisterTaskDefinition("Rich", SampleTasks.Rich(sandbox.Service), TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);

        string xml = _store.Export(task.Path);
        var info = _store.Inspect(xml);
        string copy = _store.Import(xml, new TaskImportOptions(sandbox.FolderPath, "Copy"));

        Assert.Equal(TaskAccount.System, info.Account);
        Assert.True(info.AccountExistsHere);
        Assert.True(info.HighestPrivileges);
        Assert.False(info.StoresPassword);
        Assert.Equal(task.Definition.Triggers.Count, info.Triggers.Count);
        Assert.Equal(["/c exit 0", "/c exit 1"], info.Actions.Select(action => action.Arguments));
        Assert.Empty(XmlDiff.Diff(xml, _store.Export(copy), "Date", "URI"));
    }

    [SkippableFact]
    public void ATaskExportedOnAnotherMachine_CanBeImportedForTheCurrentUser()
    {
        // Issue #30: the user and SIDs in the file do not exist here.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        var definition = SampleTasks.Simple(sandbox.Service);
        definition.Triggers.Add(new LogonTrigger { UserId = TaskAccounts.CurrentUserName });
        definition.Principal.RunLevel = TaskRunLevel.Highest;
        var source = sandbox.Folder.RegisterTaskDefinition("Source", definition);
        string xml = Regex.Replace(source.Xml, "<UserId>[^<]*</UserId>", $"<UserId>{ForeignSid}</UserId>");
        _out.WriteLine(xml);

        var info = _store.Inspect(xml);
        Assert.False(info.AccountExistsHere);
        Assert.Equal(new TaskAccount(TaskAccountKind.User, ForeignSid), info.Account);
        Assert.True(info.HighestPrivileges);

        var asIs = Record.Exception(() => _store.Import(xml, new TaskImportOptions(sandbox.FolderPath, "AsIs")));
        _out.WriteLine("Importing unchanged: " + (asIs is null ? "succeeded" : $"{asIs.GetType().Name}: {asIs.Message} / {asIs.InnerException?.GetType().Name} 0x{asIs.InnerException?.HResult:X8}"));
        Assert.NotNull(asIs);

        string path = _store.Import(xml, new TaskImportOptions(sandbox.FolderPath, "ForMe", TaskAccount.CurrentUser, HighestPrivileges: false));

        var imported = _store.Load(path);
        Assert.Equal(TaskAccount.CurrentUser, imported.Account);
        Assert.False(imported.HighestPrivileges);
        Assert.DoesNotContain(ForeignSid, _store.Export(path));
        Assert.Equal(2, imported.Triggers.Count);
    }

    [SkippableFact]
    public void ATaskWithAStoredPassword_CanOnlyBeSavedWithThatPassword()
    {
        // Windows keeps the password of a task that runs whether or not its user is logged on,
        // and wants it again for every change. Editing must neither drop that logon type nor
        // pretend the save worked.
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        string user = "ftstest" + Guid.NewGuid().ToString("N")[..5];
        string password = "Ft5!" + Guid.NewGuid().ToString("N")[..9]; // net.exe asks for confirmation above 14 characters
        _out.WriteLine(Run("net.exe", $"user {user} {password} /add"));
        try
        {
            var task = sandbox.Folder.RegisterTaskDefinition("Stored", SampleTasks.Simple(sandbox.Service), TaskCreation.CreateOrUpdate, user, password, TaskLogonType.Password);
            var draft = _store.Load(task.Path);
            Assert.True(draft.StoresPassword);
            Assert.Equal(TaskAccountKind.User, draft.Account.Kind);

            Assert.Throws<TaskAccountException>(() => _store.Save(draft with { Hidden = true }, sandbox.FolderPath, task.Path));
            Assert.Throws<TaskAccountException>(() => _store.Save(draft with { Hidden = true }, sandbox.FolderPath, task.Path, "not the password"));
            Assert.False(_store.Load(task.Path).Hidden);

            _store.Save(draft with { Hidden = true }, sandbox.FolderPath, task.Path, password);

            var saved = _store.Load(task.Path);
            Assert.True(saved.Hidden);
            Assert.True(saved.StoresPassword);
            Assert.Equal(TaskLogonType.Password, sandbox.Service.GetTask(task.Path).Definition.Principal.LogonType);

            // Enabling and disabling does not re-register the task, so it needs no password.
            _store.SetEnabled(task.Path, false);
            Assert.False(_store.Load(task.Path).Enabled);
        }
        finally
        {
            _out.WriteLine(Run("net.exe", $"user {user} /delete"));
        }
    }

    [SkippableFact]
    public void ATaskThatRunsWithoutItsUserLoggedOn_ButKeepsNoPassword_IsNotSaidToStoreOne()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var task = sandbox.Folder.RegisterTaskDefinition("S4U", SampleTasks.Simple(sandbox.Service), TaskCreation.CreateOrUpdate, TaskAccounts.CurrentUserName, null, TaskLogonType.S4U);
        using var identity = WindowsIdentity.GetCurrent();
        string xml = Regex.Replace(task.Xml, "<UserId>[^<]*</UserId>", $"<UserId>{identity.User!.Value}</UserId>");

        var info = _store.Inspect(xml);

        Assert.Equal(TaskAccount.CurrentUser, info.Account);
        Assert.False(info.StoresPassword);
    }

    [SkippableFact]
    public void ImportingSomethingThatIsNotATask_IsRefusedClearly()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();

        Assert.Throws<FormatException>(() => _store.Inspect("<html><body>nope</body></html>"));
        Assert.Throws<FormatException>(() => _store.Import("not xml at all", new TaskImportOptions(sandbox.FolderPath, "Bad")));
        Assert.Empty(sandbox.Folder.Tasks);
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 15000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(250);
        }
        return condition();
    }

    private static string Run(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return $"{file} exit {process.ExitCode}: {output.Trim()}";
    }
}
