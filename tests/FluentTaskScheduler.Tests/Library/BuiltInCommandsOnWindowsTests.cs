using System.IO.Compression;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>
/// Runs the built-in commands that delete or move files for real, on folders made for the test,
/// to see that they do what their descriptions say and nothing more. Windows only.
/// </summary>
public sealed class BuiltInCommandsOnWindowsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static TaskTemplate Template(string id) => BuiltInTemplates.Create().Single(template => template.Id == id).Template;

    private string NewFolder(string name) => Directory.CreateDirectory(_folder.File(name)).FullName;

    private static string NewFile(string folder, string name, int daysOld = 0)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysOld));
        return path;
    }

    [SkippableFact]
    public async Task TheTempCleanupTemplate_DeletesOldFiles_KeepsNewOnes_AndStaysInsideTheTempFolder()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        string temp = NewFolder("temp"), elsewhere = NewFolder("elsewhere");
        string old = NewFile(temp, "old.txt", daysOld: 10);
        string oldInSubfolder = NewFile(Directory.CreateDirectory(Path.Combine(temp, "sub")).FullName, "old.txt", daysOld: 8);
        string recent = NewFile(temp, "recent.txt", daysOld: 6);
        string behindLink = NewFile(elsewhere, "old.txt", daysOld: 10);
        // A link in the temp folder to a folder that is not temporary at all.
        string link = Path.Combine(temp, "link");
        await WindowsPowerShell.RunAsync("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"");
        Assert.True(Directory.Exists(link));
        TaskTemplate template = Template("TempCleanup");

        try
        {
            var (exitCode, output) = await WindowsPowerShell.RunAsync(template.Command, template.Arguments, ("TEMP", temp));

            Assert.True(exitCode == 0, output);
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(oldInSubfolder));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(behindLink));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [SkippableFact]
    public async Task TheLogRotationTemplate_ArchivesTheLogsOfEarlierDays_KeepsTodays_AndDropsOldArchives()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        string logs = NewFolder("logs"), archive = NewFolder(Path.Combine("logs", "archive"));
        string yesterday = NewFile(logs, "yesterday.log", daysOld: 1);
        string lastWeek = NewFile(logs, "last week.log", daysOld: 7);
        string today = NewFile(logs, "today.log");
        string notALog = NewFile(logs, "notes.txt", daysOld: 7);
        string oldArchive = NewFile(archive, "logs-old.zip", daysOld: 40);
        string recentArchive = NewFile(archive, "logs-recent.zip", daysOld: 5);
        TaskTemplate template = Template("LogRotation");

        var (exitCode, output) = await WindowsPowerShell.RunAsync(template.Command, template.Arguments.Replace(@"C:\Logs", logs));

        Assert.True(exitCode == 0, output);
        Assert.False(File.Exists(yesterday));
        Assert.False(File.Exists(lastWeek));
        Assert.True(File.Exists(today));
        Assert.True(File.Exists(notALog));
        Assert.False(File.Exists(oldArchive));
        Assert.True(File.Exists(recentArchive));

        string created = Assert.Single(Directory.GetFiles(archive, "logs-2*.zip"));
        using ZipArchive zip = ZipFile.OpenRead(created);
        Assert.Equal(["last week.log", "yesterday.log"], zip.Entries.Select(entry => entry.FullName).Order());
    }

    [SkippableFact]
    public async Task TheLogRotationTemplate_Fails_AndCreatesNothing_WhenTheLogFolderDoesNotExist()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        string missing = _folder.File("no such folder");
        TaskTemplate template = Template("LogRotation");

        var (exitCode, _) = await WindowsPowerShell.RunAsync(template.Command, template.Arguments.Replace(@"C:\Logs", missing));

        Assert.NotEqual(0, exitCode);
        Assert.False(Directory.Exists(missing));
    }

    [SkippableFact]
    public async Task TheBackupTemplate_MirrorsTheFolder_WithoutBuildOutput_AndReportsSuccess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        string source = NewFolder("projects"), target = _folder.File("backup");
        NewFile(source, "app.cs");
        NewFile(Directory.CreateDirectory(Path.Combine(source, "node_modules")).FullName, "package.js");
        NewFile(Directory.CreateDirectory(Path.Combine(source, ".git")).FullName, "HEAD");
        TaskTemplate template = Template("ProjectBackup");
        string arguments = template.Arguments.Replace(@"C:\Projects", source).Replace(@"D:\Backups\Projects", target);

        var (exitCode, output) = await WindowsPowerShell.RunAsync(template.Command, arguments);

        // robocopy itself ends with 1 here: files were copied.
        Assert.True(exitCode == 0, output);
        Assert.True(File.Exists(Path.Combine(target, "app.cs")));
        Assert.True(File.Exists(Path.Combine(target, ".git", "HEAD")));
        Assert.False(Directory.Exists(Path.Combine(target, "node_modules")));
    }

    [SkippableFact]
    public async Task TheBackupTemplate_ReportsFailure_WhenThereIsNothingToCopyFrom()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        TaskTemplate template = Template("ProjectBackup");
        string arguments = template.Arguments.Replace(@"C:\Projects", _folder.File("no such folder")).Replace(@"D:\Backups\Projects", _folder.File("backup"));

        var (exitCode, _) = await WindowsPowerShell.RunAsync(template.Command, arguments);

        Assert.Equal(16, exitCode);
    }

    [SkippableFact]
    public async Task ClearingTheTempFolder_RemovesFilesAndFolders_SkipsWhatIsInUse_AndIsNoFailure()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        string temp = NewFolder("temp");
        string file = NewFile(temp, "file.tmp");
        string subfolder = Directory.CreateDirectory(Path.Combine(temp, "sub", "deeper")).FullName;
        NewFile(subfolder, "file.tmp");
        string inUse = NewFile(temp, "in use.tmp");

        int exitCode;
        using (new FileStream(inUse, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (exitCode, _) = await WindowsPowerShell.RunAsync(
                PowerShellCommand.Executable, $"-NoProfile -NonInteractive -Command \"{QuickAction.ClearTempScript}\"", ("TEMP", temp));
        }

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.Combine(temp, "sub")));
        Assert.True(File.Exists(inUse));
        Assert.True(Directory.Exists(temp));
    }
}
