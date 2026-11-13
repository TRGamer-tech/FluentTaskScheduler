using System;
using System.IO;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Where the app keeps its own files: settings, logs, saved scripts and the records of what it
    /// has snoozed. Everything derives from one folder, so a test (or a portable copy) can point
    /// the whole app somewhere else.
    /// </summary>
    public sealed class AppPaths
    {
        /// <summary>Environment variable that overrides the data folder.</summary>
        public const string DataFolderVariable = "FLUENTTASKSCHEDULER_DATA";

        public AppPaths()
            : this(Environment.GetEnvironmentVariable(DataFolderVariable) is { Length: > 0 } overridden
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler"))
        {
        }

        public AppPaths(string dataFolder) => DataFolder = dataFolder;

        public string DataFolder { get; }
        public string SettingsFile => Path.Combine(DataFolder, "settings.json");
        public string ScriptsFolder => Path.Combine(DataFolder, "Scripts");
        public string ExecutionHistoryFolder => Path.Combine(DataFolder, "ExecutionHistory");

        /// <summary>A file directly in the data folder.</summary>
        public string File(string name) => Path.Combine(DataFolder, name);
    }
}
