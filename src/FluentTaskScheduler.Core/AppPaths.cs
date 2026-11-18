namespace FluentTaskScheduler;

/// <summary>
/// Where the app keeps its own files: settings, logs, saved scripts and the records of what it
/// has snoozed. Everything derives from one folder, so a test (or a portable copy) can point
/// the whole app somewhere else.
/// </summary>
public sealed class AppPaths
{
    /// <summary>Environment variable that overrides the data folder.</summary>
    public const string DataFolderVariable = "FLUENTTASKSCHEDULER_DATA";

    /// <summary>The paths of the Windows account the app runs as, unless the environment variable names a data folder.</summary>
    public AppPaths()
        : this(Environment.GetEnvironmentVariable(DataFolderVariable), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    /// <param name="overridden">What the environment variable says; null or empty when it is not set.</param>
    /// <param name="localAppData">Where Windows has the account keep what its programs store on this computer.</param>
    internal AppPaths(string? overridden, string localAppData)
    {
        if (overridden is { Length: > 0 })
        {
            DataFolder = overridden;
            return;
        }

        // Publisher, then product, as Windows programs do. It stays with the computer rather than
        // roaming with the account: nearly everything in it is about the tasks of this computer.
        PublisherFolder = Path.Combine(localAppData, "TRGamer-tech");
        DataFolder = Path.Combine(PublisherFolder, "FluentTaskScheduler");
        Version19 = new AppPaths(Path.Combine(localAppData, "FluentTaskScheduler"));
    }

    public AppPaths(string dataFolder) => DataFolder = dataFolder;

    public string DataFolder { get; }
    public string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public string ScriptsFolder => Path.Combine(DataFolder, "Scripts");
    public string ExecutionHistoryFolder => Path.Combine(DataFolder, "ExecutionHistory");

    /// <summary>A file directly in the data folder.</summary>
    public string File(string name) => Path.Combine(DataFolder, name);

    /// <summary>
    /// Where versions before 2.0 kept these files: in the folder that an installation for one
    /// user also puts the app itself into, so that uninstalling the app removed them with it.
    /// <see cref="DataFolderMove"/> brings them over from there. Null for a data folder that
    /// somebody chose: that one never was anywhere else.
    /// </summary>
    internal AppPaths? Version19 { get; }

    /// <summary>The folder above the data folder, which is there for it alone; null for a data folder that somebody chose.</summary>
    internal string? PublisherFolder { get; }
}
