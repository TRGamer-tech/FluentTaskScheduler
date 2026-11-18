using System.Globalization;
using FluentTaskScheduler.Settings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FluentTaskScheduler.Logging;

/// <summary>The log files the app keeps in its data folder.</summary>
public enum LogKind
{
    /// <summary>What the app did, including its warnings.</summary>
    App,

    /// <summary>What failed.</summary>
    Errors,

    /// <summary>Unhandled exceptions.</summary>
    Crashes
}

/// <summary>
/// The app's logger. What it writes follows the logging preferences: with logging on, events go
/// to the app log, and errors to a file of their own if the user wants them separate. A crash is
/// always recorded, in the crash log.
/// <para>
/// The logger is built from the data folder alone and starts out with the default preferences.
/// That lets it exist before the settings are read, and record what goes wrong while they are.
/// </para>
/// </summary>
public sealed class AppLogging
{
    private const string LineFormat = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private const long DefaultMaxFileBytes = 1024 * 1024;

    private readonly string _folder;
    private volatile bool _enabled;
    private volatile bool _errorsSeparate;

    public AppLogging(AppPaths paths) : this(paths, DefaultMaxFileBytes)
    {
    }

    internal AppLogging(AppPaths paths, long maxFileBytes)
    {
        _folder = paths.DataFolder;
        Apply(new AppSettings());

        Logger = new LoggerConfiguration()
            .WriteTo.Logger(log => WriteFile(log, LogKind.App, maxFileBytes, e => _enabled && (e.Level < LogEventLevel.Error || !_errorsSeparate)))
            .WriteTo.Logger(log => WriteFile(log, LogKind.Errors, maxFileBytes, e => _enabled && _errorsSeparate && e.Level == LogEventLevel.Error))
            .WriteTo.Logger(log => WriteFile(log, LogKind.Crashes, maxFileBytes, e => e.Level == LogEventLevel.Fatal))
            .CreateLogger();
    }

    /// <summary>Writes the log files. Dispose it to close them.</summary>
    public Logger Logger { get; }

    /// <summary>Applies the logging preferences as they are now and whenever they change.</summary>
    public void Follow(ISettingsStore settings)
    {
        Apply(settings.Current);
        settings.Changed += (_, change) => Apply(change.Current);
    }

    /// <summary>The file that <paramref name="kind"/> of event is currently written to; null when none has been written yet.</summary>
    public string? CurrentFile(LogKind kind)
    {
        if (!Directory.Exists(_folder)) return null;

        string stem = FileStem(kind);
        return Directory.EnumerateFiles(_folder, stem + "*.txt")
            .Select(file => (file, part: PartNumber(Path.GetFileNameWithoutExtension(file)[stem.Length..])))
            .Where(log => log.part is not null)
            .OrderByDescending(log => log.part)
            .Select(log => log.file)
            .FirstOrDefault();
    }

    // A full file is not emptied: the logger carries on in App_Log_001.txt, then App_Log_002.txt,
    // and deletes the oldest. The part with the highest number is the one being written.
    private static int? PartNumber(string suffix)
    {
        if (suffix.Length == 0) return 0;
        return suffix[0] == '_' && int.TryParse(suffix.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int part) ? part : null;
    }

    private void Apply(AppSettings settings)
    {
        _enabled = settings.EnableLogging;
        _errorsSeparate = settings.SeparateLogFiles;
    }

    private void WriteFile(LoggerConfiguration log, LogKind kind, long maxFileBytes, Func<LogEvent, bool> accepts) =>
        log.Filter.ByIncludingOnly(accepts).WriteTo.File(
            Path.Combine(_folder, FileStem(kind) + ".txt"),
            outputTemplate: LineFormat,
            formatProvider: CultureInfo.InvariantCulture,
            fileSizeLimitBytes: maxFileBytes,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 2);

    private static string FileStem(LogKind kind) => kind switch
    {
        LogKind.Errors => "Error_Log",
        LogKind.Crashes => "Crash_Log",
        _ => "App_Log"
    };
}
