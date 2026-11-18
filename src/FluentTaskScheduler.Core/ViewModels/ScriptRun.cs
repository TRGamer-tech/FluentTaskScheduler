using FluentTaskScheduler.Library;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// One run of the script in the editor: Windows PowerShell with a copy of the script, and the
/// lines of output that the editor has not taken yet. How the run began and how it ended are
/// lines of that output too, worded in the language the app has at that moment.
/// </summary>
internal sealed class ScriptRun : ProgramRun
{
    private readonly List<(OutputLine Line, bool IsComplete)> _arrived = [];

    private ScriptRun(IUiThread ui) : base(ui)
    {
    }

    /// <summary>Starts the script the way a task made from it would run it. Call on the UI thread.</summary>
    /// <param name="file">Where the copy of the script goes that is run; it is removed when the run ends.</param>
    public static ScriptRun Start(IProcessRunner runner, IUiThread ui, string file, string text)
    {
        var run = new ScriptRun(ui);
        _ = run.RunAsync(runner, file, text);
        return run;
    }

    /// <summary>
    /// The lines that arrived since they were last taken, oldest first. A line that is not
    /// complete is one the script may still add to or write over: the next line of its kind
    /// replaces it.
    /// </summary>
    public (OutputLine Line, bool IsComplete)[] TakeLines()
    {
        lock (_arrived)
        {
            (OutputLine, bool)[] lines = [.. _arrived];
            _arrived.Clear();
            return lines;
        }
    }

    private async Task RunAsync(IProcessRunner runner, string file, string text)
    {
        try
        {
            ScriptFile.Write(file, text);
            Keep(OutputKind.Note, Loc.Get("ScriptEditor.Status.Starting"));

            // In the system folder, where Task Scheduler starts a task that names no folder of its own:
            // a path relative to the current folder leads where it would lead in the task.
            ProcessRequest request = WindowsTools.InUtf8Console(WindowsTools.PowerShell, PowerShellCommand.ForFile(file)) with
            {
                WorkingDirectory = Environment.SystemDirectory
            };
            int exitCode = await runner.RunAsync(
                request,
                line => Keep(line.IsError ? OutputKind.Error : OutputKind.Output, line.Text, line.IsComplete),
                Cancellation);

            Keep(OutputKind.Note, Loc.Format("ScriptEditor.Status.ExitCode", exitCode));
        }
        catch (OperationCanceledException)
        {
            Keep(OutputKind.Note, Loc.Get("ScriptEditor.Status.Stopped"));
        }
        catch (Exception ex) when (ex is ProcessStartException or IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The script in the editor could not be run from {Path}", file);
            Keep(OutputKind.Error, Loc.Format("ScriptEditor.Error.Start", ex.Message));
        }
        finally
        {
            DeleteRunCopy(file);
            ReportEnd();
        }
    }

    /// <summary>
    /// Keeps a line for the editor, from any thread. A script can write faster than a list on
    /// screen can follow, so the editor takes the lines together, once per turn of the UI thread.
    /// </summary>
    private void Keep(OutputKind kind, string text, bool isComplete = true)
    {
        lock (_arrived)
        {
            // Never more than the output can hold anyway, however long the UI thread takes.
            if (_arrived.Count >= 2 * ScriptEditorViewModel.MaxOutputLines) _arrived.RemoveRange(0, ScriptEditorViewModel.MaxOutputLines);

            _arrived.Add((new OutputLine(text, kind), isComplete));
        }
        Report();
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
