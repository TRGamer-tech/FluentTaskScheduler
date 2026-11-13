using System.Diagnostics;
using System.Xml.Linq;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// A throw-away folder in the machine's Task Scheduler, reached through schtasks.exe: the same
/// door any other program uses, and independent of the app under test.
/// </summary>
public sealed class WindowsTasks : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>A folder of its own, removed again with everything in it.</summary>
    public WindowsTasks() : this(@"\FtsUiTests_" + Guid.NewGuid().ToString("N")[..8])
    {
    }

    private WindowsTasks(string folder) => Folder = folder;

    /// <summary>The top folder, where the app puts a new task when no folder is selected. Nothing is cleaned up here.</summary>
    public static WindowsTasks Root { get; } = new("");

    public string Folder { get; }

    public string PathOf(string name) => Folder + @"\" + name;

    public void Delete(string name) => TryRun($"/Delete /TN \"{PathOf(name)}\" /F", out _);

    /// <summary>Registers a task from task XML, the way an export from another machine would be.</summary>
    public void Create(string name, string xml)
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            Run($"/Create /TN \"{PathOf(name)}\" /XML \"{file}\" /F");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Registers a task that runs a command once a day, at night. The default command does nothing.</summary>
    /// <param name="command">Must not contain quotation marks.</param>
    public void CreateDaily(string name, string command = "cmd.exe /c exit 0") =>
        Run($"/Create /TN \"{PathOf(name)}\" /TR \"{command}\" /SC DAILY /ST 03:00 /F");

    public bool Exists(string name) => TryRun($"/Query /TN \"{PathOf(name)}\"", out _);

    /// <summary>The task's definition as Task Scheduler stores it.</summary>
    public XElement Definition(string name)
    {
        string xml = Run($"/Query /TN \"{PathOf(name)}\" /XML");
        return XElement.Parse(xml[xml.IndexOf("<Task", StringComparison.Ordinal)..]);
    }

    /// <summary>The text of the element at <paramref name="path"/> below the task element, or null if it is not there.</summary>
    public string? Value(string name, params string[] path)
    {
        XElement? element = Definition(name);
        foreach (string step in path) element = element?.Element(Ns + step);
        return element?.Value;
    }

    public XElement? Element(string name, params string[] path)
    {
        XElement? element = Definition(name);
        foreach (string step in path) element = element?.Element(Ns + step);
        return element;
    }

    public void Dispose()
    {
        if (Folder.Length == 0) return;

        // schtasks has no command for folders; the scripting interface of Task Scheduler does.
        string name = Folder.TrimStart('\\');
        RunScript($"try {{ $folder = $scheduler.GetFolder('\\{name}') }} catch {{ exit }}; " + // a test that registered nothing has no folder
                  $"$folder.GetTasks(1) | ForEach-Object {{ $folder.DeleteTask($_.Name, 0) }}; " +
                  $"$scheduler.GetFolder('\\').DeleteFolder('{name}', 0)");
    }

    /// <summary>Every task on the machine, hidden ones included, with whether it is enabled.</summary>
    public static IReadOnlyDictionary<string, bool> EnabledStates()
    {
        string listing = RunScript(
            "function Walk($folder) { $folder.GetTasks(1) | ForEach-Object { '{0}|{1}' -f $_.Path, $_.Enabled }; " +
            "$folder.GetFolders(0) | ForEach-Object { Walk $_ } }; Walk $scheduler.GetFolder('\\')");

        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.LastIndexOf('|');
            if (separator > 0) states[line[..separator]] = bool.Parse(line[(separator + 1)..]);
        }
        return states;
    }

    /// <summary>Runs PowerShell with <c>$scheduler</c> connected to Task Scheduler's scripting interface.</summary>
    private static string RunScript(string script)
    {
        using var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "$scheduler = New-Object -ComObject Schedule.Service; $scheduler.Connect(); " + script },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static string Run(string arguments) =>
        TryRun(arguments, out string output) ? output : throw new InvalidOperationException($"schtasks {arguments} failed: {output}");

    private static bool TryRun(string arguments, out string output)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        output = process.ExitCode == 0 ? standardOutput : standardError + standardOutput;
        return process.ExitCode == 0;
    }
}
