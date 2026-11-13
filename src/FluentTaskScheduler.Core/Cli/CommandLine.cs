using System.Text.Json;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Cli;

/// <summary>
/// The app's command line: list tasks, start, enable and disable one, and export a task's
/// history. It works without a window and answers through its exit code, so scripts can use it.
/// </summary>
public sealed class CommandLine(ITaskStore store, ITaskRunner runner, ITaskHistory history, TextWriter output)
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    /// <summary>Where a history goes when no file is named: into the current folder.</summary>
    internal const string DefaultHistoryFile = "history.csv";

    /// <summary>
    /// The most events one export holds: the newest ones. Windows keeps some 15 000 events of all
    /// tasks together unless its log was made far larger; when this is reached, the export says so.
    /// </summary>
    internal const int MostEventsExported = 100_000;

    private static readonly string[] Commands = ["--list", "--run", "--enable", "--disable", "--export-history", "--help", "-h", "/?"];

    // Switches other programs start the app with: the updater's hooks, a clicked notification,
    // and the app restarting itself. They are meant for the window, not for the command line.
    private static readonly string[] NotCommands = ["--squirrel-", "--veloapp-", "-toastactivated", "-embedding", "----appnotificationactivated", AppArguments.AfterRestart];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// True when the arguments are meant for the command line. That includes switches it does not
    /// know: a typo should be told so, not answered by opening the window.
    /// </summary>
    public static bool Handles(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return false;
        string first = arguments[0].ToLowerInvariant();
        if (NotCommands.Any(first.StartsWith)) return false;
        return first.StartsWith('-') || first.StartsWith('/');
    }

    /// <returns>The process exit code.</returns>
    public int Run(IReadOnlyList<string> arguments)
    {
        string command = arguments.Count > 0 ? arguments[0].ToLowerInvariant() : "";
        string? task = arguments.Count > 1 ? arguments[1] : null;
        string? historyFile = HistoryFileOf(arguments);

        bool complete = command switch
        {
            "--run" or "--enable" or "--disable" => task is not null,
            "--export-history" => task is not null && historyFile is not null,
            _ => true
        };
        if (!Commands.Contains(command) || !complete)
        {
            output.WriteLine($"Unknown or incomplete command: {string.Join(' ', arguments)}");
            WriteUsage();
            return UsageError;
        }

        try
        {
            switch (command)
            {
                case "--list":
                    output.WriteLine(JsonSerializer.Serialize(store.GetTasks().Select(row => new
                    {
                        row.Name,
                        row.Path,
                        State = row.State.ToString(),
                        LastRun = row.LastRunTime,
                        NextRun = row.NextRunTime
                    }), Json));
                    return Success;

                case "--run":
                    if (runner.Run(task!) == RunOutcome.Suppressed)
                    {
                        output.WriteLine($"Not started, tasks are snoozed: {task}");
                        return Failed;
                    }
                    output.WriteLine($"Started: {task}");
                    return Success;

                case "--enable":
                case "--disable":
                    store.SetEnabled(task!, command == "--enable");
                    output.WriteLine($"{(command == "--enable" ? "Enabled" : "Disabled")}: {task}");
                    return Success;

                case "--export-history":
                    return ExportHistory(task!, historyFile!);

                default:
                    WriteUsage();
                    return Success;
            }
        }
        catch (TaskNotFoundException)
        {
            output.WriteLine($"Task not found: {task}");
        }
        catch (TaskAccessDeniedException)
        {
            output.WriteLine($"Access denied: {task}. Run the command as administrator.");
        }
        catch (Exception ex)
        {
            // Whatever Task Scheduler answered goes to the caller as text and a failing exit code.
            output.WriteLine($"Error: {ex.Message}");
        }
        return Failed;
    }

    /// <summary>The file a history export goes to; null when the task path is followed by anything but "--output &lt;file&gt;".</summary>
    private static string? HistoryFileOf(IReadOnlyList<string> arguments) => arguments.Count switch
    {
        2 => DefaultHistoryFile,
        4 when arguments[2].Equals("--output", StringComparison.OrdinalIgnoreCase) => arguments[3],
        _ => null
    };

    private int ExportHistory(string task, string file)
    {
        // The log names a task exactly as Task Scheduler spells it, so that spelling is asked for.
        // A task that is gone can still have history; it is then looked for as typed.
        TaskRow? row = store.GetTask(task);
        string path = row?.Path ?? task;
        IReadOnlyList<TaskEvent> events = history.GetEvents(path, MostEventsExported);
        bool recording = history.GetLogState() != LogState.Disabled;

        if (events.Count == 0 && row is null) throw new TaskNotFoundException(task);
        if (events.Count == 0 && !recording)
        {
            output.WriteLine($"No history: Task Scheduler is not recording any on this computer. Switch it on in the app, or as administrator with: wevtutil sl {TaskSchedulerLog.Channel} /e:true");
            return Failed;
        }

        File.WriteAllBytes(file, HistoryCsv.Write(events, TimeZoneInfo.Local));
        output.WriteLine($"Exported {events.Count} events of {path} to {Path.GetFullPath(file)}");
        if (events.Count == MostEventsExported) output.WriteLine("That is the most one export holds: older events were left out.");
        if (!recording) output.WriteLine("Note: Task Scheduler is not recording history at the moment, so the latest runs may be missing.");
        return Success;
    }

    private void WriteUsage()
    {
        output.WriteLine("FluentTaskScheduler.exe usage:");
        output.WriteLine("  --list                  List all scheduled tasks as JSON");
        output.WriteLine("  --run <task path>       Start a task, e.g. --run \"\\Backups\\Nightly\"");
        output.WriteLine("  --enable <task path>    Enable a task");
        output.WriteLine("  --disable <task path>   Disable a task");
        output.WriteLine("  --export-history <task path> [--output <file>]");
        output.WriteLine($"                          Write a task's history to a CSV file, {DefaultHistoryFile} unless a file is named");
        output.WriteLine("  --help                  Show this text");
        output.WriteLine("Exit code: 0 on success, 1 when the command failed, 2 when it was not understood.");
        output.WriteLine("Shells may not wait for it: in PowerShell, pipe the output on (e.g. | Out-Host); cmd waits in batch files only.");
    }
}
