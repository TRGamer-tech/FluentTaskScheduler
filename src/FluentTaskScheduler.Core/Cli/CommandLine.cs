using System.Text.Json;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Cli;

/// <summary>
/// The app's command line: list tasks, start, enable and disable one. It works without a window
/// and answers through its exit code, so scripts can use it.
/// </summary>
public sealed class CommandLine(ITaskStore store, ITaskRunner runner, TextWriter output)
{
    public const int Success = 0;
    public const int Failed = 1;
    public const int UsageError = 2;

    private static readonly string[] Commands = ["--list", "--run", "--enable", "--disable", "--help", "-h", "/?"];

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

        if (!Commands.Contains(command) || (command is "--run" or "--enable" or "--disable" && task is null))
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

    private void WriteUsage()
    {
        output.WriteLine("FluentTaskScheduler.exe usage:");
        output.WriteLine("  --list                List all scheduled tasks as JSON");
        output.WriteLine("  --run <task path>     Start a task, e.g. --run \"\\Backups\\Nightly\"");
        output.WriteLine("  --enable <task path>  Enable a task");
        output.WriteLine("  --disable <task path> Disable a task");
        output.WriteLine("  --help                Show this text");
        output.WriteLine("Exit code: 0 on success, 1 when the command failed, 2 when it was not understood.");
    }
}
