using System.Text.Json;
using System.Text.RegularExpressions;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Library;

/// <summary>
/// The scripts of the library: those that ship with the app, and those the user saved. The
/// user's scripts live in one folder, as PowerShell files next to a list of their names. Nothing
/// outside of that folder is ever written or deleted, with one exception: a script that a
/// version before 2.0 saved also stays in the folder that version kept it in, where the tasks
/// created from it back then still run it. That copy is kept the same as the script, and goes
/// when the script goes.
///
/// Every call reads the list from disk, so there is no state to go stale. Reading and writing
/// can fail with an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class ScriptStore
{
    private const string ScriptExtension = ".ps1";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static readonly Regex FileArgument = new("-File\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _folder;
    private readonly string _listFile;
    private readonly string _version19ListFile;
    private readonly string? _version19Folder;
    private readonly Lazy<IReadOnlyList<BuiltIn>> _builtIn;

    /// <param name="builtInScriptsFile">The JSON file with the scripts that ship with the app.</param>
    public ScriptStore(AppPaths paths, string builtInScriptsFile)
    {
        _folder = paths.ScriptsFolder;
        _listFile = Path.Combine(_folder, "scripts.json");
        _version19ListFile = paths.File("user_templates.json");
        _version19Folder = paths.Version19?.ScriptsFolder;
        _builtIn = new(() => ReadBuiltIn(builtInScriptsFile));
    }

    // Property names are the stored format, here and in Entry.
    private sealed record BuiltIn
    {
        public string Id { get; init; } = "";
        public string Command { get; init; } = "";
        public string Arguments { get; init; } = "";
        public bool RunAsAdmin { get; init; }
    }

    /// <summary>
    /// One of the user's scripts. A script written in the editor has <see cref="File"/>, the
    /// name of its text in the scripts folder. An entry brought over from version 1.9 can instead
    /// be a plain command line, which is kept as it is.
    /// </summary>
    private sealed record Entry
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public bool RunAsAdmin { get; init; }
        public string? File { get; init; }
        public string? Command { get; init; }
        public string? Arguments { get; init; }
    }

    /// <summary>Raised after a script was saved or deleted.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Where a list of scripts that could not be read was moved, when the library started over
    /// with an empty one instead; null if that did not happen.
    /// </summary>
    public string? UnreadableListBackup { get; private set; }

    /// <summary>The user's scripts followed by the built-in ones, named in the language the app shows now.</summary>
    public IReadOnlyList<LibraryScript> GetScripts() =>
    [
        .. ReadList().Select(ToScript).OfType<LibraryScript>(),
        .. _builtIn.Value.Select(script => new LibraryScript
        {
            Id = script.Id,
            Name = Loc.Get($"Scripts.{script.Id}.Name"),
            Description = Loc.Get($"Scripts.{script.Id}.Description"),
            Command = script.Command,
            Arguments = script.Arguments,
            RunElevated = script.RunAsAdmin,
            IsBuiltIn = true
        })
    ];

    /// <summary>The text of a script written in the editor.</summary>
    /// <exception cref="FileNotFoundException">The library has no such script (any more), or its file is gone.</exception>
    public string ReadText(string id)
    {
        Entry? entry = ReadList().Find(candidate => candidate.Id == id);
        return entry is not null && TextFileOf(entry) is { } file
            ? File.ReadAllText(file)
            : throw new FileNotFoundException(null, id);
    }

    /// <summary>
    /// Saves a script written in the editor: a new one, or the new name, description and text of
    /// the one saved as <paramref name="id"/>. Tasks created from a script run its file, so they
    /// run the new text from then on. An id the library does not have (any more) makes a new script.
    /// </summary>
    public LibraryScript Save(string? id, string name, string description, string text)
    {
        List<Entry> entries = ReadList();
        int position = id is null ? -1 : entries.FindIndex(candidate => candidate.Id == id && TextFileOf(candidate) is not null);

        Entry entry = (position < 0 ? NewEntry() : entries[position]) with { Name = name, Description = description };

        ScriptFile.Write(TextFileOf(entry)!, text);
        if (CopyThatOlderTasksRun(entry) is { } copy) ScriptFile.Write(copy, text);
        if (position < 0) entries.Add(entry);
        else entries[position] = entry;
        WriteList(entries);

        Changed?.Invoke(this, EventArgs.Empty);
        return ToScript(entry)!;
    }

    /// <summary>Removes one of the user's scripts, together with its file. Built-in scripts stay.</summary>
    public void Delete(string id)
    {
        List<Entry> entries = ReadList();
        Entry? entry = entries.Find(candidate => candidate.Id == id);
        if (entry is null) return;

        entries.Remove(entry);
        WriteList(entries);

        if (TextFileOf(entry) is { } file) RemoveFileOfDeletedScript(file);
        if (CopyThatOlderTasksRun(entry) is { } copy) RemoveFileOfDeletedScript(copy);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void RemoveFileOfDeletedScript(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The script is off the list either way; a file left behind does no harm.
            Serilog.Log.Warning(ex, "The file {Path} of a deleted script could not be removed", file);
        }
    }

    // The file gets a name of its own, not the script's: tasks refer to it by its path, so
    // renaming the script must not move it.
    private static Entry NewEntry()
    {
        string id = Guid.NewGuid().ToString("N");
        return new Entry { Id = id, File = id + ScriptExtension };
    }

    private LibraryScript? ToScript(Entry entry)
    {
        if (TextFileOf(entry) is { } file)
        {
            return new LibraryScript
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                Command = PowerShellCommand.Executable,
                Arguments = PowerShellCommand.ForFile(file),
                RunElevated = entry.RunAsAdmin,
                HasText = true
            };
        }

        if (string.IsNullOrWhiteSpace(entry.Command)) return null;
        return new LibraryScript
        {
            Id = entry.Id,
            Name = entry.Name,
            Description = entry.Description,
            Command = entry.Command,
            Arguments = entry.Arguments ?? "",
            RunElevated = entry.RunAsAdmin
        };
    }

    /// <summary>
    /// The full path of the entry's script file, or null if it has none. The list is a file
    /// anyone can edit: a name that leads out of the scripts folder is not followed.
    /// </summary>
    private string? TextFileOf(Entry entry) =>
        entry.File is { } name && IsScriptFileName(name) ? Path.Combine(_folder, name) : null;

    /// <summary>
    /// The entry's script as it still lies in the folder version 1.9 kept its scripts in, or null
    /// if it is not there. Tasks that were created from the script before the app's files moved
    /// name that file.
    /// </summary>
    private string? CopyThatOlderTasksRun(Entry entry)
    {
        if (_version19Folder is null || TextFileOf(entry) is null) return null;

        string copy = Path.Combine(_version19Folder, entry.File!);
        return File.Exists(copy) ? copy : null;
    }

    private static bool IsScriptFileName(string name) =>
        name.Length > ScriptExtension.Length
        && name.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(['/', '\\', ':']) < 0;

    private List<Entry> ReadList()
    {
        if (!File.Exists(_listFile)) return ImportFromVersion19();

        try
        {
            return Parse(File.ReadAllText(_listFile));
        }
        catch (JsonException ex)
        {
            // Keep what was there: the next save would otherwise overwrite the only record of the
            // user's scripts. An empty list takes its place, as without a list the next read would
            // take over the scripts of version 1.9 once more.
            string backup = UnusedBackupName();
            File.Move(_listFile, backup);
            UnreadableListBackup = backup;
            Serilog.Log.Error(ex, "The list of saved scripts {Path} could not be read and was set aside as {Backup}", _listFile, backup);
            WriteList([]);
            return [];
        }
    }

    /// <summary>A name for a list that cannot be read, which leaves any set aside before untouched.</summary>
    private string UnusedBackupName()
    {
        string backup = Path.ChangeExtension(_listFile, ".unreadable.json");
        for (int number = 2; File.Exists(backup); number++) backup = Path.ChangeExtension(_listFile, $".unreadable.{number}.json");
        return backup;
    }

    private static List<Entry> Parse(string json) =>
        (JsonSerializer.Deserialize<List<Entry?>>(json, Json) ?? [])
            .OfType<Entry>()
            .Where(entry => !string.IsNullOrEmpty(entry.Id))
            .Select(entry => entry with { Name = entry.Name ?? "", Description = entry.Description ?? "" })
            .ToList();

    private void WriteList(List<Entry> entries)
    {
        Directory.CreateDirectory(_folder);
        string temp = _listFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, _listFile, overwrite: true);
    }

    /// <summary>
    /// Version 1.9 kept its list one folder up and without ids, and a script written in its
    /// editor was a command line with the full path of the script file in it. That path names
    /// the folder version 1.9 kept its scripts in, which is not the scripts folder of today. The
    /// old list is left where it is; it is only read as long as there is no new one.
    /// </summary>
    private List<Entry> ImportFromVersion19()
    {
        if (!File.Exists(_version19ListFile)) return [];

        List<Entry> imported;
        try
        {
            imported = (JsonSerializer.Deserialize<List<Entry?>>(File.ReadAllText(_version19ListFile), Json) ?? [])
                .OfType<Entry>()
                .Select(FromVersion19)
                .ToList();
        }
        catch (JsonException ex)
        {
            Serilog.Log.Error(ex, "The scripts saved by version 1.9 in {Path} could not be read", _version19ListFile);
            imported = [];
        }

        WriteList(imported);
        return imported;
    }

    private Entry FromVersion19(Entry old)
    {
        var entry = old with { Id = Guid.NewGuid().ToString("N"), Name = old.Name ?? "", Description = old.Description ?? "" };

        Match match = FileArgument.Match(old.Arguments ?? "");
        if (!match.Success) return entry;

        string path = match.Groups[1].Value;
        string name = Path.GetFileName(path);
        bool isInScriptsFolder = IsScriptFileName(name)
            && Path.GetDirectoryName(path) is { } folder
            && (IsSameFolder(folder, _folder) || IsSameFolder(folder, _version19Folder))
            && File.Exists(Path.Combine(_folder, name));
        return isInScriptsFolder ? entry with { File = name, Command = null, Arguments = null } : entry;
    }

    private static bool IsSameFolder(string folder, string? other) => folder.Equals(other, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<BuiltIn> ReadBuiltIn(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<List<BuiltIn>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Serilog.Log.Error(ex, "The built-in scripts could not be read from {Path}", file);
            return [];
        }
    }
}
