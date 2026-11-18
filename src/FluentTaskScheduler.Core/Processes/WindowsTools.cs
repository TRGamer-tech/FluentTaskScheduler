using System.Runtime.InteropServices;
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
    /// It starts a program and not a command line: the cmd that runs chcp does not apply the new
    /// code page to the rest of the command line it is running, so what its own commands on that
    /// line (echo, del, ...) write stays in the old code page.
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

    /// <summary>
    /// A request for a program that writes into a pipe in the OEM code page of Windows whatever
    /// its console is set to, as DISM does. What that code page cannot represent, such a program
    /// writes as "?"; everything else arrives intact.
    /// </summary>
    /// <param name="program">The full path of the program.</param>
    public static ProcessRequest InOemCodePage(string program, string arguments) =>
        new(program, arguments) { OutputEncoding = OemCodePage() };

    private static Encoding OemCodePage()
    {
        if (!OperatingSystem.IsWindows()) return Encoding.UTF8;

        // .NET knows the code pages of Windows other than UTF-8 and UTF-16 only once they are registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP());
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetOEMCP();
}
