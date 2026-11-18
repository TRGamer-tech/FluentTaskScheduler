using System.Text.Json;
using FluentTaskScheduler.Scheduling;
using Serilog;

namespace FluentTaskScheduler.Snoozing;

/// <summary>
/// Version 1.9 kept its snooze state in three places: fields inside settings.json,
/// task_snoozes.json and snooze_suppressed.json. This reads them on the first start after the
/// upgrade, so that a snooze in progress, and above all the tasks it disabled, are carried over.
/// <para>
/// An old "until restart" snooze has no end time, only a boot stamp that cannot be compared with
/// anything known here. It is taken to be over, which re-enables its tasks at this start.
/// </para>
/// </summary>
internal sealed class LegacySnoozeState
{
    private const string TaskSnoozesFile = "task_snoozes.json";
    private const string SuppressedRunsFile = "snooze_suppressed.json";

    private readonly List<string> _filesTakenOver = [];

    private LegacySnoozeState() { }

    /// <summary>What version 1.9 left behind; null if it left nothing.</summary>
    public SnoozeFileContent? Content { get; private set; }

    /// <summary>
    /// One of the two files of its own that version 1.9 kept, if it could not be read. It stays
    /// where it is: it may name tasks that are still disabled.
    /// </summary>
    public string? UnreadableFile { get; private set; }

    // The three classes hold what is needed of what version 1.9 wrote; their property names are that format.
    private sealed class OldSettings
    {
        public bool IsSnoozed { get; set; }
        public DateTimeOffset? SnoozeUntilUtc { get; set; }
        public List<string?>? SnoozeDisabledTaskPaths { get; set; }
    }

    private sealed class OldTaskSnooze
    {
        public string? TaskPath { get; set; }
        public DateTimeOffset? UntilUtc { get; set; }
        public bool WasEnabled { get; set; } = true;
    }

    private sealed class OldSuppressedRun
    {
        public DateTimeOffset TimeUtc { get; set; }
        public string? TaskPath { get; set; }
        public string? Origin { get; set; }
    }

    public static LegacySnoozeState Read(AppPaths paths)
    {
        var legacy = new LegacySnoozeState();

        // settings.json is not this class's file: the settings store rewrites it without the old fields.
        var settings = legacy.ReadJson<OldSettings>(paths.SettingsFile, ownFile: false);
        var taskSnoozes = legacy.ReadJson<List<OldTaskSnooze?>>(paths.File(TaskSnoozesFile), ownFile: true) ?? [];
        var suppressedRuns = legacy.ReadJson<List<OldSuppressedRun?>>(paths.File(SuppressedRunsFile), ownFile: true) ?? [];

        SnoozeWindow? global = settings is { IsSnoozed: true, SnoozeUntilUtc: { } until } ? new SnoozeWindow(until) : null;

        // The list also outlives its snooze when 1.9 was interrupted while re-enabling the tasks.
        var tasks = new Dictionary<string, OwnedTask>(StringComparer.OrdinalIgnoreCase);
        foreach (string? path in settings?.SnoozeDisabledTaskPaths ?? [])
        {
            if (!string.IsNullOrWhiteSpace(path)) tasks[path] = new OwnedTask(path) { ByGlobal = global is not null, Disabled = true };
        }

        foreach (OldTaskSnooze? snooze in taskSnoozes)
        {
            if (snooze is not { TaskPath: { Length: > 0 } path }) continue;

            // WasEnabled false: the task was disabled before 1.9 snoozed it, and 1.9 would have left it so.
            if (!tasks.TryGetValue(path, out OwnedTask? task) && !snooze.WasEnabled) continue;

            task ??= new OwnedTask(path) { Disabled = true };
            tasks[path] = task with { Window = snooze.UntilUtc is { } end ? new SnoozeWindow(end) : null };
        }

        var runs = new List<SuppressedRun>();
        foreach (OldSuppressedRun? run in suppressedRuns.Take(SnoozeService.MaxSuppressedRuns))
        {
            if (run is { TaskPath: { } path })
                runs.Add(new SuppressedRun(run.TimeUtc, path, run.Origin == "Pipeline" ? RunOrigin.CompletionAction : RunOrigin.Manual));
        }

        if (global is not null || tasks.Count > 0 || runs.Count > 0)
        {
            var ledger = new SnoozeLedger
            {
                Global = global,
                SuspendsTriggers = global is not null && tasks.Values.Any(task => task.ByGlobal),
                Tasks = tasks.Values.ToValueList()
            };
            legacy.Content = new SnoozeFileContent(ledger, runs);
        }
        return legacy;
    }

    /// <summary>Removes the files of version 1.9 that were read, once their content has its new home.</summary>
    public void RemoveFiles()
    {
        foreach (string file in _filesTakenOver)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "{Path} of version 1.9 could not be removed", file);
            }
        }
    }

    private T? ReadJson<T>(string path, bool ownFile) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            T? content = JsonSerializer.Deserialize<T>(SnoozeStateFile.ReadText(path));
            if (ownFile) _filesTakenOver.Add(path);
            return content;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "{Path} of version 1.9 could not be read", path);
            if (ownFile) UnreadableFile ??= path;
            return null;
        }
    }
}
