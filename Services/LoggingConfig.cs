using System;
using System.Diagnostics;
using System.IO;
using FluentTaskScheduler.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Bootstraps the app-wide Serilog pipeline (replaces the old hand-rolled LogService) and
    /// exposes the log file paths/open-file helpers that Settings still needs.
    ///
    /// Behavior preserved from the old LogService:
    /// - Info/Warning/Error go to App_Log.txt only while Settings.EnableLogging is on
    ///   (checked live on every event); App_Log.txt rolls over at ~1 MB.
    /// - Error also always (regardless of EnableLogging) goes to Error_Log.txt (unrotated)
    ///   and the Windows Event Log; it's additionally excluded from App_Log.txt when
    ///   Settings.SeparateLogFiles is on.
    /// - Fatal (crashes) always goes to Crash_Log.txt (unrotated) and the Windows Event Log,
    ///   and never to App_Log.txt.
    /// </summary>
    public static class LoggingConfig
    {
        public static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler");

        public static readonly string LogPath      = Path.Combine(LogFolder, "App_Log.txt");
        public static readonly string ErrorLogPath = Path.Combine(LogFolder, "Error_Log.txt");
        public static readonly string CrashLogPath = Path.Combine(LogFolder, "Crash_Log.txt");

        private const long MaxAppLogSizeBytes = 1 * 1024 * 1024; // 1 MB
        private const string EventSourceName = "FluentTaskScheduler";
        private const string OutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        public static void Initialize()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(IsAppLogEvent)
                    .WriteTo.File(LogPath,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: MaxAppLogSizeBytes,
                        retainedFileCountLimit: 2,
                        rollingInterval: RollingInterval.Infinite,
                        outputTemplate: OutputTemplate))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Error)
                    .WriteTo.File(ErrorLogPath, rollingInterval: RollingInterval.Infinite, outputTemplate: OutputTemplate))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Fatal)
                    .WriteTo.File(CrashLogPath, rollingInterval: RollingInterval.Infinite, outputTemplate: OutputTemplate + "{NewLine}"))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
                    .WriteTo.Sink(new FallbackEventLogSink(EventSourceName)))
                .CreateLogger();
        }

        private static bool IsAppLogEvent(LogEvent e)
        {
            var settings = App.Container.GetRequiredService<ISettingsService>();
            if (!settings.EnableLogging) return false;
            if (e.Level == LogEventLevel.Fatal) return false; // crashes never go to App_Log.txt
            if (e.Level == LogEventLevel.Error && settings.SeparateLogFiles) return false;
            return true;
        }

        public static void OpenLogFile() => OpenFileInternal(LogPath);
        public static void OpenErrorLog() => OpenFileInternal(ErrorLogPath);
        public static void OpenCrashLog() => OpenFileInternal(CrashLogPath);

        private static void OpenFileInternal(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                else if (Directory.Exists(LogFolder))
                {
                    Process.Start(new ProcessStartInfo { FileName = LogFolder, UseShellExecute = true });
                }
            }
            catch
            {
                // Opening the log should never crash the app.
            }
        }
    }
}
