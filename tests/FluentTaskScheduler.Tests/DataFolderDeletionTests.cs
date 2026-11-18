using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests;

public sealed class DataFolderDeletionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Laid out as under %LOCALAPPDATA%: the paths of an account, as the app has them without an environment variable.
    private AppPaths Paths => new(null, _temp.Path);
    private string Version19Folder => _temp.File("FluentTaskScheduler");
    private string PublisherFolder => _temp.File("TRGamer-tech");

    private static readonly string[] AppFilesOfEveryKind =
    [
        "App_Log.txt", "App_Log_001.txt", "Crash_Log.txt", "Error_Log.txt",
        "ExecutionHistory/runs-2026-11.jsonl",
        "Scripts/0f8fad5b-d9cb-469f-a165-70867728950e.ps1", "Scripts/scripts.json",
        "settings.json", "settings.json.tmp", "snooze.json", "start-with-windows"
    ];

    /// <summary>What Velopack keeps in the folder it installs the app into for one user, and a file of the user's own.</summary>
    private static readonly string[] FilesOfOthers = ["Update.exe", "current/FluentTaskScheduler.exe", "notes.txt"];

    private static void Write(string folder, IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            string file = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "content");
        }
    }

    private static string[] FilesIn(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void TheAppsFiles_Go_AndSoDoTheFoldersThatWereThereForThemAlone()
    {
        Write(Paths.DataFolder, AppFilesOfEveryKind);

        DataFolderDeletion.Run(Paths);

        Assert.False(Directory.Exists(Paths.DataFolder));
        Assert.False(Directory.Exists(PublisherFolder));
        Assert.True(Directory.Exists(_temp.Path));
    }

    [Fact]
    public void WhatIsNotTheApps_Stays_AndTheFolderWithIt()
    {
        Write(Paths.DataFolder, AppFilesOfEveryKind);
        Write(Paths.DataFolder, ["notes.txt", "Backup/settings.json"]);

        DataFolderDeletion.Run(Paths);

        Assert.Equal(["Backup/settings.json", "notes.txt"], FilesIn(Paths.DataFolder));
    }

    [Fact]
    public void WhereVersionsBefore2KeptTheFiles_TheyGoAsWell_AndTheAppInstalledThereStays()
    {
        Write(Paths.DataFolder, ["settings.json"]);
        Write(Version19Folder, AppFilesOfEveryKind);
        Write(Version19Folder, FilesOfOthers);

        DataFolderDeletion.Run(Paths);

        Assert.Equal(FilesOfOthers.Order(StringComparer.Ordinal), FilesIn(Version19Folder));
    }

    [Fact]
    public void AFolderOfVersionsBefore2ThatHeldNothingElse_IsRemoved()
    {
        Write(Version19Folder, ["Scripts/0f8fad5b-d9cb-469f-a165-70867728950e.ps1", "user_templates.json"]);

        DataFolderDeletion.Run(Paths);

        Assert.False(Directory.Exists(Version19Folder));
    }

    [Fact]
    public void ADataFolderThatSomebodyChose_LosesTheAppsFilesOnly_AndNothingAroundItIsTouched()
    {
        string chosen = _temp.File("Chosen");
        Write(chosen, AppFilesOfEveryKind);
        Write(chosen, ["notes.txt"]);
        Write(Version19Folder, ["settings.json"]);
        Write(_temp.Path, ["beside.txt"]);

        DataFolderDeletion.Run(new AppPaths(chosen, _temp.Path));

        Assert.Equal(["notes.txt"], FilesIn(chosen));
        Assert.Equal(["settings.json"], FilesIn(Version19Folder));
        Assert.True(File.Exists(_temp.File("beside.txt")));
    }

    [Fact]
    public void WithNothingThere_NothingHappens()
    {
        DataFolderDeletion.Run(Paths);

        Assert.Empty(Directory.GetFileSystemEntries(_temp.Path));
    }

    [SkippableFact]
    public void AFileThatIsInUse_Stays_AndTheRestGoes()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        Write(Paths.DataFolder, ["App_Log.txt", "settings.json", "Scripts/scripts.json"]);

        using (File.Open(Path.Combine(Paths.DataFolder, "App_Log.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            DataFolderDeletion.Run(Paths);
        }

        Assert.Equal(["App_Log.txt"], FilesIn(Paths.DataFolder));
    }
}
