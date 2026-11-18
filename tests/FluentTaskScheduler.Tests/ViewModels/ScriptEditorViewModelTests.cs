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

    /// <summary>
    /// The editor of the pages a window gets anew: it takes over from the one that was there,
    /// which the window then disposes of together with its other old pages.
    /// </summary>
    private ScriptEditorViewModel TakeOver(ScriptEditorViewModel previous, IUiThread? ui = null)
    {
        ScriptEditorViewModel successor = NewEditor(ui ?? new InlineUiThread());
        successor.ContinueFrom(previous);
        previous.Dispose();
        return successor;
    }

    /// <summary>The copy of the script that the started program was told to run.</summary>
    private string RunCopy => Regex.Match(_runner.Last.Request.Arguments, "-File \"([^\"]+)\"").Groups[1].Value;

    private static (string, OutputKind) Note(string text) => (text, OutputKind.Note);
    private static (string, OutputKind) Line(string text) => (text, OutputKind.Output);
    private static (string, OutputKind) Error(string text) => (text, OutputKind.Error);

    /// <summary>A line of the output by what it shows, to compare with the lines a test expects.</summary>
    private static (string, OutputKind) Shown(OutputLine line) => (line.Text, line.Kind);

    [Fact]
    public void Running_HandsWindowsPowerShellACopyOfTheText_ThatItCanReadWhateverTheLanguage()
    {
        _editor.Text = "Write-Output 'Zürich'\rWrite-Output '東京'";

        _editor.RunCommand.Execute(null);

        ProcessRequest request = _runner.Last.Request;
        Assert.Equal(WindowsTools.InUtf8Console(WindowsTools.PowerShell, PowerShellCommand.ForFile(RunCopy)) with { WorkingDirectory = Environment.SystemDirectory }, request);
        Assert.Equal(_folder.Path, Path.GetDirectoryName(RunCopy));
        byte[] written = File.ReadAllBytes(RunCopy);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written[..3]);
        Assert.Equal("Write-Output 'Zürich'\r\nWrite-Output '東京'", System.Text.Encoding.UTF8.GetString(written[3..]));

        _runner.Last.Exits();

        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void AScript_RunsInTheFolderThatATaskMadeFromItStartsIn()
    {
        _editor.Text = "Get-ChildItem *.log";

        _editor.RunCommand.Execute(null);

        // A task made from a script names no folder to start in, so Task Scheduler starts it in the system folder.
        Assert.Equal(Environment.SystemDirectory, _runner.Last.Request.WorkingDirectory);
        _runner.Last.Exits();
    }

    [Fact]
    public void WhatTheScriptWrites_IsShownInTheOrderItArrived_WithErrorsMarked_AndHowItEnded()
    {
        _editor.Text = "anything";

        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one").WritesError("two").Writes("three");

        Assert.True(_editor.IsRunning);
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Error("two"), Line("three")], _editor.Output.Select(Shown));
        Assert.Equal("two", _editor.Output[2].ToString());

        _runner.Last.Exits(3);

        Assert.False(_editor.IsRunning);
        Assert.Equal(Note("ScriptEditor.Status.ExitCode"), Shown(_editor.Output[^1]));
        Assert.Equal(5, _editor.Output.Count);
    }

    [Fact]
    public void ALineTheScriptHasNotFinished_IsShownAtOnce_AndGivesWayToWhatBecomesOfIt()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);

        _runner.Last.WritesUnfinished("Copying 10%");
        Assert.Equal(Line("Copying 10%"), Shown(_editor.Output[^1]));

        _runner.Last.WritesUnfinished("Copying 80%").Writes("Copying 100%").Writes("Done").WritesUnfinished("Continue? ").WritesError("no input");

        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("Copying 100%"), Line("Done"), Line("Continue? "), Error("no input")],
            _editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void RepeatedLines_AreEntriesOfTheirOwn_SoTheListOnScreenFindsTheLastOne()
    {
        _editor.Text = "Get-ChildItem";
        _editor.RunCommand.Execute(null);

        _runner.Last.Writes("").Writes("    Directory: C:\\").Writes("");

        // The page scrolls to the last line, and the list on screen finds where that is with IndexOf.
        Assert.Equal(_editor.Output.Count - 1, _editor.Output.IndexOf(_editor.Output[^1]));
        _runner.Last.Exits();
    }

    [Fact]
    public void OfAProgressDisplayThatChangedWhileTheUiThreadWasBusy_OnlyTheLatestStateIsShown()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        var changes = new List<string>();
        editor.Output.CollectionChanged += (_, e) => changes.Add($"{e.Action} {((OutputLine?)e.NewItems?[0])?.Text}");

        _runner.Last.WritesUnfinished("1%").WritesUnfinished("2%").WritesUnfinished("3%");
        ui.RunPosted();
        _runner.Last.WritesUnfinished("4%").WritesUnfinished("5%").Writes("100%").Writes("Done");
        ui.RunPosted();

        Assert.Equal(["Add 3%", "Replace 100%", "Add Done"], changes);
        _runner.Last.Exits();
    }

    [Fact]
    public void ANewRun_StartsWithEmptyOutput()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("from the first run").Exits();

        _editor.RunCommand.Execute(null);

        Assert.Equal([Note("ScriptEditor.Status.Starting")], _editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void WhatTheUiThreadGetsToOnlyAfterAScriptHasEnded_DoesNotDisturbTheNextRun()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        _runner.Last.Writes("from the first run").Exits();
        editor.RunCommand.Execute(null);
        _runner.Last.Writes("from the second run");

        ui.RunPosted();

        Assert.True(editor.IsRunning);
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("from the second run")], editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void Stop_EndsTheScript_AndTheOutputSaysSo()
    {
        _editor.Text = "Start-Sleep 600";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("still going");

        _editor.StopCommand.Execute(null);

        Assert.True(_runner.Last.WasStopped);
        Assert.False(_editor.IsRunning);
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("still going"), Note("ScriptEditor.Status.Stopped")], _editor.Output.Select(Shown));
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void WhenPowerShellCannotBeStarted_TheOutputShowsAnError_AndTheEditorIsReadyAgain()
    {
        _editor.Text = "anything";
        _runner.StartFailure = new InvalidOperationException("The system cannot find the file specified.");

        _editor.RunCommand.Execute(null);

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Error("ScriptEditor.Error.Start")], _editor.Output.Select(Shown));
        Assert.False(_editor.IsRunning);
        Assert.True(_editor.RunCommand.CanExecute(null));
        Assert.Empty(Directory.GetFiles(_folder.Path, "script-run-*"));
    }

    [Fact]
    public void AFailureTheRunnerDoesNotDeclare_ShowsAsAnErrorInTheOutput_AndTheEditorIsReadyAgain()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one");

        _runner.Last.RunFails(new InvalidOperationException("No process is associated with this object."));

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Error("ScriptEditor.Error.Start")], _editor.Output.Select(Shown));
        Assert.False(_editor.IsRunning);
        Assert.True(_editor.RunCommand.CanExecute(null));
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void RunAndStop_AreAvailableOnlyWhenThereIsSomethingToRunOrToStop()
    {
        Assert.False(_editor.RunCommand.CanExecute(null));
        Assert.False(_editor.StopCommand.CanExecute(null));

        _editor.Text = "  \r ";
        Assert.False(_editor.RunCommand.CanExecute(null));

        _editor.Text = "Get-Date";
        Assert.True(_editor.RunCommand.CanExecute(null));

        _editor.RunCommand.Execute(null);
        Assert.False(_editor.RunCommand.CanExecute(null));
        Assert.True(_editor.StopCommand.CanExecute(null));

        _runner.Last.Exits();
        Assert.True(_editor.RunCommand.CanExecute(null));
        Assert.False(_editor.StopCommand.CanExecute(null));
    }

    [Fact]
    public void LinesThatArriveWhileTheUiThreadIsBusy_AreHandedOverTogether_InOrder()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);

        _runner.Last.Writes("one").Writes("two").Writes("three");

        Assert.Equal([Note("ScriptEditor.Status.Starting")], editor.Output.Select(Shown));
        Assert.Equal(1, ui.PostedCount);

        ui.RunPosted();

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Line("two"), Line("three")], editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void ALineThatArrivesWhileEarlierOnesAreBeingShown_IsShownAsWell_WithoutWaitingForTheNext()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        FakeProcess script = _runner.Last;
        // The script writes its second line at the very moment the first one is put on screen.
        editor.Output.CollectionChanged += (_, e) =>
        {
            if (e.NewItems?[0] is OutputLine { Text: "one" }) script.Writes("two");
        };

        script.Writes("one");
        ui.RunPosted();

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Line("two")], editor.Output.Select(Shown));
        script.Exits();
    }

    [Fact]
    public void AScriptThatWritesWithoutEnd_OnlyKeepsTheNewestLines()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);

        // In portions, as a UI thread would get to see them, and once far more than fits at all.
        int written = 0;
        foreach (int portion in new[] { 10, ScriptEditorViewModel.MaxOutputLines - 5, 3 * ScriptEditorViewModel.MaxOutputLines, 7 })
        {
            for (int i = 0; i < portion; i++) _runner.Last.Writes($"line {++written}");
            ui.RunPosted();

            Assert.True(editor.Output.Count <= ScriptEditorViewModel.MaxOutputLines);
            Assert.Equal(Line($"line {written}"), Shown(editor.Output[^1]));
        }

        Assert.Equal(ScriptEditorViewModel.MaxOutputLines, editor.Output.Count);
        Assert.Equal(Line($"line {written - ScriptEditorViewModel.MaxOutputLines + 1}"), Shown(editor.Output[0]));
        _runner.Last.Exits();
    }

    [Fact]
    public void TheOutput_CanBeCopiedAsText_AndCleared()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one").WritesError("two").Exits();

        _editor.CopyOutputCommand.Execute(null);
        _editor.ClearOutputCommand.Execute(null);

        Assert.Equal(string.Join(Environment.NewLine, "ScriptEditor.Status.Starting", "one", "two", "ScriptEditor.Status.ExitCode"), _clipboard.Text);
        Assert.Empty(_editor.Output);
    }

    [Fact]
    public void ClearingTheOutput_AlsoDropsWhatTheScriptHasWrittenButTheOutputDoesNotShowYet()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        _runner.Last.Writes("before");

        editor.ClearOutputCommand.Execute(null);
        _runner.Last.Writes("after");
        ui.RunPosted();

        Assert.Equal([Line("after")], editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public async Task CopyingTheOutput_WhileAnotherProgramHoldsTheClipboard_SaysThatNothingWasCopied()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one").Exits();
        _clipboard.IsBusy = true;

        await _editor.CopyOutputCommand.ExecuteAsync(null);

        Assert.Null(_clipboard.Text);
        Assert.Equal(["Error: Clipboard.Busy"], _prompts.Shown);
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
        Assert.Equal(Note("ScriptEditor.Status.Saved"), Shown(Assert.Single(_editor.Output)));

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
    public async Task AScriptThatCannotBeSaved_IsReported_AndStaysUnsaved()
    {
        // The scripts folder cannot be made where a file of its name stands.
        File.WriteAllText(_folder.Paths.ScriptsFolder, "a file where the folder should be");
        _editor.Name = "Clock";
        _editor.Text = "Get-Date";

        await _editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Error: ScriptEditor.Error.Save"], _prompts.Shown);
        Assert.Empty(_store.GetScripts());
        Assert.Empty(_editor.Output);
        _prompts.Confirm = false;
        Assert.False(await _editor.NewAsync());
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

        Assert.True(await _editor.NewAsync());

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

        Assert.False(await _editor.NewAsync());

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
    public async Task AnEditorThatTakesOver_ContinuesWithTheScript_AndStillKnowsWhatIsNotSaved()
    {
        LibraryScript script = _store.Save(null, "Clock", "Tells the time.", "Get-Date");
        await _editor.OpenAsync(script);
        _editor.Text = "Get-Date -Format s";

        ScriptEditorViewModel successor = TakeOver(_editor);

        Assert.Equal(("Clock", "Tells the time.", "Get-Date -Format s"), (successor.Name, successor.Description, successor.Text));
        _prompts.Confirm = false;
        Assert.False(await successor.NewAsync());

        await successor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Get-Date -Format s", _store.ReadText(Assert.Single(_store.GetScripts()).Id));
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
    public void Disposing_EndsARunningScript_AndNothingMoreReachesTheOutput()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "Start-Sleep 600";
        editor.RunCommand.Execute(null);
        _runner.Last.Writes("still going");

        editor.Dispose();
        ui.RunPosted();

        Assert.True(_runner.Last.WasStopped);
        Assert.False(File.Exists(RunCopy));
        Assert.Equal([Note("ScriptEditor.Status.Starting")], editor.Output.Select(Shown));
    }

    [Fact]
    public void AScriptThatIsRunning_RunsOnInTheEditorThatTakesOver_WithWhatItWroteSoFar()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one").WritesError("two");

        ScriptEditorViewModel successor = TakeOver(_editor);

        Assert.False(_runner.Last.WasStopped);
        Assert.True(File.Exists(RunCopy));
        Assert.True(successor.IsRunning);
        Assert.False(successor.RunCommand.CanExecute(null));
        Assert.True(successor.StopCommand.CanExecute(null));
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Error("two")], successor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void WhatAScriptWritesAfterAnotherEditorTookOver_IsShownThere_AndSoIsItsEnd_Once()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one");
        ScriptEditorViewModel successor = TakeOver(_editor);

        _runner.Last.Writes("two").Exits(3);

        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("one"), Line("two"), Note("ScriptEditor.Status.ExitCode")],
            successor.Output.Select(Shown));
        Assert.False(successor.IsRunning);
        Assert.True(successor.RunCommand.CanExecute(null));
        Assert.False(successor.StopCommand.CanExecute(null));
        Assert.False(File.Exists(RunCopy));
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one")], _editor.Output.Select(Shown));
    }

    [Fact]
    public void Stop_InTheEditorThatTookOver_EndsTheScript_AndTheOutputSaysSo()
    {
        _editor.Text = "Start-Sleep 600";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("still going");
        ScriptEditorViewModel successor = TakeOver(_editor);

        successor.StopCommand.Execute(null);

        Assert.True(_runner.Last.WasStopped);
        Assert.False(successor.IsRunning);
        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("still going"), Note("ScriptEditor.Status.Stopped")],
            successor.Output.Select(Shown));
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void LinesTheUiThreadHadNotGotTo_WhenAnotherEditorTookOver_AreShownThere_OnceAndInOrder()
    {
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        _runner.Last.Writes("one");
        ui.RunPosted();
        _runner.Last.Writes("two").Writes("three");

        ScriptEditorViewModel successor = TakeOver(editor, ui);
        _runner.Last.Writes("four");
        ui.RunPosted();

        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("one"), Line("two"), Line("three"), Line("four")],
            successor.Output.Select(Shown));
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one")], editor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void ALineTheScriptHadNotFinished_WhenAnotherEditorTookOver_StillGivesWayToWhatBecomesOfIt()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("Copying").WritesUnfinished("10%");
        ScriptEditorViewModel successor = TakeOver(_editor);

        _runner.Last.WritesUnfinished("80%").Writes("100%");

        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("Copying"), Line("100%")], successor.Output.Select(Shown));
        _runner.Last.Exits();
    }

    [Fact]
    public void OnlyTheNewestLinesAreKept_AlsoWhenAnotherEditorTookOverOnTheWay()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        int written = 0;
        while (written < ScriptEditorViewModel.MaxOutputLines - 10) _runner.Last.Writes($"line {++written}");
        ScriptEditorViewModel successor = TakeOver(_editor);

        while (written < ScriptEditorViewModel.MaxOutputLines + 10) _runner.Last.Writes($"line {++written}");

        // The note the run began with and the first ten lines have made room.
        Assert.Equal(ScriptEditorViewModel.MaxOutputLines, successor.Output.Count);
        Assert.Equal(Line("line 11"), Shown(successor.Output[0]));
        Assert.Equal(Line($"line {written}"), Shown(successor.Output[^1]));
        _runner.Last.Exits();
    }

    [Fact]
    public void TheEditorThatWasReplaced_RaisesNothingMore_WhateverBecomesOfTheScript()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one");
        var raised = new List<string>();
        _editor.PropertyChanged += (_, e) => raised.Add($"property {e.PropertyName}");
        _editor.Output.CollectionChanged += (_, e) => raised.Add($"output {e.Action}");
        _editor.RunCommand.CanExecuteChanged += (_, _) => raised.Add("can run");
        _editor.StopCommand.CanExecuteChanged += (_, _) => raised.Add("can stop");

        TakeOver(_editor);
        _runner.Last.Writes("two").WritesUnfinished("three").Exits();

        Assert.Empty(raised);
    }

    [Fact]
    public void AScript_RunsOn_WhenTheWindowGetsNewPagesASecondTime()
    {
        _editor.Text = "Start-Sleep 600";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one");
        ScriptEditorViewModel second = TakeOver(_editor);
        _runner.Last.Writes("two");

        ScriptEditorViewModel third = TakeOver(second);
        _runner.Last.Writes("three");

        Assert.False(_runner.Last.WasStopped);
        Assert.True(third.IsRunning);

        third.StopCommand.Execute(null);

        Assert.True(_runner.Last.WasStopped);
        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("one"), Line("two"), Line("three"), Note("ScriptEditor.Status.Stopped")],
            third.Output.Select(Shown));
        Assert.Equal([Note("ScriptEditor.Status.Starting"), Line("one"), Line("two")], second.Output.Select(Shown));
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void DisposingTheEditorThatTookOver_EndsTheScript()
    {
        _editor.Text = "Start-Sleep 600";
        _editor.RunCommand.Execute(null);
        ScriptEditorViewModel successor = TakeOver(_editor);

        successor.Dispose();

        Assert.True(_runner.Last.WasStopped);
        Assert.False(File.Exists(RunCopy));
    }

    [Fact]
    public void TheOutputOfAScriptThatHasEnded_IsStillThere_InTheEditorThatTakesOver()
    {
        _editor.Text = "anything";
        _editor.RunCommand.Execute(null);
        _runner.Last.Writes("one").Exits();

        ScriptEditorViewModel successor = TakeOver(_editor);

        Assert.Equal(
            [Note("ScriptEditor.Status.Starting"), Line("one"), Note("ScriptEditor.Status.ExitCode")],
            successor.Output.Select(Shown));
        Assert.False(successor.IsRunning);
        Assert.True(successor.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhatAScriptWritesOnAnotherThread_WhileEditorAfterEditorTakesOver_ArrivesOnceAndInOrder()
    {
        const int lines = 8 * ScriptEditorViewModel.MaxOutputLines;
        var ui = new BusyUiThread();
        ScriptEditorViewModel editor = NewEditor(ui);
        editor.Text = "anything";
        editor.RunCommand.Execute(null);
        FakeProcess script = _runner.Last;
        Task writing = Task.Run(() =>
        {
            for (int line = 1; line <= lines; line++) script.Writes($"line {line}");
        });

        // This thread is the UI thread: it hands the run from editor to editor for as long as the script writes.
        while (!writing.IsCompleted) editor = TakeOver(editor, ui);
        await writing;
        ui.RunPosted();

        // More was written than the output holds: it shows the newest lines, each of them once.
        Assert.Equal(
            Enumerable.Range(lines - ScriptEditorViewModel.MaxOutputLines + 1, ScriptEditorViewModel.MaxOutputLines).Select(line => $"line {line}"),
            editor.Output.Select(line => line.Text));
        script.Exits();
    }
}
