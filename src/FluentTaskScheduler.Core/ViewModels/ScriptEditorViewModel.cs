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
public sealed record OutputLine(string Text, OutputKind Kind)
{
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

    private readonly List<(OutputLine Line, bool IsComplete)> _arrived = [];
    private CancellationTokenSource? _run;
    private bool _disposed;

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
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    /// <summary>What the script wrote and how its runs ended, oldest line first.</summary>
    public ObservableCollection<OutputLine> Output { get; } = [];

    private bool HasText => !string.IsNullOrWhiteSpace(Text);
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

    [RelayCommand]
    private async Task NewAsync()
    {
        if (await MayDiscardChangesAsync()) Load(null, "", "", "");
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
            Show(OutputKind.Note, Loc.Format("ScriptEditor.Status.Saved", name));
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
    [RelayCommand(CanExecute = nameof(HasText))]
    private async Task RunAsync()
    {
        // Not in the temp folder: clearing that is one of the quick actions.
        string file = paths.File($"script-run-{Guid.NewGuid():N}.ps1");
        using var run = new CancellationTokenSource();
        _run = run;
        IsRunning = true;
        ClearOutput();
        try
        {
            ScriptFile.Write(file, Text);
            Show(OutputKind.Note, Loc.Get("ScriptEditor.Status.Starting"));

            int exitCode = await runner.RunAsync(
                WindowsTools.InUtf8Console(WindowsTools.PowerShell, PowerShellCommand.ForFile(file)),
                line => Show(line.IsError ? OutputKind.Error : OutputKind.Output, line.Text, line.IsComplete),
                run.Token);

            Show(OutputKind.Note, Loc.Format("ScriptEditor.Status.ExitCode", exitCode));
        }
        catch (OperationCanceledException)
        {
            Show(OutputKind.Note, Loc.Get("ScriptEditor.Status.Stopped"));
        }
        catch (Exception ex) when (ex is ProcessStartException or IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The script in the editor could not be run from {Path}", file);
            Show(OutputKind.Error, Loc.Format("ScriptEditor.Error.Start", ex.Message));
        }
        finally
        {
            _run = null;
            IsRunning = false;
            DeleteRunCopy(file);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _run?.Cancel();

    [RelayCommand]
    private void ClearOutput()
    {
        lock (_arrived) _arrived.Clear();
        Output.Clear();
        _unfinished = null;
    }

    [RelayCommand]
    private void CopyOutput() => clipboard.SetText(string.Join(Environment.NewLine, Output.Select(line => line.Text)));

    /// <summary>Ends the script if one is running.</summary>
    public void Dispose()
    {
        _disposed = true;
        _run?.Cancel();
    }

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

    /// <summary>
    /// Adds a line to the output, from any thread. A script can write faster than a list on
    /// screen can follow, so lines are collected here and handed over together, once per turn
    /// of the UI thread.
    /// </summary>
    /// <param name="isComplete">False for a line the script may still add to or write over; the next line of its kind replaces it.</param>
    private void Show(OutputKind kind, string text, bool isComplete = true)
    {
        lock (_arrived)
        {
            // Never more than the output can hold anyway, however long the UI thread takes.
            if (_arrived.Count >= 2 * MaxOutputLines) _arrived.RemoveRange(0, MaxOutputLines);

            _arrived.Add((new OutputLine(text, kind), isComplete));
            if (_arrived.Count > 1) return; // the hand-over is already on its way
        }
        ui.Post(HandOverArrivedLines);
    }

    private void HandOverArrivedLines()
    {
        (OutputLine Line, bool IsComplete)[] lines;
        lock (_arrived)
        {
            lines = [.. _arrived];
            _arrived.Clear();
        }
        if (_disposed) return;

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

    private static void DeleteRunCopy(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The copy {Path} of a script that was run could not be removed", file);
        }
    }
}
