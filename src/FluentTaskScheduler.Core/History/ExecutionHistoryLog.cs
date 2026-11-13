using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentTaskScheduler.Settings;
using Serilog;

namespace FluentTaskScheduler.History;

/// <summary>
/// Keeps a copy of every finished run in files of the app's own, for as long as the setting
/// <see cref="AppSettings.EnableExecutionHistoryLog"/> is on. Windows' own history is limited in
/// size and overwrites itself; this copy survives that. It can only record what Task Scheduler
/// logs, so it stands still while that log is switched off and while the app is not running.
/// </summary>
/// <remarks>
/// One file per month, named runs-yyyy-MM.jsonl after the month (in UTC) in which the run ended,
/// with one JSON object per line. The newest <see cref="MonthsKept"/> files are kept; older ones
/// are deleted when a new month begins.
/// </remarks>
public sealed class ExecutionHistoryLog : IDisposable
{
    public const int MonthsKept = 12;

    private const string FilePattern = "runs-*.jsonl";

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly ITaskEventStream _stream;
    private readonly ISettingsStore _settings;
    private readonly string _folder;
    private string? _newestFile;

    public ExecutionHistoryLog(ITaskEventStream stream, ISettingsStore settings, AppPaths paths)
    {
        _stream = stream;
        _settings = settings;
        _folder = paths.ExecutionHistoryFolder;
        _stream.RunCompleted += OnRunCompleted;
    }

    public void Dispose() => _stream.RunCompleted -= OnRunCompleted;

    private void OnRunCompleted(object? sender, TaskRun run)
    {
        if (!_settings.Current.EnableExecutionHistoryLog || run.Ended is not { } ended) return;

        string file = Path.Combine(_folder, $"runs-{ended.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)}.jsonl");
        var entry = new Entry(ended.ToUniversalTime(), run.Started?.ToUniversalTime(), run.TaskPath, run.Status, run.ResultCode, run.User, run.InstanceId);
        try
        {
            Directory.CreateDirectory(_folder);
            File.AppendAllText(file, JsonSerializer.Serialize(entry, Json) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "The run of {Task} that ended {Ended} could not be written to the execution history {File}", run.TaskPath, ended, file);
            return;
        }

        if (file != _newestFile)
        {
            _newestFile = file;
            DeleteOldFiles();
        }
    }

    /// <summary>The month is part of the name in a form that sorts by time, so the names alone tell which files are the newest.</summary>
    private void DeleteOldFiles()
    {
        var old = Directory.EnumerateFiles(_folder, FilePattern)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MonthsKept)
            .ToList();
        foreach (string file in old)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "The old execution history file {File} could not be deleted", file);
            }
        }
    }

    // Property names are the stored format.
    private sealed record Entry(DateTimeOffset Ended, DateTimeOffset? Started, string Task, RunStatus Result, uint? ResultCode, string User, Guid? InstanceId);
}
