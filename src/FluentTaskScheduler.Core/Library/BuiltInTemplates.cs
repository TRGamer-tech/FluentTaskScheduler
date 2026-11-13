using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Library;

/// <summary>The sections the built-in templates are shown in. Member names end the resource names of the headings.</summary>
public enum TemplateGroup
{
    Maintenance,
    Developer,
    Power
}

/// <summary>A task template that ships with the app.</summary>
/// <param name="Id">Names the template in resources (Templates.&lt;Id&gt;.Name, .Desc, .Note) and in UI automation.</param>
/// <param name="Glyph">Its icon in the Segoe Fluent Icons font.</param>
/// <param name="Note">What to check or change before saving the task; empty when there is nothing.</param>
public sealed record BuiltInTemplate(string Id, TemplateGroup Group, string Glyph, string Note, TaskTemplate Template);

/// <summary>
/// The task templates that ship with the app.
///
/// PowerShell is written here as it is typed, on one line and with single quotes where the
/// language leaves the choice; <see cref="PowerShellCommand.ForScript"/> does the quoting for the
/// command line. Two things must not appear in it: "$(", which Task Scheduler takes for one of
/// its own variables inside task arguments, and anything that only works after Windows
/// PowerShell 5.1, the version every task runs with.
///
/// Task Scheduler and the task history judge a run by its exit code, and PowerShell reports 1
/// whenever its last command had an error, even an ignored one. A script that ignores errors on
/// purpose therefore ends with an explicit "exit".
/// </summary>
public static class BuiltInTemplates
{
    /// <summary>All templates, with their texts in the language the app shows now.</summary>
    public static IReadOnlyList<BuiltInTemplate> Create() =>
    [
        Define("TempCleanup", TemplateGroup.Maintenance, "\uE74D", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            // Get-ChildItem -Recurse would follow links to folders outside the temp folder, so the tree is walked by hand.
            Arguments = PowerShellCommand.ForScript(
                "$cutoff = (Get-Date).AddDays(-7); " +
                "$folders = New-Object System.Collections.Stack; " +
                "$folders.Push($env:TEMP); " +
                "while ($folders.Count -gt 0) { " +
                "foreach ($item in Get-ChildItem -LiteralPath $folders.Pop() -Force -ErrorAction SilentlyContinue) { " +
                "if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }; " +
                "if ($item.PSIsContainer) { $folders.Push($item.FullName) } " +
                "elseif ($item.LastWriteTime -lt $cutoff) { Remove-Item -LiteralPath $item.FullName -Force -ErrorAction SilentlyContinue } } }; " +
                "exit 0"),
            Trigger = Daily,
            At = new(3, 0),
            RunIfMissed = true,
            Category = Category("Maintenance"),
            Tags = [Tag("Cleanup"), Tag("Disk")]
        }),
        Define("UpdateHealth", TemplateGroup.Maintenance, "\uE895", hasNote: false, new()
        {
            Command = PowerShellCommand.Executable,
            Arguments = PowerShellCommand.ForScript(
                "$log = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WindowsUpdateHealth.log'; " +
                "$pending = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().Search('IsInstalled=0 and IsHidden=0').Updates.Count; " +
                "$last = Get-HotFix | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn | Select-Object -Last 1; " +
                "'{0} pending={1} last-install={2:yyyy-MM-dd}' -f (Get-Date -Format s), $pending, $last.InstalledOn | Out-File -FilePath $log -Append -Encoding utf8"),
            Trigger = Weekly(DaysOfTheWeek.Monday),
            At = new(9, 0),
            Category = Category("Maintenance"),
            Tags = [Tag("Updates"), Tag("Health")]
        }),
        Define("Defrag", TemplateGroup.Maintenance, "\uEDA2", hasNote: true, new()
        {
            Command = "defrag.exe",
            Arguments = "C: /O /H",
            Trigger = Weekly(DaysOfTheWeek.Sunday),
            At = new(2, 0),
            RunElevated = true,
            OnlyWhenIdle = true,
            OnlyOnAcPower = true,
            Category = Category("Maintenance"),
            Tags = [Tag("Disk"), Tag("Performance")]
        }),
        Define("RestorePoint", TemplateGroup.Maintenance, "\uE81C", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            Arguments = PowerShellCommand.ForScript("Checkpoint-Computer -Description 'Weekly restore point' -RestorePointType MODIFY_SETTINGS"),
            Trigger = Weekly(DaysOfTheWeek.Wednesday),
            At = new(12, 0),
            RunElevated = true,
            OnlyOnAcPower = true,
            Category = Category("Maintenance"),
            Tags = [Tag("Backup"), Tag("Recovery")]
        }),
        Define("SystemScan", TemplateGroup.Maintenance, "\uE90F", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            // Only the exit codes are logged: sfc writes UTF-16 and DISM the console code page, which do not mix in one file.
            Arguments = PowerShellCommand.ForScript(
                "$log = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'SystemScan.log'; " +
                "DISM.exe /Online /Cleanup-Image /RestoreHealth | Out-Null; " +
                "$dism = $LASTEXITCODE; " +
                "sfc.exe /scannow | Out-Null; " +
                "'{0} DISM={1} SFC={2}' -f (Get-Date -Format s), $dism, $LASTEXITCODE | Out-File -FilePath $log -Append -Encoding utf8"),
            Trigger = Weekly(DaysOfTheWeek.Saturday),
            At = new(4, 0),
            RunElevated = true,
            OnlyWhenIdle = true,
            OnlyOnAcPower = true,
            Category = Category("Maintenance"),
            Tags = [Tag("Health"), Tag("Repair")]
        }),
        Define("DnsFlush", TemplateGroup.Maintenance, "\uE968", hasNote: false, new()
        {
            Command = "ipconfig.exe",
            Arguments = "/flushdns",
            Trigger = new() { Type = TriggerType.AtLogon },
            Category = Category("Maintenance"),
            Tags = [Tag("Network")]
        }),
        Define("RecycleBin", TemplateGroup.Maintenance, "\uE74D", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            Arguments = PowerShellCommand.ForScript("Clear-RecycleBin -Force -ErrorAction SilentlyContinue; exit 0"),
            Trigger = Weekly(DaysOfTheWeek.Saturday),
            At = new(20, 0),
            Category = Category("Maintenance"),
            Tags = [Tag("Cleanup")]
        }),

        Define("GitFetch", TemplateGroup.Developer, "\uE895", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            // GIT_TERMINAL_PROMPT: a repository that wants a password fails instead of waiting for one nobody types.
            Arguments = PowerShellCommand.ForScript(
                @"$root = 'C:\Repos'; " +
                "$env:GIT_TERMINAL_PROMPT = '0'; " +
                "$failed = -not (Test-Path -LiteralPath $root); " +
                "Get-ChildItem -LiteralPath $root -Directory -Filter .git -Recurse -Depth 2 -Force -ErrorAction SilentlyContinue | " +
                "ForEach-Object { git -C $_.Parent.FullName fetch --all --prune; if ($LASTEXITCODE -ne 0) { $failed = $true } }; " +
                "exit ([int]$failed)"),
            Trigger = Daily,
            At = new(8, 0),
            RunIfMissed = true,
            Category = Category("Work"),
            Tags = ["git", Tag("Sync")]
        }),
        Define("DockerPrune", TemplateGroup.Developer, "\uE7B8", hasNote: true, new()
        {
            Command = "docker.exe",
            Arguments = "system prune -f",
            Trigger = Weekly(DaysOfTheWeek.Friday),
            At = new(19, 0),
            Category = Category("Work"),
            Tags = ["docker", Tag("Cleanup")]
        }),
        Define("LogRotation", TemplateGroup.Developer, "\uE7C3", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            // Every error ends the script, so the logs are only deleted after they made it into the archive.
            // The archive is named to the second: a second run on the same day never replaces an earlier one.
            Arguments = PowerShellCommand.ForScript(
                "$ErrorActionPreference = 'Stop'; " +
                @"$source = 'C:\Logs'; " +
                "$archive = Join-Path $source 'archive'; " +
                "$logs = Get-ChildItem -LiteralPath $source -Filter *.log -File | Where-Object { $_.LastWriteTime -lt (Get-Date).Date }; " +
                "if ($logs) { " +
                "New-Item -ItemType Directory -Path $archive -Force | Out-Null; " +
                "Compress-Archive -LiteralPath $logs.FullName -DestinationPath (Join-Path $archive ('logs-{0:yyyyMMdd-HHmmss}.zip' -f (Get-Date))); " +
                "$logs | Remove-Item -Force }; " +
                "if (Test-Path -LiteralPath $archive) { " +
                "Get-ChildItem -LiteralPath $archive -Filter *.zip -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force }"),
            Trigger = Daily,
            At = new(1, 0),
            RunIfMissed = true,
            Category = Category("Work"),
            Tags = [Tag("Logs"), Tag("Cleanup")]
        }),
        Define("NodePrune", TemplateGroup.Developer, "\uE7B8", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            Arguments = PowerShellCommand.ForScript(
                "npm cache verify; " +
                "$failed = $LASTEXITCODE -ne 0; " +
                "if (Get-Command pnpm -ErrorAction SilentlyContinue) { pnpm store prune; if ($LASTEXITCODE -ne 0) { $failed = $true } }; " +
                "exit ([int]$failed)"),
            Trigger = Weekly(DaysOfTheWeek.Friday),
            At = new(18, 0),
            Category = Category("Work"),
            Tags = ["node", Tag("Cleanup")]
        }),
        Define("ProjectBackup", TemplateGroup.Developer, "\uE8F7", hasNote: true, new()
        {
            Command = "cmd.exe",
            // robocopy says what it did in its exit code, and everything below 8 is a success that Task
            // Scheduler and the task history would show as a failure. 8 and up mean files could not be
            // copied, 16 and up that nothing was copied at all.
            Arguments = @"/c robocopy ""C:\Projects"" ""D:\Backups\Projects"" /MIR /XD node_modules bin obj /R:1 /W:1 /NP & if errorlevel 16 (exit 16) else if errorlevel 8 (exit 8) else (exit 0)",
            Trigger = Daily,
            At = new(2, 30),
            RunIfMissed = true,
            Category = Category("Work"),
            Tags = [Tag("Backup")]
        }),
        Define("DevServerRestart", TemplateGroup.Developer, "\uE72C", hasNote: true, new()
        {
            Command = PowerShellCommand.Executable,
            Arguments = PowerShellCommand.ForScript("Restart-Service -Name 'YourServiceName' -Force"),
            Trigger = Daily,
            At = new(5, 0),
            RunElevated = true,
            Category = Category("Work"),
            Tags = [Tag("Service")]
        }),

        Define("IdleShutdown", TemplateGroup.Power, "\uE7E8", hasNote: true, new()
        {
            Command = "shutdown.exe",
            Arguments = $"/s /t 60 /c {ShutdownComment("IdleShutdown")}",
            Trigger = Daily,
            At = new(23, 30),
            OnlyWhenIdle = true,
            Category = Category("System"),
            Tags = [Tag("Power")]
        }),
        Define("WeeklyReboot", TemplateGroup.Power, "\uE777", hasNote: true, new()
        {
            Command = "shutdown.exe",
            Arguments = $"/r /t 120 /c {ShutdownComment("WeeklyReboot")}",
            Trigger = Weekly(DaysOfTheWeek.Sunday),
            At = new(4, 0),
            OnlyWhenIdle = true,
            Category = Category("System"),
            Tags = [Tag("Power"), Tag("Updates")]
        }),
        Define("BatterySaver", TemplateGroup.Power, "\uE83E", hasNote: true, new()
        {
            Command = "powercfg.exe",
            Arguments = "/setactive a1841308-3541-4fab-bc81-f71556f20b4a",
            Trigger = new() { Type = TriggerType.AtLogon },
            Category = Category("System"),
            Tags = [Tag("Power"), Tag("Battery")]
        }),
        Define("DisplayOff", TemplateGroup.Power, "\uE7F4", hasNote: false, new()
        {
            Command = PowerShellCommand.Executable,
            // Posted, not sent: sending to all windows waits for each of them, and one that hangs would hold the task up.
            Arguments = PowerShellCommand.ForScript(
                "(Add-Type -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);' " +
                "-Name Display -Namespace FluentTaskScheduler -PassThru)::PostMessage([IntPtr]0xffff, 0x0112, [IntPtr]0xF170, [IntPtr]2) | Out-Null"),
            Trigger = new() { Type = TriggerType.SessionStateChange, SessionChange = TaskSessionStateChangeType.SessionLock },
            Category = Category("System"),
            Tags = [Tag("Power"), Tag("Display")]
        })
    ];

    private static TriggerDraft Daily => new() { Type = TriggerType.Daily };

    private static TriggerDraft Weekly(DaysOfTheWeek day) => new() { Type = TriggerType.Weekly, DaysOfWeek = day };

    private static BuiltInTemplate Define(string id, TemplateGroup group, string glyph, bool hasNote, TaskTemplate outline) =>
        new(id, group, glyph, hasNote ? Loc.Get($"Templates.{id}.Note") : "", outline with
        {
            Name = Loc.Get($"Templates.{id}.Name"),
            Description = Loc.Get($"Templates.{id}.Desc")
        });

    // A task keeps its category and tags as words, and the task list shows them, so they are
    // written in the language the app shows now. Names of programs are tags of their own.
    private static string Category(string id) => Loc.Get($"Templates.Category.{id}");

    private static string Tag(string id) => Loc.Get($"Templates.Tag.{id}");

    /// <summary>The text Windows shows while it counts down, quoted for the command line.</summary>
    private static string ShutdownComment(string id) =>
        '"' + Loc.Get($"Templates.{id}.Comment").Replace('"', '\'') + '"';
}
