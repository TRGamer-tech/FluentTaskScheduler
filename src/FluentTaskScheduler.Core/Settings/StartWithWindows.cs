using System.Security;
using Microsoft.Win32;

namespace FluentTaskScheduler.Settings;

/// <summary>
/// Whether Windows starts the app when the user signs in. Windows keeps that itself, and lets the
/// user change it in Task Manager and in Windows Settings, so it is asked each time instead of
/// being remembered in the settings file.
/// </summary>
public interface IStartWithWindows
{
    bool IsEnabled { get; }

    /// <returns>False when Windows did not accept the change.</returns>
    bool TrySetEnabled(bool enabled);
}

/// <summary>
/// The entry under the current user's Run key that starts this copy of the app at sign-in. An
/// entry that points to another copy does not count as enabled, and switching this copy off
/// leaves it alone.
/// </summary>
public sealed class RunKeyStartup : IStartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Where "Startup apps" in Task Manager and Windows Settings records that the user switched an entry off there.
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _valueName;
    private readonly string _command;

    public RunKeyStartup() : this("FluentTaskScheduler", Environment.ProcessPath ?? "")
    {
    }

    /// <param name="valueName">Name of the entry; one per program.</param>
    /// <param name="executablePath">The program Windows is to start.</param>
    public RunKeyStartup(string valueName, string executablePath)
    {
        _valueName = valueName;
        _command = $"\"{executablePath}\"";
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (!StartsThisCopy(run)) return false;

                // An entry switched off under "Startup apps" stays in the Run key; the refusal is an odd first byte here.
                using RegistryKey? approval = Registry.CurrentUser.OpenSubKey(ApprovalKey);
                return approval?.GetValue(_valueName) is not byte[] { Length: > 0 } state || state[0] % 2 == 0;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                Serilog.Log.Warning(ex, "Whether the app starts with Windows could not be read");
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                run.SetValue(_valueName, _command);
            }
            else
            {
                // Nothing there, or the entry of another copy, which is not this copy's to remove.
                if (!StartsThisCopy(run)) return true;
                run.DeleteValue(_valueName, throwOnMissingValue: false);
            }

            // Without a record under "Startup apps" an entry counts as allowed, which is right in both cases.
            using RegistryKey? approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, writable: true);
            approval?.DeleteValue(_valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Serilog.Log.Warning(ex, "Starting with Windows could not be switched {State}", enabled ? "on" : "off");
            return false;
        }
    }

    private bool StartsThisCopy(RegistryKey? run) =>
        _command.Equals(run?.GetValue(_valueName) as string, StringComparison.OrdinalIgnoreCase);
}
