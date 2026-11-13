using System.Globalization;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Processes;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class LibraryViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2030, 5, 6, 12, 20, 0);

    private readonly TempFolder _folder = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly RecordingNavigator _navigator = new();
    private readonly ScriptStore _store;
    private readonly ScriptEditorViewModel _editor;

    public LibraryViewModelTests()
    {
        File.WriteAllText(_folder.File("built-in.json"),
            """[{ "Id": "Ping", "Command": "ping.exe", "Arguments": "localhost" }, { "Id": "Reboot", "Command": "shutdown.exe", "Arguments": "/r", "RunAsAdmin": true }]""");
        _store = new ScriptStore(_folder.Paths, _folder.File("built-in.json"));
        _editor = new ScriptEditorViewModel(_store, new FakeProcessRunner(), _folder.Paths, new InlineUiThread(), _prompts, new FakeClipboard());
    }

    public void Dispose() => _folder.Dispose();

    private LibraryViewModel NewLibrary() =>
        new(_store, _editor, _navigator, _prompts, new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

    private static TemplateCard Template(LibraryViewModel library, string id) =>
        library.TemplateSections.SelectMany(group => group.Templates).Single(card => card.Id == id);

    private static ScriptCard Script(LibraryViewModel library, string name) => library.Scripts.Single(card => card.Name == name);

    [Fact]
    public void AllTemplatesAreListed_UnderTheHeadingsOfTheirGroups()
    {
        LibraryViewModel library = NewLibrary();

        Assert.Equal(
            [("Templates.Group.Maintenance", 7), ("Templates.Group.Developer", 6), ("Templates.Group.Power", 4)],
            library.TemplateSections.Select(group => (group.Title, group.Templates.Count)));
        Assert.False(library.HasNoTemplates);
    }

    [Fact]
    public void ATemplateCard_SaysWhatRunsAndWhen_AndWhetherItNeedsAdministratorRights()
    {
        LibraryViewModel library = NewLibrary();
        string twoOClock = new TimeOnly(2, 0).ToString("t", CultureInfo.CurrentCulture);

        TemplateCard defrag = Template(library, "Defrag");
        Assert.Equal(
            ("Templates.Defrag.Name", "Templates.Defrag.Desc", "Templates.Defrag.Note", $"defrag.exe · Dialog.Trigger.Weekly (Dialog.Day.Sun) Trigger.At {twoOClock}"),
            (defrag.Name, defrag.Description, defrag.Note, defrag.Summary));
        Assert.True(defrag.HasNote);
        Assert.True(defrag.RunsElevated);

        TemplateCard dnsFlush = Template(library, "DnsFlush");
        Assert.Equal("ipconfig.exe · Trigger.AtLogon", dnsFlush.Summary);
        Assert.False(dnsFlush.HasNote);
        Assert.False(dnsFlush.RunsElevated);

        Assert.StartsWith("powershell.exe · Dialog.Trigger.Daily Trigger.At ", Template(library, "TempCleanup").Summary);
        Assert.Equal("powershell.exe · Trigger.SessionStateChange", Template(library, "DisplayOff").Summary);
    }

    [Fact]
    public void Scripts_AreListedWithTheUsersOwnFirst_AndOnlyThoseCanBeChanged()
    {
        _store.Save(null, "Mine", "My own script.", "Get-Date");
        LibraryViewModel library = NewLibrary();

        Assert.Equal(["Mine", "Scripts.Ping.Name", "Scripts.Reboot.Name"], library.Scripts.Select(card => card.Name));
        Assert.Equal([(true, true), (false, false), (false, false)], library.Scripts.Select(card => (card.CanEdit, card.CanDelete)));
        Assert.Equal([false, false, true], library.Scripts.Select(card => card.RunsElevated));
        Assert.False(library.HasScriptsProblem);
    }

    [Fact]
    public void Searching_NarrowsTemplatesAndScriptsDown_ByNameOrDescription_WhateverTheCase()
    {
        _store.Save(null, "Mine", "Pings the build server.", "Test-Connection build");
        LibraryViewModel library = NewLibrary();

        library.SearchText = "  DOCKER ";

        TemplateSection group = Assert.Single(library.TemplateSections);
        Assert.Equal("Templates.Group.Developer", group.Title);
        Assert.Equal("DockerPrune", Assert.Single(group.Templates).Id);
        Assert.Empty(library.Scripts);
        Assert.True(library.HasNoScripts);
        Assert.False(library.HasNoTemplates);

        library.SearchText = "ping";

        Assert.Empty(library.TemplateSections);
        Assert.True(library.HasNoTemplates);
        Assert.Equal(["Mine", "Scripts.Ping.Name"], library.Scripts.Select(card => card.Name));
        Assert.False(library.HasNoScripts);

        library.SearchText = "";

        Assert.Equal(17, library.TemplateSections.Sum(section => section.Templates.Count));
        Assert.Equal(3, library.Scripts.Count);
    }

    [Fact]
    public async Task CreatingATaskFromATemplate_OpensTheTaskEditorWithTheTemplatesTask_AndThenShowsTheNewTask()
    {
        LibraryViewModel library = NewLibrary();
        _navigator.CreatedTaskPath = @"\Templates.Defrag.Name";

        await Template(library, "Defrag").CreateTaskCommand.ExecuteAsync(null);

        TaskTemplate template = BuiltInTemplates.Create().Single(candidate => candidate.Id == "Defrag").Template;
        Assert.Equal(template.ToDraft(Now), Assert.Single(_navigator.Drafts));
        Assert.Equal(new DateTime(2030, 5, 7, 2, 0, 0), _navigator.Drafts[0].Triggers[0].Start);
        Assert.Equal(["CreateTask Templates.Defrag.Name", @"ShowTask \Templates.Defrag.Name"], _navigator.Calls);
    }

    [Fact]
    public async Task CreatingATaskFromAScript_OpensTheTaskEditorWithATaskThatRunsTheScript()
    {
        LibraryViewModel library = NewLibrary();

        await Script(library, "Scripts.Reboot.Name").CreateTaskCommand.ExecuteAsync(null);

        TaskDraft draft = Assert.Single(_navigator.Drafts);
        Assert.Equal(("Scripts.Reboot.Name", "Scripts.Reboot.Description", true), (draft.Name, draft.Description, draft.HighestPrivileges));
        Assert.Equal([new ActionDraft { Path = "shutdown.exe", Arguments = "/r" }], draft.Actions);
        Assert.Equal([new TriggerDraft { Type = TriggerType.Daily, Start = new DateTime(2030, 5, 6, 13, 0, 0) }], draft.Triggers);
        // The user closed the task editor without saving: there is no task to show.
        Assert.Equal(["CreateTask Scripts.Reboot.Name"], _navigator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingAScript_AsksFirst_SayingWhatHappensToTasksThatRunIt(bool confirm)
    {
        LibraryScript mine = _store.Save(null, "Mine", "", "Get-Date");
        LibraryViewModel library = NewLibrary();
        _prompts.Confirm = confirm;

        await Script(library, "Mine").DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["ScriptLibrary.DeleteTemplate.Title: ScriptLibrary.DeleteTemplate.Content\n\nScriptLibrary.DeleteScript.TasksWarning [Dialog.Common.Delete]"], _prompts.Shown);
        Assert.Equal(!confirm, _store.GetScripts().Any(script => script.Id == mine.Id));
        Assert.Equal(!confirm, library.Scripts.Any(card => card.Id == mine.Id));
    }

    [Fact]
    public async Task EditingAScript_PutsItIntoTheScriptEditor_AndShowsTheEditor()
    {
        _store.Save(null, "Mine", "My own script.", "Get-Date");
        LibraryViewModel library = NewLibrary();

        await Script(library, "Mine").EditCommand.ExecuteAsync(null);

        Assert.Equal(("Mine", "My own script.", "Get-Date"), (_editor.Name, _editor.Description, _editor.Text));
        Assert.Equal(["ShowScriptEditor"], _navigator.Calls);
    }

    [Fact]
    public async Task EditingAScript_StaysInTheLibrary_WhenTheEditorKeepsItsUnsavedScript()
    {
        _store.Save(null, "Mine", "", "Get-Date");
        LibraryViewModel library = NewLibrary();
        _editor.Text = "not saved yet";
        _prompts.Confirm = false;

        await Script(library, "Mine").EditCommand.ExecuteAsync(null);

        Assert.Equal("not saved yet", _editor.Text);
        Assert.Empty(_navigator.Calls);
    }

    [Fact]
    public async Task NewScript_ShowsTheScriptEditor_Empty()
    {
        _store.Save(null, "Mine", "", "Get-Date");
        LibraryViewModel library = NewLibrary();
        await Script(library, "Mine").EditCommand.ExecuteAsync(null);
        _navigator.Calls.Clear();

        await library.NewScriptCommand.ExecuteAsync(null);

        Assert.Equal(("", ""), (_editor.Name, _editor.Text));
        Assert.Equal(["ShowScriptEditor"], _navigator.Calls);
    }

    [Fact]
    public async Task TheList_FollowsTheLibrary_UntilTheWindowCloses()
    {
        LibraryViewModel library = NewLibrary();
        library.SearchText = "clock";

        _editor.Name = "Clock";
        _editor.Text = "Get-Date";
        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Clock", Assert.Single(library.Scripts).Name);

        library.Dispose();
        _store.Save(null, "Clock two", "", "Get-Date");

        Assert.Single(library.Scripts);
    }

    [Fact]
    public void AListOfScriptsThatCouldNotBeRead_IsReported_AndTheBuiltInScriptsAreStillThere()
    {
        Directory.CreateDirectory(_folder.Paths.ScriptsFolder);
        File.WriteAllText(Path.Combine(_folder.Paths.ScriptsFolder, "scripts.json"), "not a list");

        LibraryViewModel library = NewLibrary();

        Assert.Equal("Library.Scripts.ListSetAside", library.ScriptsProblem);
        Assert.True(library.HasScriptsProblem);
        Assert.Equal(["Scripts.Ping.Name", "Scripts.Reboot.Name"], library.Scripts.Select(card => card.Name));
    }
}
