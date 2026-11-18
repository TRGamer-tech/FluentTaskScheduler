using System.Diagnostics;
using System.Text;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>The real Windows PowerShell and command prompt, for tests that only run on Windows.</summary>
internal static class WindowsPowerShell
{
    public const string SkipReason = "needs Windows";

    /// <summary>The syntax errors Windows PowerShell 5.1 finds in a script; none if it would start running.</summary>
    public static async Task<IReadOnlyList<string>> SyntaxErrorsInAsync(string script)
    {
        string file = Path.Combine(Path.GetTempPath(), $"FtsTests_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(file, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            // The path travels in a variable, so no character in it can be mistaken for PowerShell.
            var (_, output) = await RunAsync("powershell.exe",
                "-NoProfile -NonInteractive -Command \"$errors = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($env:FTS_TESTS_SCRIPT, [ref]$null, [ref]$errors); $errors | ForEach-Object { $_.ToString() }\"",
                ("FTS_TESTS_SCRIPT", file));
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Runs a program to its end and returns its exit code with everything it wrote.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var (name, value) in environment) start.Environment[name] = value;

        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await errors);
    }
}
