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
    /// A request that switches a console to UTF-8 and then starts a program in it. Most console
    /// programs write into a pipe in the code page of their console, which otherwise depends on
    /// the language of Windows and cannot represent text in other scripts at all.
    /// </summary>
    /// <remarks>
    /// cmd settles the code page for what its own commands (echo, del, ...) write when it starts,
    /// so the cmd that switches the console goes on writing in the old one. For commands of cmd
    /// itself, make cmd.exe the program: that cmd starts in the switched console.
    /// </remarks>
    /// <param name="program">The full path of the program.</param>
    /// <param name="arguments">
    /// Its command line after the program name. cmd reads it on the way: its special characters
    /// (such as &amp; | &lt; &gt; ^ %) keep their meaning to cmd outside of double quotes, and % keeps it inside them too.
    /// </param>
    public static ProcessRequest InUtf8Console(string program, string arguments) =>
        // /d and /v:off: no AutoRun commands and no "!" expansion, whatever the registry says.
        // /s: cmd removes the outer quotes and takes the rest as written.
        new(InSystemFolder("cmd.exe"), $"/d /v:off /s /c \"\"{InSystemFolder("chcp.com")}\" 65001 >nul & \"{program}\" {arguments}\"")
        {
            OutputEncoding = Encoding.UTF8
        };
}
