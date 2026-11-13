using System.Text;
using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.ViewModels;

/// <summary>A maintenance command the Quick Actions page runs when asked.</summary>
/// <param name="Id">
/// Names the action in resources (QuickActions.&lt;Id&gt;.Title and .Desc, plus .Confirm for an
/// action that asks first) and in UI automation.
/// </param>
/// <param name="Glyph">Its icon in the Segoe Fluent Icons font.</param>
public sealed record QuickAction(string Id, string Glyph, ProcessRequest Request)
{
    /// <summary>Windows refuses the command without administrator rights.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>The action deletes or interrupts something, so the user is asked before it runs.</summary>
    public bool ConfirmFirst { get; init; }

    /// <summary>The last lines the program writes are what it found; they stay on screen after a successful run.</summary>
    public bool ReportsFindings { get; init; }

    // Files in use cannot be deleted and are no reason to call the run a failure.
    internal const string ClearTempScript =
        "Remove-Item -Path (Join-Path $env:TEMP '*') -Recurse -Force -ErrorAction SilentlyContinue; exit 0";

    // Windows starts Explorer again by itself when it ends this way. Where that is switched off, the last part does it.
    internal const string RestartExplorerScript =
        "Stop-Process -Name explorer -Force; Start-Sleep -Seconds 3; " +
        "if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process (Join-Path $env:SystemRoot 'explorer.exe') }";

    public static IReadOnlyList<QuickAction> All { get; } =
    [
        new("FlushDNS", "", Tool("ipconfig.exe", "/flushdns")) { ReportsFindings = true },
        new("ClearTemp", "", PowerShell(ClearTempScript)) { ConfirmFirst = true },
        new("RestartExplorer", "", PowerShell(RestartExplorerScript)) { ConfirmFirst = true },
        new("IPReleaseRenew", "", Tool("ipconfig.exe", "/renew")) { RequiresElevation = true },
        // sfc writes UTF-16 whatever its console is set to.
        new("SfcScannow", "", new ProcessRequest(WindowsTools.InSystemFolder("sfc.exe"), "/scannow") { OutputEncoding = Encoding.Unicode })
        {
            RequiresElevation = true,
            ReportsFindings = true
        },
        new("DismHealth", "", Tool("Dism.exe", "/Online /Cleanup-Image /CheckHealth")) { RequiresElevation = true, ReportsFindings = true }
    ];

    internal static ProcessRequest Tool(string fileName, string arguments) =>
        WindowsTools.InUtf8Console(WindowsTools.InSystemFolder(fileName), arguments);

    /// <param name="script">Must not contain double quotes: it travels inside one pair of them.</param>
    internal static ProcessRequest PowerShell(string script) =>
        WindowsTools.InUtf8Console(WindowsTools.PowerShell, $"-NoProfile -NonInteractive -Command \"{script}\"");
}
