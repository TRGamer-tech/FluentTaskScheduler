using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentTaskScheduler.Localization;
using Serilog;

namespace FluentTaskScheduler.Snoozing;

/// <summary>Everything the state file holds.</summary>
internal sealed record SnoozeFileContent(SnoozeLedger Ledger, ValueList<SuppressedRun> SuppressedRuns);

/// <summary>
/// The one file all snooze state is kept in. It is always replaced as a whole.
/// <para>
/// The file has one owner, the app's <see cref="SnoozeService"/>, and nobody else writes it. To
/// the owner a file that cannot be read is never "nothing is snoozed": the next save would then
/// erase the only record of the tasks this app disabled, and they would stay disabled for good.
/// A file with damaged content is set aside under another name and reported, so the app starts
/// without snoozes but the record survives. A file that cannot be opened, or cannot be set
/// aside, stays where it is, and this instance refuses to write over it; the next start may
/// well be able to read it.
/// </para>
/// </summary>
internal sealed class SnoozeStateFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private bool _holdsUnreadableState;

    public SnoozeStateFile(string path) => _path = path;

    private enum ReadResult
    {
        NoFile,
        Content,
        Damaged,
        Inaccessible
    }

    /// <summary>Reads the stored content without changing anything on disk. Any process may do this at any time.</summary>
    /// <param name="content">The content; null when there is no file yet, or when it could not be read.</param>
    /// <returns>False when there is a file but it could not be read.</returns>
    public bool TryRead(out SnoozeFileContent? content) => Read(out content) is ReadResult.NoFile or ReadResult.Content;

    /// <summary>
    /// The read of the file's owner. If the file could not be read, <paramref name="unreadableFile"/>
    /// says where it is now: set aside if its content is damaged, in its place otherwise.
    /// </summary>
    public SnoozeFileContent? ReadForOwner(DateTimeOffset now, out string? unreadableFile)
    {
        unreadableFile = Read(out SnoozeFileContent? content) switch
        {
            ReadResult.Damaged => SetAside(now),
            ReadResult.Inaccessible => LeaveInPlace(),
            _ => null
        };
        return content;
    }

    /// <exception cref="IOException">The file could not be written, or it holds unreadable state that must not be lost.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public void Write(SnoozeFileContent content)
    {
        // The message is shown to the user as the reason a snooze did not start.
        if (_holdsUnreadableState) throw new IOException(Loc.Format("Snooze.Error.RecordUnreadable", _path));

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content, Json)));
            // On the disk before it takes the place of the old file: after a power cut the file is
            // either the old or the new one, not an empty one.
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, _path, overwrite: true);
    }

    /// <summary>
    /// Reads a file while leaving it free for everything: a second process that looks at a file
    /// must not make its owner's replacing of that file fail.
    /// </summary>
    public static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ReadResult Read(out SnoozeFileContent? content)
    {
        content = null;
        if (!File.Exists(_path)) return ReadResult.NoFile;

        try
        {
            content = Parse(ReadText(_path));
            if (content is not null) return ReadResult.Content;

            Log.Error("The snooze state in {Path} is damaged", _path);
            return ReadResult.Damaged;
        }
        catch (FileNotFoundException)
        {
            return ReadResult.NoFile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "The snooze state in {Path} could not be opened", _path);
            return ReadResult.Inaccessible;
        }
    }

    private string SetAside(DateTimeOffset now)
    {
        string stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string kept = Path.ChangeExtension(_path, $".unreadable-{stamp}.json");
        try
        {
            File.Move(_path, kept, overwrite: true);
            return kept;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "The damaged snooze state {Path} could not be set aside", _path);
            return LeaveInPlace();
        }
    }

    private string LeaveInPlace()
    {
        _holdsUnreadableState = true;
        return _path;
    }

    /// <summary>Null when the text is not a complete state file, whatever the reason.</summary>
    private static SnoozeFileContent? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SnoozeFileContent>(json, Json) is
            {
                Ledger: { Session: not null, Tasks: { } tasks },
                SuppressedRuns: { } runs
            } content
                && tasks.All(task => task is { Path.Length: > 0, FailedRestore: null or { TaskPath: not null, Detail: not null } })
                && runs.All(run => run is { TaskPath: not null })
                ? content
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
