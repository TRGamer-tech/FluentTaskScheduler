using System.Text.RegularExpressions;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Processes;
using FluentTaskScheduler.Tests.Library;
using FluentTaskScheduler.Tests.Processes;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class ScriptEditorViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly ScriptStore _store;
    private readonly ScriptEditorViewModel _editor;

    public ScriptEditorViewModelTests()
    {
        File.WriteAllText(_folder.File("built-in.json"), "[]");
        _store = new ScriptStore(_folder.Paths, _folder.File("built-in.json"));
        _editor = NewEditor(new InlineUiThread());
    }

    public void Dispose() => _folder.Dispose();

    private ScriptEditorViewModel NewEditor(IUiThread ui) => new(_store, _runner, _folder.Paths, ui, _prompts, _clipboard);

    /// <summary>The copy of the script that the started program was told to run.</summary>
    private string RunCopy => Regex.Match(_runner.Last.Request.Arguments, "-File \"([^\"]+)\"").Groups[1].Value;

    private static OutputLine Note(string text) => new(text, OutputKind.Note);
    private static OutputLine Line(string text) => new(text, OutputKind.Output);
    private static OutputLine Error(string text) => new(text, OutputKind.Error);

    [Fact]
    public async Task Running_HandsWindowsPowerShellACopyOfTheText_ThatItCanReadWhateverTheLanguage()
    {
        _editor.Text = "Write-Output 'Zürich'\rWrite-Output '東京'";

        Task run = _editor.RunCommand.ExecuteAsync(null);

        ProcessRequest request = _runner.Last.Request;
        Assert.Equal(WindowsTools.InUtf8Console($"\"{WindowsTools.PowerShell}\" {PowerShellCommand.ForFile(RunCopy)}"), request);
        Assert.Equal(_folder.Path, Path.GetDirectoryName(RunCopy));
        byte[] written = File.ReadAllBytes(RunCopy);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written[..3]);
        Assert.Equal("Write-Output 'Zürich'\r\nWrite-Output '東京'", System.Text.Encoding.UTF8.GetString(written[3..]));

        _runner.Last.Exits();
        await run;

        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public async Task WhatTheScriptWrites_IsShownInTheOrderItArrived_WithErrorsMarked_AndHowItEnded()
    {
        _editor.Text = "anything";

        Task run = _editor.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("one").WritesError("two").Writes("three");

        Assert.True(_editor.IsRunning);
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Error("two"), Line("three")], _editor.Output);
        Assert.Equal("two", _editor.Output[2].ToString());

        _runner.Last.Exits(3);
        await run;

        Assert.False(_editor.IsRunning);
        Assert.Equal(Note("ScriptEditor.Status.ExitCode"), _editor.Output[^1]);
        Assert.Equal(5, _editor.Output.Count);
    }

    [Fact]
    public async Task ALineTheScriptHasNotFinished_IsShownAtOnce_AndGivesWayToWhatBecomesOfIt()
    {
        _editor.Text = "anything";
        Task run = _editor.RunCommand.ExecuteAsync(null);

        _runner.Last.WritesUnfinished("Copying 10%");
        Assert.Equal(Line("Copying 10%"), _editor.Output[^1]);

        _runner.Last.WritesUnfinished("Copying 80%").Writes("Copying 100%").Writes("Done").WritesUnfinished("Continue? ").WritesError("no input");

        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("Copying 100%"), Line("Done"), Line("Continue? "), Error("no input")],
            _editor.Output);
        _runner.Last.Exits();
        await run;
    }

    [Fact]
    public async Task OfAProgressDisplayThatChangedWhileTheUiThreadWasBusy_OnlyTheLatestStateIsShown()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        Task run = editor.RunCommand.ExecuteAsync(null);
        var changes = new List<string>();
        editor.Output.CollectionChanged += (_, e) => changes.Add($"{e.Action} {((OutputLine?)e.NewItems?[0])?.Text}");

        _runner.Last.WritesUnfinished("1%").WritesUnfinished("2%").WritesUnfinished("3%");
        ui.RunPosted();
        _runner.Last.WritesUnfinished("4%").WritesUnfinished("5%").Writes("100%").Writes("Done");
        ui.RunPosted();

        Assert.Equal(["Add ScriptEditor.Status.Starting", "Add 3%", "Replace 100%", "Add Done"], changes);
        _runner.Last.Exits();
        await run;
    }

    [Fact]
    public async Task ANewRun_StartsWithEmptyOutput()
    {
        _editor.Text = "anything";
        Task first = _editor.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("from the first run").Exits();
        await first;

        Task second = _editor.RunCommand.ExecuteAsync(null);

        Assert.Equal([Note("ScriptEditor.Status.Starting")], _editor.Output);
        _runner.Last.Exits();
        await second;
    }

    [Fact]
    public async Task Stop_EndsTheScript_AndTheOutputSaysSo()
    {
        _editor.Text = "Start-Sleep 600";
        Task run = _editor.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("still going");

        _editor.StopCommand.Execute(null);
        await run;

        Assert.True(_runner.Last.WasStopped);
        Assert.False(_editor.IsRunning);
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("still going"), Note("ScriptEditor.Status.Stopped")], _editor.Output);
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public async Task WhenPowerShellCannotBeStarted_TheOutputShowsAnError_AndTheEditorIsReadyAgain()
    {
        _editor.Text = "anything";
        _runner.StartFailure = new InvalidOperationException("The system cannot find the file specified.");

        await _editor.RunCommand.ExecuteAsync(null);

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Error("ScriptEditor.Error.Start")], _editor.Output);
        Assert.False(_editor.IsRunning);
        Assert.True(_editor.RunCommand.CanExecute(null));
        Assert.Empty(Directory.GetFiles(_folder.Path, "script-run-*"));
    }

    [Fact]
    public async Task RunAndStop_AreAvailableOnlyWhenThereIsSomethingToRunOrToStop()
    {
        Assert.False(_editor.RunCommand.CanExecute(null));
        Assert.False(_editor.StopCommand.CanExecute(null));

        _editor.Text = "  \r ";
        Assert.False(_editor.RunCommand.CanExecute(null));

        _editor.Text = "Get-Date";
        Assert.True(_editor.RunCommand.CanExecute(null));

        Task run = _editor.RunCommand.ExecuteAsync(null);
        Assert.False(_editor.RunCommand.CanExecute(null));
        Assert.True(_editor.StopCommand.CanExecute(null));

        _runner.Last.Exits();
        await run;
        Assert.True(_editor.RunCommand.CanExecute(null));
        Assert.False(_editor.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task LinesThatArriveWhileTheUiThreadIsBusy_AreHandedOverTogether_InOrder()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        Task run = editor.RunCommand.ExecuteAsync(null);

        _runner.Last.Writes("one").Writes("two").Writes("three");

        Assert.Empty(editor.Output);
        Assert.Equal(1, ui.PostedCount);

        ui.RunPosted();

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Line("two"), Line("three")], editor.Output);
        _runner.Last.Exits();
        await run;
    }

    [Fact]
    public async Task AScriptThatWritesWithoutEnd_OnlyKeepsTheNewestLines()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        Task run = editor.RunCommand.ExecuteAsync(null);

        // In portions, as a UI thread would get to see them, and once far more than fits at all.
        int written = 0;
        foreach (int portion in new[] { 10, ScriptEditorViewModel.MaxOutputLines - 5, 3 * ScriptEditorViewModel.MaxOutputLines, 7 })
        {
            for (int i = 0; i < portion; i++) _runner.Last.Writes($"line {++written}");
            ui.RunPosted();

            Assert.True(editor.Output.Count <= ScriptEditorViewModel.MaxOutputLines);
            Assert.Equal(Line($"line {written}"), editor.Output[^1]);
        }

        Assert.Equal(ScriptEditorViewModel.MaxOutputLines, editor.Output.Count);
        Assert.Equal(Line($"line {written - ScriptEditorViewModel.MaxOutputLines + 1}"), editor.Output[0]);
        _runner.Last.Exits();
        await run;
    }

    [Fact]
    public async Task TheOutput_CanBeCopiedAsText_AndCleared()
    {
        _editor.Text = "anything";
        Task run = _editor.RunCommand.ExecuteAsync(null);
        _runner.Last.Writes("one").WritesError("two").Exits();
        await run;

        _editor.CopyOutputCommand.Execute(null);
        _editor.ClearOutputCommand.Execute(null);

        Assert.Equal(string.Join(Environment.NewLine, "ScriptEditor.Status.Starting", "one", "two", "ScriptEditor.Status.ExitCode"), _clipboard.Text);
        Assert.Empty(_editor.Output);
    }

    [Fact]
    public async Task Saving_PutsTheScriptIntoTheLibrary_AndSavingAgainChangesThatScript()
    {
        _editor.Name = " Tidy up ";
        _editor.Description = "Removes old files.";
        _editor.Text = "Remove-Item old.txt";

        await _editor.SaveCommand.ExecuteAsync(null);

        LibraryScript saved = Assert.Single(_store.GetScripts());
        Assert.Equal(("Tidy up", "Removes old files.", "Remove-Item old.txt"), (saved.Name, saved.Description, _store.ReadText(saved.Id)));
        Assert.Equal(Note("ScriptEditor.Status.Saved"), Assert.Single(_editor.Output));

        _editor.Name = "Tidy up more";
        _editor.Text = "Remove-Item older.txt";
        await _editor.SaveCommand.ExecuteAsync(null);

        LibraryScript changed = Assert.Single(_store.GetScripts());
        Assert.Equal((saved.Id, "Tidy up more", "Remove-Item older.txt"), (changed.Id, changed.Name, _store.ReadText(changed.Id)));
        Assert.Empty(_prompts.Shown);
    }

    [Theory]
    [InlineData("", "Get-Date", false)]
    [InlineData("  ", "Get-Date", false)]
    [InlineData("Clock", " \r", false)]
    [InlineData("Clock", "Get-Date", true)]
    public void Saving_NeedsANameAndSomeText(string name, string text, bool canSave)
    {
        _editor.Name = name;
        _editor.Text = text;

        Assert.Equal(canSave, _editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ANameAnotherScriptAlreadyHas_IsRefused_AndNothingIsSaved()
    {
        _store.Save(null, "Clock", "", "Get-Date");
        _editor.Name = "clock";
        _editor.Text = "Get-Date -Format s";

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Error: ScriptEditor.Error.NameTaken"], _prompts.Shown);
        Assert.Equal("Get-Date", _store.ReadText(Assert.Single(_store.GetScripts()).Id));
    }

    [Fact]
    public async Task OpeningAScript_PutsItsNameDescriptionAndTextIntoTheEditor_AndSavingChangesThatScript()
    {
        LibraryScript script = _store.Save(null, "Clock", "Tells the time.", "Get-Date");

        Assert.True(await _editor.OpenAsync(script));

        Assert.Equal(("Clock", "Tells the time.", "Get-Date"), (_editor.Name, _editor.Description, _editor.Text));

        _editor.Text = "Get-Date -Format s";
        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Get-Date -Format s", _store.ReadText(script.Id));
        Assert.Single(_store.GetScripts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningAScript_AsksBeforeChangesAreDiscarded_AndKeepsThemIfToldTo(bool discard)
    {
        LibraryScript script = _store.Save(null, "Clock", "", "Get-Date");
        _editor.Text = "not saved yet";
        _prompts.Confirm = discard;

        bool opened = await _editor.OpenAsync(script);

        Assert.Equal(discard, opened);
        Assert.Equal(discard ? "Get-Date" : "not saved yet", _editor.Text);
        Assert.Equal(["ScriptEditor.Discard.Title: ScriptEditor.Discard.Message [ScriptEditor.Discard.Confirm]"], _prompts.Shown);
    }

    [Fact]
    public async Task New_EmptiesTheEditor_WithoutAsking_WhenEverythingIsSaved()
    {
        _editor.Name = "Clock";
        _editor.Text = "Get-Date";
        await _editor.SaveCommand.ExecuteAsync(null);

        await _editor.NewCommand.ExecuteAsync(null);

        Assert.Equal(("", "", ""), (_editor.Name, _editor.Description, _editor.Text));
        Assert.Empty(_prompts.Shown);

        // What is saved next is a new script, not the one from before.
        _editor.Name = "Calendar";
        _editor.Text = "Get-Date -Format D";
        await _editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(["Clock", "Calendar"], _store.GetScripts().Select(script => script.Name));
    }

    [Fact]
    public async Task New_AsksBeforeChangesAreDiscarded()
    {
        _editor.Text = "not saved yet";
        _prompts.Confirm = false;

        await _editor.NewCommand.ExecuteAsync(null);

        Assert.Equal("not saved yet", _editor.Text);
        Assert.Single(_prompts.Shown);
    }

    [Fact]
    public async Task LineEndsATextBoxChangedOnItsOwn_AreNotChangesToDiscard()
    {
        LibraryScript script = _store.Save(null, "Two lines", "", "one\r\ntwo");
        await _editor.OpenAsync(script);

        _editor.Text = "one\rtwo";
        await _editor.NewCommand.ExecuteAsync(null);

        Assert.Empty(_prompts.Shown);
        Assert.Equal("", _editor.Text);
    }

    [Fact]
    public async Task AScriptWhoseFileIsGone_IsNotOpened_AndTheUserIsTold()
    {
        LibraryScript script = _store.Save(null, "Clock", "", "Get-Date");
        File.Delete(WindowsCommandLine.Split(script.Arguments)[^1]);
        _editor.Text = "";

        Assert.False(await _editor.OpenAsync(script));

        Assert.Equal(["Error: ScriptEditor.Error.Open"], _prompts.Shown);
        Assert.Equal("", _editor.Name);
    }

    [Fact]
    public async Task Disposing_EndsARunningScript()
    {
        _editor.Text = "Start-Sleep 600";
        Task run = _editor.RunCommand.ExecuteAsync(null);

        _editor.Dispose();
        await run;

        Assert.True(_runner.Last.WasStopped);
        Assert.False(File.Exists(RunCopy));
    }
}
