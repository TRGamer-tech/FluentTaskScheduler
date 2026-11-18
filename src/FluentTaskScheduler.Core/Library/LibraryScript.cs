using System.Text;

namespace FluentTaskScheduler.Library;

/// <summary>A command with a name, kept in the library to become the action of new tasks.</summary>
public sealed record LibraryScript
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    public required string Command { get; init; }
    public string Arguments { get; init; } = "";
    public bool RunElevated { get; init; }

    /// <summary>True for a script that ships with the app; those cannot be changed or deleted.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// True for a script written in the script editor. The library keeps its text as a file,
    /// which the command runs and the editor can open again.
    /// </summary>
    public bool HasText { get; init; }

    /// <summary>The script as a task that runs it. A script stands for no schedule of its own.</summary>
    public TaskTemplate ToTemplate() => new()
    {
        Name = Name,
        Description = Description,
        Command = Command,
        Arguments = Arguments,
        RunElevated = RunElevated
    };
}

/// <summary>PowerShell script files as Windows PowerShell reads them correctly.</summary>
internal static class ScriptFile
{
    // Windows PowerShell 5.1 takes a file without a byte order mark for text in the ANSI code
    // page, which garbles every character outside of ASCII.
    private static readonly UTF8Encoding Utf8WithByteOrderMark = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Writes the script with Windows line ends, whichever kind the text came with. An existing
    /// file is replaced in one step: a task may run it at any moment and must not find half of it.
    /// </summary>
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text.ReplaceLineEndings("\r\n"), Utf8WithByteOrderMark);
        File.Move(temp, path, overwrite: true);
    }
}
