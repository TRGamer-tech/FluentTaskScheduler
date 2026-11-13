using System.Text.RegularExpressions;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Integration;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>
/// The task templates that ship with the app. Texts are resource names here, as in every test,
/// which is what lets the tests find the resources a template needs.
/// </summary>
public sealed class BuiltInTemplatesTests
{
    private static readonly DateTime Now = new(2030, 5, 6, 12, 0, 0);
    private static readonly IReadOnlyList<BuiltInTemplate> Templates = BuiltInTemplates.Create();

    public static TheoryData<string> Ids => new(Templates.Select(template => template.Id));

    private static BuiltInTemplate Find(string id) => Templates.Single(template => template.Id == id);

    [Fact]
    public void ThereAre17Templates_EachWithItsOwnId_AndSomeInEveryGroup()
    {
        Assert.Equal(17, Templates.Count);
        Assert.Equal(Templates.Count, Templates.Select(template => template.Id).Distinct().Count());
        Assert.Equal(Enum.GetValues<TemplateGroup>(), Templates.Select(template => template.Group).Distinct());
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void EveryTemplate_BecomesATaskTheEditorCanSaveAsItIs(string id)
    {
        BuiltInTemplate template = Find(id);

        TaskDraft draft = template.Template.ToDraft(Now);

        Assert.Equal($"Templates.{id}.Name", draft.Name);
        Assert.Equal($"Templates.{id}.Desc", draft.Description);
        Assert.Equal(TaskAccount.CurrentUser, draft.Account);
        Assert.False(draft.StoresPassword);
        Assert.True(draft.Enabled);

        ActionDraft action = Assert.Single(draft.Actions);
        Assert.True(action.IsEditable);
        Assert.Matches(@"^[a-z]+\.exe$", action.Path);
        // Task Scheduler takes $(name) in arguments for one of its own variables and replaces it.
        Assert.DoesNotContain("$(", action.Arguments);

        TriggerDraft trigger = Assert.Single(draft.Triggers);
        Assert.True(trigger.Enabled);
        switch (trigger.Type)
        {
            case TriggerType.Daily:
            case TriggerType.Weekly:
                Assert.NotNull(template.Template.At);
                Assert.InRange(trigger.Start!.Value, Now.AddSeconds(1), Now.AddDays(1));
                Assert.Equal(template.Template.At.Value.ToTimeSpan(), trigger.Start.Value.TimeOfDay);
                Assert.NotEqual(default, trigger.DaysOfWeek);
                break;
            case TriggerType.AtLogon:
            case TriggerType.SessionStateChange:
                Assert.Null(trigger.Start);
                break;
            default:
                Assert.Fail($"No template is expected to use a trigger of type {trigger.Type}.");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void PowerShellInATemplate_ReachesPowerShellAsOneLineInOneArgument(string id)
    {
        TaskTemplate template = Find(id).Template;
        if (template.Command != PowerShellCommand.Executable) return;

        List<string> arguments = WindowsCommandLine.Split(template.Arguments);

        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command"], arguments.Take(7));
        Assert.Equal(8, arguments.Count);
        Assert.DoesNotContain('\n', arguments[7]);
        Assert.Equal(template.Arguments, PowerShellCommand.ForScript(arguments[7]));
    }

    [Theory]
    [MemberData(nameof(RepoFiles.Languages), MemberType = typeof(RepoFiles))]
    public void EveryTextOfATemplate_ExistsInEveryLanguage(string language)
    {
        var texts = RepoFiles.Texts(language);

        var needed = Templates.SelectMany(template => new[]
            {
                template.Template.Name,
                template.Template.Description,
                template.Note,
                $"Templates.Group.{template.Group}",
                // The text of a shutdown message travels inside the arguments.
                Regex.Match(template.Template.Arguments, @"Templates\.\w+\.\w+").Value
            })
            .Where(name => name.Length > 0)
            .Distinct();

        string[] missing = needed.Where(name => !texts.ContainsKey(name)).ToArray();
        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(RepoFiles.Languages), MemberType = typeof(RepoFiles))]
    public void TheNameOfATemplate_IsAValidTaskName_InEveryLanguage(string language)
    {
        var texts = RepoFiles.Texts(language);

        // The name on the card is the name of the task; it should not need repairing.
        string[] invalid = Templates.Select(template => texts[template.Template.Name]).Where(name => name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0).ToArray();
        Assert.Empty(invalid);
    }

    [SkippableTheory]
    [MemberData(nameof(Ids))]
    public async Task PowerShellInATemplate_IsAcceptedByWindowsPowerShell(string id)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        if (WindowsCommandLine.PowerShellScriptIn(Find(id).Template.Arguments) is not { } script) return;

        Assert.Empty(await WindowsPowerShell.SyntaxErrorsInAsync(script));
    }

    [SkippableTheory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(7, 0)]
    [InlineData(8, 8)]
    [InlineData(11, 8)]
    [InlineData(16, 16)]
    [InlineData(17, 16)]
    public async Task TheBackupTemplate_ReportsSuccess_ForEveryExitCodeRobocopyUsesForSuccess(int robocopyExitCode, int expectedExitCode)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        TaskTemplate template = Find("ProjectBackup").Template;
        // Everything up to the "&" is the copy itself; a command that only sets the exit code stands in for it.
        string afterTheCopy = template.Arguments[template.Arguments.IndexOf(" & ", StringComparison.Ordinal)..];

        var (exitCode, _) = await WindowsPowerShell.RunAsync(template.Command, $"/c cmd /c exit {robocopyExitCode}{afterTheCopy}");

        Assert.Equal(expectedExitCode, exitCode);
    }
}

/// <summary>Registers every template with the real Task Scheduler.</summary>
[Collection(SchedulerSandbox.Collection)]
public sealed class BuiltInTemplatesInTaskSchedulerTests
{
    [SkippableTheory]
    [MemberData(nameof(BuiltInTemplatesTests.Ids), MemberType = typeof(BuiltInTemplatesTests))]
    public void EveryTemplate_IsAcceptedByTaskScheduler_AndReadsBackAsItWasSaved(string id)
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");
        using var sandbox = new SchedulerSandbox();
        var store = new TaskStore();
        TaskDraft draft = BuiltInTemplates.Create().Single(template => template.Id == id).Template.ToDraft(DateTime.Now);

        TaskDraft saved = store.Load(store.Save(draft, sandbox.FolderPath));

        Assert.Equal(draft.Triggers.Select((trigger, index) => trigger with { SourceIndex = index }), saved.Triggers);
        Assert.Equal(draft.Actions.Select((action, index) => action with { SourceIndex = index }), saved.Actions);
        // Task Scheduler keeps no restart interval for a task that is never restarted.
        Assert.Equal(draft with { Triggers = saved.Triggers, Actions = saved.Actions, RestartInterval = saved.RestartInterval }, saved);
    }
}
