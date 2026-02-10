using System;
using System.IO;

namespace FluentTaskScheduler.Services
{
    public static class LogService
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler");
        
        public static readonly string LogPath = Path.Combine(LogFolder, "app.log");
        
        private static readonly object _lock = new();
        private const long MaxLogSize = 1 * 1024 * 1024; // 1 MB

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            var text = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write("ERROR", text);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
        {
            if (!SettingsService.EnableLogging) return;

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

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging should never crash the app
            }
        }

        public static void OpenLogFile()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = LogPath,
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
