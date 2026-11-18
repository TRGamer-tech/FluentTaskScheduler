using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentTaskScheduler.Processes;
using FluentTaskScheduler.Tests.Support;
using Xunit.Abstractions;

namespace FluentTaskScheduler.Tests.Processes;

/// <summary>
/// Why <see cref="WindowsTools.InUtf8Console"/> starts programs the way it does, observed on the
/// machine that runs the test. Besides the two facts it asserts, the test prints what the kinds
/// of programs the app runs write into a pipe, byte for byte, with and without the switch to
/// UTF-8, and the code pages in effect: when their text arrives garbled, one run on Windows shows
/// which explanation holds. Windows only.
/// </summary>
public sealed class ConsoleCodePageTests : IDisposable
{
    private const string Text = "Zürich 東京 Ελλάδα";
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);
    private static readonly string Cmd = WindowsTools.InSystemFolder("cmd.exe");
    private static readonly string Chcp = WindowsTools.InSystemFolder("chcp.com");

    private readonly ITestOutputHelper _out;
    private readonly TempFolder _folder = new();

    public ConsoleCodePageTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _folder.Dispose();

    [SkippableFact]
    public async Task ChcpSwitchesTheConsoleOfAProgramStartedWithoutAWindow_ButNotForEchoOnTheSameCommandLine()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "runs Windows programs");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        uint hostConsole = GetConsoleOutputCP();
        _out.WriteLine($"This machine: OEM code page {GetOEMCP()}, ANSI code page {GetACP()}. The console of the test host: {(hostConsole == 0 ? "none" : hostConsole)}.");
        _out.WriteLine($"The text: {Text}, in UTF-8 {Hex(Encoding.UTF8.GetBytes(Text))}");

        await Record("chcp", Command($"\"{Chcp}\""));
        byte[] switched = await Record("chcp after chcp 65001", Command($"\"{Chcp}\" 65001 >nul & \"{Chcp}\""));
        byte[] echo = await Record("echo", Command($"echo {Text}"));
        byte[] echoAfterSwitch = await Record("echo after chcp 65001, by the same cmd", Command($"\"{Chcp}\" 65001 >nul & echo {Text}"));
        // The text comes as an argument, so that how cmd decodes the batch file itself plays no part.
        string batchFile = _folder.File("chcp-then-echo.cmd");
        File.WriteAllText(batchFile, $"@\"{Chcp}\" 65001 >nul\r\n@echo %~1\r\n");
        await Record("echo on the line after chcp 65001, by the same cmd running a batch file", Command($"\"{batchFile}\" \"{Text}\""));
        await Record("echo by a cmd started after chcp 65001", WindowsTools.InUtf8Console(Cmd, $"/d /c echo {Text}"));
        await Record("echo by cmd /u", new ProcessRequest(Cmd, $"/d /u /c echo {Text}"));

        File.WriteAllText(_folder.File(Text + ".txt"), "");
        await RecordWithAndWithoutSwitch("where", WindowsTools.InSystemFolder("where.exe"), $"/r \"{_folder.Path}\" *.txt");
        await RecordWithAndWithoutSwitch("PowerShell", WindowsTools.PowerShell, $"-NoProfile -NonInteractive -Command \"Write-Output '{Text}'\"");
        // DISM names its log file when it fails, as it does at once for an image that does not exist.
        await RecordWithAndWithoutSwitch("DISM", WindowsTools.InSystemFolder("Dism.exe"),
            $"/LogPath:\"{_folder.File(Text + ".log")}\" /Get-ImageInfo /ImageFile:\"{_folder.File("none.wim")}\"");
        // How sfc ends its lines. It writes its usage, or that it needs administrator rights.
        await Record("sfc", new ProcessRequest(WindowsTools.InSystemFolder("sfc.exe"), "/?"));

        Assert.Contains("65001", Encoding.ASCII.GetString(switched));
        Assert.Equal(echo, echoAfterSwitch);
    }

    private static ProcessRequest Command(string commandLine) => new(Cmd, $"/d /v:off /s /c \"{commandLine}\"");

    private async Task RecordWithAndWithoutSwitch(string label, string program, string arguments)
    {
        await Record(label, new ProcessRequest(program, arguments));
        await Record($"{label} after chcp 65001", WindowsTools.InUtf8Console(program, arguments));
    }

    /// <summary>Starts the program the way <see cref="ProcessRunner"/> does and prints what it writes, undecoded.</summary>
    /// <returns>What it wrote to its output.</returns>
    private async Task<byte[]> Record(string label, ProcessRequest request)
    {
        using Process process = Process.Start(new ProcessStartInfo(request.FileName, request.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.StandardInput.Close();
        using var output = new MemoryStream();
        using var errors = new MemoryStream();
        await Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(output),
            process.StandardError.BaseStream.CopyToAsync(errors),
            process.WaitForExitAsync()).WaitAsync(Patience);

        _out.WriteLine($"{label}: exit code {process.ExitCode}");
        Print("output", output.ToArray());
        Print("errors", errors.ToArray());
        return output.ToArray();
    }

    private void Print(string stream, byte[] bytes)
    {
        if (bytes.Length == 0) return;

        _out.WriteLine($"  {stream}: {Hex(bytes)}");
        foreach (int codePage in new[] { 65001, 1200, (int)GetOEMCP(), (int)GetACP() }.Distinct())
        {
            _out.WriteLine($"    read in code page {codePage}: {Visible(Encoding.GetEncoding(codePage).GetString(bytes))}");
        }
    }

    private static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2")));

    /// <summary>Control characters, line ends among them, as \xNN, so that each reading stays on one line.</summary>
    private static string Visible(string text) => string.Concat(text.Select(c => char.IsControl(c) ? $"\\x{(int)c:X2}" : c.ToString()));

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();
}
