using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentTaskScheduler.Settings;

/// <summary>
/// Settings in a JSON file. Changes are written after a short pause, so a burst of them (dragging
/// the window border) costs one write, and always by replacing the file as a whole.
/// </summary>
public sealed class SettingsStore : ISettingsStore, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly ITimer _saveTimer;
    private volatile AppSettings _current;
    private bool _dirty;

    public SettingsStore(AppPaths paths, TimeProvider? time = null)
    {
        _path = paths.SettingsFile;
        _saveTimer = (time ?? TimeProvider.System).CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _current = Load();
    }

    public AppSettings Current => _current;

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Where a settings file that could not be read was moved when the store started with
    /// defaults instead; null if that did not happen.
    /// </summary>
    public string? UnreadableFileBackup { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings previous, current;
        lock (_gate)
        {
            previous = _current;
            current = change(previous).Normalized();
            if (current == previous) return;
            _current = current;
            _dirty = true;
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, current));
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            try
            {
                WriteWhole(_path, JsonSerializer.Serialize(_current, Json));
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Stays dirty: the next change or the next Flush tries again.
                Serilog.Log.Error(ex, "Settings could not be saved to {Path}", _path);
            }
        }
    }

    public void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(_current, Json));

    public void Import(string path)
    {
        AppSettings imported = Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException("The file does not contain settings of this app.");

        Update(current => imported with
        {
            WindowWidth = current.WindowWidth,
            WindowHeight = current.WindowHeight,
            LastFolderPath = current.LastFolderPath
        });
        Flush();
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }

    private AppSettings Load()
    {
        string json;
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "Settings could not be read from {Path}; using defaults for this session", _path);
            return new AppSettings();
        }

        if (Parse(json) is { } settings) return settings;

        // Keep what was there: the next save would otherwise overwrite the only copy.
        try
        {
            string backup = Path.ChangeExtension(_path, ".unreadable.json");
            File.Move(_path, backup, overwrite: true);
            UnreadableFileBackup = backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The unreadable settings file {Path} could not be set aside", _path);
        }
        return new AppSettings();
    }

    /// <summary>Null when the text is not a settings file: no JSON object, wrong value types, or none of the known settings in it.</summary>
    private static AppSettings? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var known = typeof(AppSettings).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (!document.RootElement.EnumerateObject().Any(property => known.Contains(property.Name))) return null;

            return document.RootElement.Deserialize<AppSettings>(Json)?.Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteWhole(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
