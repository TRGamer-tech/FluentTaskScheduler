using System;
using System.Diagnostics;
using System.IO;

namespace FluentTaskScheduler.Services
{
    public static class LogService
    {
        public static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler");

        public static readonly string LogPath      = Path.Combine(LogFolder, "App_Log.txt");
        public static readonly string ErrorLogPath = Path.Combine(LogFolder, "Error_Log.txt");
        public static readonly string CrashLogPath = Path.Combine(LogFolder, "Crash_Log.txt");

        private static readonly object _lock = new();
        private const long MaxLogSize = 1 * 1024 * 1024; // 1 MB
        private const string EventSource = "FluentTaskScheduler";

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null)
        {
            var text = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write("ERROR", text);
            AppendToFile(ErrorLogPath, FormatLine("ERROR", text));
            WriteToEventLog(text, EventLogEntryType.Warning);
        }

        /// <summary>Logs an unhandled exception to Crash_Log.txt and the Windows Event Log.</summary>
        public static void WriteCrash(Exception? ex, string source)
        {
            var text = $"[{source}] {ex?.GetType().Name}: {ex?.Message}{Environment.NewLine}Stack Trace: {ex?.StackTrace ?? "No stack"}";
            AppendToFile(CrashLogPath, FormatLine("CRASH", text) + Environment.NewLine);
            WriteToEventLog(text, EventLogEntryType.Error);
        }

        private static string FormatLine(string level, string message)
            => $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        private static void Write(string level, string message)
        {
            if (!SettingsService.EnableLogging) return;

            // If separate logs are enabled, errors don't go into App_Log.txt
            if (SettingsService.SeparateLogFiles && (level == "ERROR" || level == "CRASH")) return;

            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogFolder))
                        Directory.CreateDirectory(LogFolder);

                    // Rotate if too large
                    if (File.Exists(LogPath))
                    {
                        var info = new FileInfo(LogPath);
                        if (info.Length > MaxLogSize)
                        {
                            string backup = LogPath + ".old";
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(LogPath, backup);
                        }
                    }

                    File.AppendAllText(LogPath, FormatLine(level, message) + Environment.NewLine);
                }
            }
            catch
            {
                // Logging should never crash the app
            }
        }

        private static void AppendToFile(string path, string line)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogFolder))
                        Directory.CreateDirectory(LogFolder);
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { }
        }

        private static void WriteToEventLog(string message, EventLogEntryType type)
        {
            try
            {
                // Try registering a named source (requires admin on first run)
                try
                {
                    if (!EventLog.SourceExists(EventSource))
                        EventLog.CreateEventSource(EventSource, "Application");
                    EventLog.WriteEntry(EventSource, message, type);
                }
                catch
                {
                    // Source registration needs admin; fall back to the built-in Application source
                    using var log = new EventLog("Application") { Source = "Application" };
                    log.WriteEntry($"[{EventSource}] {message}", type);
                }
            }
            catch { }
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
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    // Open the folder instead
                    if (Directory.Exists(LogFolder))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = LogFolder,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch { }
        }
    }
}
