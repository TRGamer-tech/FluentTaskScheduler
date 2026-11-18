namespace FluentTaskScheduler;

/// <summary>
/// Deletes the files the app keeps for the user, which is what "Delete App Data" in the settings
/// asks for. Uninstalling never does this: the files are outside the installer's folder so that
/// they outlive an update or a reinstallation.
/// </summary>
/// <remarks>
/// Only the app's own entries go, from the data folder and from the folder versions before 2.0
/// kept them in. Whatever else those folders hold stays: the second one is where an installation
/// for one user has the app itself, and the data folder may be one that somebody chose. A file
/// that cannot be deleted stays as well, without a word: this runs as the last thing a process
/// does, when the log is closed and no window is left to say so.
/// </remarks>
public static class DataFolderDeletion
{
    public static void Run(AppPaths paths)
    {
        DeleteAppFiles(paths.DataFolder);
        if (paths.PublisherFolder is { } publisherFolder) AppFiles.RemoveFolderIfEmpty(publisherFolder);
        if (paths.Version19 is { } version19) DeleteAppFiles(version19.DataFolder);
    }

    private static void DeleteAppFiles(string folder)
    {
        if (!Directory.Exists(folder)) return;

        try
        {
            foreach (var (path, kind) in AppFiles.In(folder))
            {
                Delete(path, isFolder: kind is AppFileKind.Folder or AppFileKind.Scripts);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder cannot be listed, so there is nothing to tell the app's files by.
            return;
        }

        AppFiles.RemoveFolderIfEmpty(folder);
    }

    private static void Delete(string path, bool isFolder)
    {
        try
        {
            if (isFolder) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use by another program, or not the user's to delete.
        }
    }
}
