using System.Text;

namespace FluentTaskScheduler.Library;

/// <summary>What a task (or this app) passes to Windows PowerShell to make it run something unattended.</summary>
public static class PowerShellCommand
{
    /// <summary>The program of a task action. Task Scheduler finds it by name on every machine.</summary>
    public const string Executable = "powershell.exe";

    // Bypass: the default execution policy refuses script files, and with them the modules written as scripts.
    private const string Unattended = "-NoProfile -NonInteractive -ExecutionPolicy Bypass";

    /// <summary>Arguments that run a script file.</summary>
    public static string ForFile(string path) => $"{Unattended} -File \"{path}\"";

    /// <summary>Arguments that run <paramref name="script"/> itself, without a console window staying open.</summary>
    public static string ForScript(string script) => $"{Unattended} -WindowStyle Hidden -Command {Quote(script)}";

    /// <summary>
    /// Writes <paramref name="argument"/> so that a Windows program reads it back as one argument,
    /// whatever quotes and backslashes it contains.
    /// </summary>
    internal static string Quote(string argument)
    {
        // A quote is written as \", which in turn makes backslashes in front of a quote special:
        // those (and only those) have to be doubled.
        var quoted = new StringBuilder(argument.Length + 2).Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
