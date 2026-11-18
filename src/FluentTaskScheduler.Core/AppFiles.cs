using System.IO.Enumeration;
using FluentTaskScheduler.Settings;

namespace FluentTaskScheduler;

internal enum AppFileKind
{
    /// <summary>A file the app reads again, or keeps for the user to read.</summary>
    File,

    /// <summary>A folder of the app's; everything in it is the app's as well.</summary>
    Folder,

    /// <summary>
    /// The folder of the saved scripts. A task that was created from one of them names the
    /// script's file by its full path, so what is in this folder cannot be taken elsewhere.
    /// </summary>
    Scripts,

    /// <summary>What a save or a run that was interrupted left behind. Nothing reads it.</summary>
    Leftover
}

/// <summary>
/// The files and folders the app keeps directly in its data folder, told by their names. That
/// folder is not always the app's alone: versions before 2.0 kept them where an installation for
/// one user also has the app itself, and the environment variable can name any folder. So what is
/// done to the app's files as a whole is done to these entries, and to nothing else in the folder.
/// </summary>
internal static class AppFiles
{
    // A star stands for any text. A new kind of file in the data folder belongs on this list.
    private static readonly string[] Files =
    [
        "settings.json", "settings.unreadable.json",
        "snooze.json", "snooze.unreadable-*.json",
        StartWithWindowsNote.FileName,
        // Version 1.9: its snooze state and its list of saved scripts.
        "task_snoozes.json", "snooze_suppressed.json", "user_templates.json",
        // The logs with their numbered parts, and the part version 1.9 set aside when a log was full.
        "App_Log*.txt", "Error_Log*.txt", "Crash_Log*.txt",
        "App_Log.txt.old", "Error_Log.txt.old", "Crash_Log.txt.old"
    ];

    // The last two are the copy the script editor runs a script from.
    private static readonly string[] Leftovers = ["settings.json.tmp", "snooze.json.tmp", "script-run-*.ps1", "script-run-*.ps1.tmp"];

    /// <summary>
    /// The app's own among the entries directly in <paramref name="folder"/>, as a list that is
    /// complete before the caller moves or deletes the first of them.
    /// </summary>
    public static List<(string Path, AppFileKind Kind)> In(string folder)
    {
        var own = new List<(string, AppFileKind)>();
        foreach (string path in Directory.EnumerateFileSystemEntries(folder))
        {
            if (KindOf(path) is { } kind) own.Add((path, kind));
        }
        return own;
    }

    private static AppFileKind? KindOf(string path)
    {
        string name = Path.GetFileName(path);
        if (Directory.Exists(path))
        {
            return Is("Scripts", name) ? AppFileKind.Scripts
                : Is("ExecutionHistory", name) ? AppFileKind.Folder
                : null;
        }

        if (Leftovers.Any(leftover => Is(leftover, name))) return AppFileKind.Leftover;
        return Files.Any(file => Is(file, name)) ? AppFileKind.File : null;
    }

    /// <summary>For a folder that the app's files have left: it is not left behind empty.</summary>
    public static void RemoveFolderIfEmpty(string folder)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use, gone already or no longer empty: then it stays, and an empty folder is in nobody's way.
        }
    }

    private static bool Is(string pattern, string name) => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);
}
