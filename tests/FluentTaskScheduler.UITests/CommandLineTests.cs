using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace FluentTaskScheduler.UITests;

/// <summary>The published exe used the way a script uses it: no window, text as the answer, and an exit code.</summary>
public class CommandLineTests
{
    [Fact]
    public void List_PrintsTheTasksOfThisMachineAsJson()
    {
        using var tasks = new WindowsTasks();
        tasks.CreateDaily("Listed");

        var (exitCode, output) = Run("--list");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output);
        var listed = json.RootElement.EnumerateArray().Single(task => task.GetProperty("Path").GetString() == tasks.PathOf("Listed"));
        Assert.Equal("Listed", listed.GetProperty("Name").GetString());
        Assert.Equal("Ready", listed.GetProperty("State").GetString());
    }

    [Fact]
    public void DisableAndEnable_SwitchTheTask()
    {
        using var tasks = new WindowsTasks();
        tasks.CreateDaily("Switched");

        Assert.Equal(0, Run("--disable", tasks.PathOf("Switched")).ExitCode);
        Assert.Equal("false", tasks.Value("Switched", "Settings", "Enabled"));

        // Task Scheduler leaves the element out of an enabled task, enabled being the default.
        Assert.Equal(0, Run("--enable", tasks.PathOf("Switched")).ExitCode);
        Assert.NotEqual("false", tasks.Value("Switched", "Settings", "Enabled"));
    }

    [Fact]
    public void Run_StartsTheTask()
    {
        using var tasks = new WindowsTasks();
        string marker = Path.Combine(Path.GetTempPath(), "FtsUiTests_" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        tasks.CreateDaily("Started", $"cmd.exe /c echo ran>{marker}");
        try
        {
            var (exitCode, output) = Run("--run", tasks.PathOf("Started"));

            Assert.Equal(0, exitCode);
            Assert.Contains("Started: " + tasks.PathOf("Started"), output);
            Assert.True(Retry.WhileFalse(() => File.Exists(marker), TimeSpan.FromSeconds(30)).Success, "The task never ran.");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void ATaskThatDoesNotExist_IsAFailure()
    {
        var (exitCode, output) = Run("--run", @"\FtsUiTests_NoSuchTask");

        Assert.Equal(1, exitCode);
        Assert.Contains(@"Task not found: \FtsUiTests_NoSuchTask", output);
    }

    [Fact]
    public void ASwitchThatIsNotKnown_GetsTheUsageInsteadOfTheWindow()
    {
        var (exitCode, output) = Run("--frobnicate");

        Assert.Equal(2, exitCode);
        Assert.Contains("usage:", output);
    }

    /// <summary>
    /// The app is a windowed program, and those have no console. Started from one, it has to
    /// borrow it, or its answer goes nowhere.
    /// </summary>
    [Fact]
    public void StartedFromAConsole_TheAnswerAppearsThere()
    {
        string title = "FtsCli_" + Guid.NewGuid().ToString("N")[..8];

        // conhost.exe gives the command a classic console window, whatever the default terminal is.
        // It has to be started through the shell: handed this process's redirected output, it
        // would open no window and write there instead.
        using var console = Process.Start(new ProcessStartInfo(
            "conhost.exe", $"cmd.exe /c \"title {title} & \"{AppSession.ExePath}\" --help & pause\"") { UseShellExecute = true })!;
        try
        {
            using var automation = new UIA3Automation();
            string text = "";
            bool shown = Retry.WhileFalse(() =>
            {
                text = ConsoleText(automation, title);
                return text.Contains("--list") && text.Contains("Exit code:");
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500)).Success;

            Assert.True(shown, "The console showed: " + text);
        }
        finally
        {
            console.Kill(entireProcessTree: true);
        }
    }

    private static string ConsoleText(UIA3Automation automation, string title)
    {
        AutomationElement? window = automation.GetDesktop().FindAllChildren().FirstOrDefault(candidate => Name(candidate).Contains(title));
        AutomationElement? textArea = window?.FindAllChildren().FirstOrDefault(child => child.ControlType == ControlType.Document);
        return textArea?.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1) ?? "";
    }

    private static string Name(AutomationElement element)
    {
        try { return element.Name ?? ""; }
        catch { return ""; } // a window that is just going away
    }

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(AppSession.ExePath) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"'{string.Join(' ', arguments)}' did not end. A command must answer and exit, not open the window.");
        }
        return (process.ExitCode, output.Result);
    }
}
