using Serilog;

namespace FluentTaskScheduler;

/// <summary>A file or folder of the app's that could not be taken to the new folder, and why.</summary>
public sealed record DataFolderMoveFailure(string Path, Exception Error);

/// <summary>What a move of the app's files did. It runs before there is a log, so the app writes this down once it has one.</summary>
/// <param name="Moved">How many files arrived in the new folder.</param>
public sealed record DataFolderMoveReport(string From, string To, int Moved, ValueList<DataFolderMoveFailure> Failures)
{
    public void WriteTo(ILogger log)
    {
        if (Moved > 0) log.Information("Moved {Count} files of the app from {From} to its data folder {To}", Moved, From, To);

        foreach (DataFolderMoveFailure failure in Failures)
        {
            log.Warning(failure.Error, "{Path} could not be moved to the data folder {To}; it stays where it is, and the next start tries again", failure.Path, To);
        }
    }
}

/// <summary>
/// Takes the app's files from the folder that versions before 2.0 kept them in to the data folder
/// of today, and leaves alone whatever else the old folder holds: for an installation for one
/// user, that is the app itself.
/// </summary>
/// <remarks>
/// <para>
/// It runs at every start, before anything reads the app's files, also when two instances start
/// at the same moment and after a start that was ended half way. Every file therefore arrives in
/// one step that has either happened or not, and never takes the place of a file that is there
/// already: of an entry that is in both folders, the one in the new folder is the one in use, and
/// the other stays where it is. What cannot be moved now is reported, and tried again by the next
/// start.
/// </para>
/// <para>
/// The saved scripts are the exception: they are copied, and stay where they were as well. A
/// task that was created from a script names the script's file by its full path, and the app is
/// not shown every task there is, so it cannot tell which script nothing runs any more.
/// </para>
/// </remarks>
public sealed class DataFolderMove
{
    private readonly List<DataFolderMoveFailure> _failures = [];
    private int _moved;

    private DataFolderMove()
    {
    }

    /// <summary>
    /// With a data folder that somebody chose, the files of the account stay where they are:
    /// whoever points the app elsewhere, a test for one, does not mean them to be touched.
    /// </summary>
    public static DataFolderMoveReport Run(AppPaths paths) =>
        paths.Version19 is { } version19
            ? Run(version19.DataFolder, paths.DataFolder)
            : new DataFolderMoveReport("", paths.DataFolder, 0, []);

    internal static DataFolderMoveReport Run(string from, string to)
    {
        var move = new DataFolderMove();
        move.TakeAppFiles(from, to);
        return new DataFolderMoveReport(from, to, move._moved, move._failures);
    }

    private void TakeAppFiles(string from, string to)
    {
        if (!Directory.Exists(from)) return;

        List<(string Path, AppFileKind Kind)> own;
        try
        {
            own = AppFiles.In(from);

            // With nothing to bring over, as after a fresh installation, no folder is made either.
            if (own.Count > 0) Directory.CreateDirectory(to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(from)) _failures.Add(new DataFolderMoveFailure(from, ex));
            return;
        }

        foreach (var (path, kind) in own)
        {
            string target = Path.Combine(to, Path.GetFileName(path));
            switch (kind)
            {
                case AppFileKind.File:
                    MoveFile(path, target);
                    break;
                case AppFileKind.Folder:
                    TakeFolder(path, target, keepOriginals: false);
                    break;
                case AppFileKind.Scripts:
                    TakeFolder(path, target, keepOriginals: true);
                    break;
                case AppFileKind.Leftover:
                    Discard(path);
                    break;
            }
        }

        RemoveIfEmpty(from);
    }

    private void TakeFolder(string from, string to, bool keepOriginals)
    {
        string[] entries;
        try
        {
            Directory.CreateDirectory(to);
            entries = Directory.GetFileSystemEntries(from);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(from)) _failures.Add(new DataFolderMoveFailure(from, ex));
            return;
        }

        foreach (string entry in entries)
        {
            string target = Path.Combine(to, Path.GetFileName(entry));
            if (Directory.Exists(entry)) TakeFolder(entry, target, keepOriginals);
            else if (keepOriginals) CopyFile(entry, target);
            else MoveFile(entry, target);
        }

        if (!keepOriginals) RemoveIfEmpty(from);
    }

    private void MoveFile(string source, string target)
    {
        if (Path.Exists(target)) return;

        try
        {
            File.Move(source, target);
            _moved++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An instance that started at the same moment may have been quicker; that is no failure.
            if (File.Exists(source) && !Path.Exists(target)) _failures.Add(new DataFolderMoveFailure(source, ex));
        }
    }

    private void CopyFile(string source, string target)
    {
        if (Path.Exists(target)) return;

        // Under another name first, so that nobody ever finds half a file under the real one.
        string unfinished = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, unfinished);
            File.Move(unfinished, target);
            _moved++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!Path.Exists(target)) _failures.Add(new DataFolderMoveFailure(source, ex));
            Discard(unfinished);
        }
    }

    private static void Discard(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It stays, and nothing reads it. If it is in the old folder, the next start tries again.
        }
    }

    private static void RemoveIfEmpty(string folder)
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
}
