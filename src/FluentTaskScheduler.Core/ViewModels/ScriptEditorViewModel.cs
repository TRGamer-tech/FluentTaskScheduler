using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.ViewModels;

public enum OutputKind
{
    /// <summary>Written by the script.</summary>
    Output,
    /// <summary>Written by the script to its error stream.</summary>
    Error,
    /// <summary>Said by the editor itself, such as how the run ended.</summary>
    Note
}

/// <summary>One line in the output of the script editor.</summary>
/// <remarks>
/// Not a record: the list on screen finds a line by comparing it with the lines it holds, and of
/// lines that compared equal by their text, blank ones above all, it would always find the first.
/// </remarks>
public sealed class OutputLine(string text, OutputKind kind)
{
    public string Text { get; } = text;
    public OutputKind Kind { get; } = kind;

    /// <summary>The text alone: this is what a screen reader reads out for the line in a list.</summary>
    public override string ToString() => Text;
}

/// <summary>
/// The script editor of one window: a PowerShell script with the name and description it has in
/// the library, a way to try it out, and what it wrote when it ran.
/// </summary>
public sealed partial class ScriptEditorViewModel(
    ScriptStore scripts,
    IProcessRunner runner,
    AppPaths paths,
    IUiThread ui,
    IPrompts prompts,
    IClipboard clipboard) : ObservableObject, IDisposable
{
    /// <summary>How many lines of output are kept. Older lines make room for new ones.</summary>
    public const int MaxOutputLines = 5000;

    /// <summary>The run of the script that this editor shows; null while there is none.</summary>
    private ScriptRun? _run;

    /// <summary>The kind of the last line of the output while the script has not finished that line; null once it has.</summary>
    private OutputKind? _unfinished;

    /// <summary>The library's id of the script being edited; null for one that was never saved.</summary>
    private string? _scriptId;
    private (string Name, string Description, string Text) _saved = ("", "", "");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(SaveCommand))]
    private string _text = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand))]
    private bool _isRunning;

    /// <summary>What the script wrote and how its runs ended, oldest line first.</summary>
    public ObservableCollection<OutputLine> Output { get; } = [];

    private bool HasText => !string.IsNullOrWhiteSpace(Text);
    private bool CanRun => HasText && !IsRunning;
    private bool CanSave => HasText && !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// Puts a script of the library into the editor. Asks first if that would discard changes.
    /// </summary>
    /// <returns>False when the editor still holds what it held: the user said no, or the script could not be read.</returns>
    public async Task<bool> OpenAsync(LibraryScript script)
    {
        if (!await MayDiscardChangesAsync()) return false;

        try
        {
            Load(script.Id, script.Name, script.Description, scripts.ReadText(script.Id));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The script {Name} could not be opened", script.Name);
            await prompts.ShowErrorAsync(Loc.Format("ScriptEditor.Error.Open", script.Name, ex.Message));
            return false;
        }
    }

    /// <summary>Empties the editor for a new script. Asks first if that would discard changes.</summary>
    /// <returns>False when the editor still holds what it held, because the user said no.</returns>
    [RelayCommand]
    public async Task<bool> NewAsync()
    {
        if (!await MayDiscardChangesAsync()) return false;

        Load(null, "", "", "");
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        string name = Name.Trim();
        try
        {
            // Built-in scripts are left out: their names change with the language.
            bool nameIsTaken = scripts.GetScripts().Any(other =>
                !other.IsBuiltIn && other.Id != _scriptId && string.Equals(other.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (nameIsTaken)
            {
                await prompts.ShowErrorAsync(Loc.Format("ScriptEditor.Error.NameTaken", name));
                return;
            }

            _scriptId = scripts.Save(_scriptId, name, Description.Trim(), Text).Id;
            _saved = Current();
            Append([(new OutputLine(Loc.Format("ScriptEditor.Status.Saved", name), OutputKind.Note), IsComplete: true)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The script {Name} could not be saved", name);
            await prompts.ShowErrorAsync(Loc.Format("ScriptEditor.Error.Save", ex.Message));
        }
    }

    /// <summary>
    /// Runs the text as it stands in the editor, saved or not, the way a task created from the
    /// script would run it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        ClearOutput();

        // Not in the temp folder: clearing that is one of the quick actions.
        Show(ScriptRun.Start(runner, ui, paths.File($"script-run-{Guid.NewGuid():N}.ps1"), Text));
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _run?.Stop();

    [RelayCommand]
    private void ClearOutput()
    {
        // What the script has written but the output does not show yet goes with the rest.
        _run?.TakeLines();
        Output.Clear();
        _unfinished = null;
    }

    [RelayCommand]
    private async Task CopyOutputAsync()
    {
        if (!clipboard.SetText(string.Join(Environment.NewLine, Output.Select(line => line.Text))))
            await prompts.ShowErrorAsync(Loc.Get("Clipboard.Busy"));
    }

    /// <summary>
    /// Takes over from the editor this one replaces: its script, together with what of it is not
    /// saved yet, its output, and the run of the script if one is under way. A window gets new
    /// pages and view models when, for one, its language changes. The editor that was replaced
    /// shows nothing more after this, and disposing of it ends nothing.
    /// </summary>
    public void ContinueFrom(ScriptEditorViewModel previous)
    {
        Load(previous._scriptId, previous.Name, previous.Description, previous.Text);
        _saved = previous._saved;

        foreach (OutputLine line in previous.Output) Output.Add(line);
        _unfinished = previous._unfinished;
        if (previous._run is not { } run) return;

        previous._run = null;
        Show(run);
    }

    /// <summary>Ends the script if one is running that no other editor has taken over.</summary>
    public void Dispose() => _run?.Abandon();

    private void Load(string? id, string name, string description, string text)
    {
        _scriptId = id;
        Name = name;
        Description = description;
        Text = text;
        _saved = Current();
    }

    // Line ends are left out of the comparison: a text box hands the text back with its own kind.
    private (string Name, string Description, string Text) Current() =>
        (Name.Trim(), Description.Trim(), Text.ReplaceLineEndings("\n"));

    private async Task<bool> MayDiscardChangesAsync() =>
        Current() == _saved
        || await prompts.ConfirmAsync(
            Loc.Get("ScriptEditor.Discard.Title"),
            Loc.Get("ScriptEditor.Discard.Message"),
            Loc.Get("ScriptEditor.Discard.Confirm"),
            isDestructive: true);

    /// <summary>Shows a run from now on: that the script is running, what it writes, and that it has ended.</summary>
    private void Show(ScriptRun run)
    {
        _run = run;
        IsRunning = true;
        run.ReportTo(() =>
        {
            Append(run.TakeLines());
            if (!run.HasEnded) return;

            _run = null;
            IsRunning = false;
        });
    }

    /// <summary>
    /// Adds lines to the end of the output. A line that is not complete gives way to the next
    /// line of its kind, which is what became of it.
    /// </summary>
    private void Append((OutputLine Line, bool IsComplete)[] lines)
    {
        if (lines.Length >= MaxOutputLines)
        {
            Output.Clear();
            _unfinished = null;
            lines = lines[^MaxOutputLines..];
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var (line, isComplete) = lines[i];

            // A progress display redraws faster than anyone can read; only its latest state is worth showing.
            bool isReplacedAtOnce = !isComplete && i + 1 < lines.Length && lines[i + 1].Line.Kind == line.Kind;
            if (isReplacedAtOnce) continue;

            if (_unfinished == line.Kind) Output[^1] = line;
            else Output.Add(line);
            _unfinished = isComplete ? null : line.Kind;
        }
        while (Output.Count > MaxOutputLines) Output.RemoveAt(0);
    }
}
