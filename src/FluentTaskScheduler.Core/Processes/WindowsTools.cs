using System.Text;

namespace FluentTaskScheduler.Processes;

/// <summary>
/// The command line programs that come with Windows, by full path. A bare name would be looked
/// up in the folder of this app first, and whatever sits there would run with the app's rights.
/// </summary>
public static class WindowsTools
{
    /// <summary>Windows PowerShell 5.1, which every supported Windows has.</summary>
    public static string PowerShell { get; } = InSystemFolder(@"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>The full path of a program in the Windows system folder.</summary>
    public static string InSystemFolder(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    /// <summary>
    /// A request that runs a command line in a console switched to UTF-8. Programs write in the
    /// code page of their console, which otherwise depends on the language of Windows and cannot
    /// represent text in other scripts at all.
    /// </summary>
    /// <param name="commandLine">
    /// A command line for cmd.exe. Its special characters (such as &amp; | &lt; &gt; ^ %) keep their meaning to
    /// cmd outside of double quotes, and % keeps it inside them too.
    /// </param>
    public static ProcessRequest InUtf8Console(string commandLine) =>
        // /d and /v:off: no AutoRun commands and no "!" expansion, whatever the registry says.
        // /s: cmd removes the outer quotes and takes the rest as written.
        new(InSystemFolder("cmd.exe"), $"/d /v:off /s /c \"\"{InSystemFolder("chcp.com")}\" 65001 >nul & {commandLine}\"")
        {
            OutputEncoding = Encoding.UTF8
        };
}
