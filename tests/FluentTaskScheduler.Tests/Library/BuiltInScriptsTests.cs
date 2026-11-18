using FluentTaskScheduler.Library;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>The scripts that ship with the app, read from the file the app ships.</summary>
public sealed class BuiltInScriptsTests : IDisposable
{
    private static readonly DateTime Now = new(2030, 5, 6, 12, 20, 0);

    private readonly TempFolder _folder = new();
    private readonly IReadOnlyList<LibraryScript> _scripts;

    public BuiltInScriptsTests() => _scripts = new ScriptStore(_folder.Paths, RepoFiles.BuiltInScripts).GetScripts();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ThereAre13Scripts_EachWithItsOwnId_AndAllMarkedAsBuiltIn()
    {
        Assert.Equal(13, _scripts.Count);
        Assert.Equal(_scripts.Count, _scripts.Select(script => script.Id).Distinct().Count());
        Assert.All(_scripts, script =>
        {
            Assert.True(script.IsBuiltIn);
            Assert.False(script.HasText);
        });
    }

    [Fact]
    public void EveryScript_BecomesATaskTheEditorCanSaveAsItIs()
    {
        Assert.All(_scripts, script =>
        {
            TaskDraft draft = script.ToTemplate().ToDraft(Now);

            Assert.Equal($"Scripts.{script.Id}.Name", draft.Name);
            Assert.Equal($"Scripts.{script.Id}.Description", draft.Description);
            Assert.Equal(TaskAccount.CurrentUser, draft.Account);
            Assert.False(draft.StoresPassword);
            Assert.Equal(script.RunElevated, draft.HighestPrivileges);

            ActionDraft action = Assert.Single(draft.Actions);
            Assert.Matches(@"^[a-z]+\.exe$", action.Path);
            Assert.DoesNotContain("$(", action.Arguments);

            // A script stands for no schedule: the task starts out daily, at the next full hour.
            Assert.Equal([new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2030, 5, 6, 13, 0, 0) }], draft.Triggers);
        });
    }

    [Theory]
    [MemberData(nameof(RepoFiles.Languages), MemberType = typeof(RepoFiles))]
    public void EveryScript_HasItsNameAndDescription_InEveryLanguage(string language)
    {
        var texts = RepoFiles.Texts(language);

        string[] missing = _scripts.SelectMany(script => new[] { script.Name, script.Description }).Where(name => !texts.ContainsKey(name)).ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void ClearingTheTempFolder_IsTheSameCommandAsTheQuickAction()
    {
        LibraryScript script = _scripts.Single(script => script.Id == "ClearTempFolder");

        Assert.Equal(QuickAction.ClearTempScript, WindowsCommandLine.PowerShellScriptIn(script.Arguments));
    }

    [SkippableFact]
    public async Task PowerShellInAScript_IsAcceptedByWindowsPowerShell()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);

        foreach (LibraryScript script in _scripts)
        {
            if (WindowsCommandLine.PowerShellScriptIn(script.Arguments) is not { } powerShell) continue;

            var errors = await WindowsPowerShell.SyntaxErrorsInAsync(powerShell);
            Assert.True(errors.Count == 0, $"{script.Id}: {string.Join("; ", errors)}");
        }
    }
}
