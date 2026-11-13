using System.ComponentModel;
using System.Diagnostics;

namespace FluentTaskScheduler.ViewModels;

/// <summary>Opens things outside the app: a file in its program, a folder in Explorer, an address in the browser.</summary>
public interface ILauncher
{
    /// <returns>False when Windows could not open <paramref name="target"/>.</returns>
    bool TryOpen(string target);
}

/// <summary>Hands the target to the Windows shell, as a double-click would.</summary>
public sealed class ShellLauncher : ILauncher
{
    public bool TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception ex)
        {
            Serilog.Log.Warning(ex, "{Target} could not be opened", target);
            return false;
        }
    }
}
