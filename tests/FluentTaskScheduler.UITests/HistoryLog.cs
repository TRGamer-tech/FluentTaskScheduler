using System.Diagnostics;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Task Scheduler's history log, switched on for one test and put back the way it was. Windows
/// ships it switched off, and then no run is recorded anywhere. Needs administrator rights.
/// </summary>
public sealed class HistoryLog : IDisposable
{
    private const string Name = "Microsoft-Windows-TaskScheduler/Operational";

    private readonly bool _wasOn;

    public HistoryLog()
    {
        _wasOn = Wevtutil($"gl {Name}").Contains("enabled: true", StringComparison.OrdinalIgnoreCase);
        if (!_wasOn) Wevtutil($"sl {Name} /e:true");
    }

    public void Dispose()
    {
        if (!_wasOn) Wevtutil($"sl {Name} /e:false");
    }

    private static string Wevtutil(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("wevtutil.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"wevtutil {arguments} failed: {error}{output}");
    }
}
