using FluentTaskScheduler.Library;

namespace FluentTaskScheduler.Tests.Library;

public sealed class PowerShellCommandTests
{
    [Theory]
    [InlineData("Write-Output 'plain'")]
    [InlineData("Write-Output \"two words\"")]
    [InlineData(@"Get-ChildItem 'C:\Logs\' -Filter *.log")]
    [InlineData(@"Write-Output ""C:\Logs\""")]
    [InlineData(@"Write-Output 'ends with a backslash\")]
    [InlineData(@"\\server\share ""a\\""b"" \\")]
    [InlineData("")]
    public void AScript_ArrivesAsOneArgument_ExactlyAsWritten(string script)
    {
        string arguments = PowerShellCommand.ForScript(script);

        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script],
            WindowsCommandLine.Split(arguments));
    }

    [Fact]
    public void AScriptFile_IsNamedInOneArgument_EvenWithSpacesInItsPath()
    {
        string arguments = PowerShellCommand.ForFile(@"C:\Users\Jo Doe\Scripts\tidy up.ps1");

        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", @"C:\Users\Jo Doe\Scripts\tidy up.ps1"],
            WindowsCommandLine.Split(arguments));
    }

    [SkippableFact]
    public async Task WindowsPowerShell_RunsAScriptWithQuotesAndBackslashes_AsWritten()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), WindowsPowerShell.SkipReason);
        const string script = @"Write-Output ""one two"" 'three\' ""C:\Temp\""";

        var (exitCode, output) = await WindowsPowerShell.RunAsync(PowerShellCommand.Executable, PowerShellCommand.ForScript(script));

        Assert.Equal(0, exitCode);
        Assert.Equal(["one two", @"three\", @"C:\Temp\"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
