using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Single place that opens Task Scheduler sessions. Sessions stay short-lived on purpose
/// (each operation opens and disposes its own), matching the previous behaviour.
/// </summary>
public class TaskSchedulerConnection
{
    public virtual TaskService Open() => new TaskService();

    internal static TaskFolder GetOrCreateFolder(TaskService ts, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "\\") return ts.RootFolder;

        try
        {
            var folder = ts.GetFolder(folderPath);
            if (folder != null) return folder;
        }
        catch (System.IO.FileNotFoundException) { }
        catch (Exception ex) { throw new Exception($"Failed to check folder '{folderPath}': {ex.Message}"); }

        int lastSlash = folderPath.LastIndexOf('\\');
        string parentPath = lastSlash > 0 ? folderPath.Substring(0, lastSlash) : "\\";
        string folderName = folderPath.Substring(lastSlash + 1);

        TaskFolder parentFolder = GetOrCreateFolder(ts, parentPath);
        try
        {
            return parentFolder.CreateFolder(folderName);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create folder '{folderName}' in '{parentPath}': {ex.Message}");
        }
    }
}
