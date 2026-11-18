using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace FluentTaskScheduler.Snoozing;

/// <summary>Tells one run of Windows from the next, for snoozes that last "until restart".</summary>
public interface IWindowsSession
{
    /// <summary>
    /// A value that stays the same for as long as the user remains signed in, and is a different
    /// one after signing out, restarting or shutting down. Changes of the clock do not affect it.
    /// </summary>
    string Id { get; }
}

/// <summary>
/// Identifies the session by the moment the user signed in to it, as Windows recorded it. That
/// moment is fixed for the life of the session, unlike a boot time worked out from the clock and
/// the uptime, and it is a new one after a shutdown with Fast Startup, which does not restart
/// the kernel.
/// <para>
/// If Windows does not tell (the query fails, or there is no logon, as for a process started by
/// a service), every run of the app counts as a session of its own. An "until restart" snooze
/// then ends at the next start of the app: too early rather than never.
/// </para>
/// </summary>
public sealed class WindowsSession : IWindowsSession
{
    private const int CurrentSession = -1;   // WTS_CURRENT_SESSION
    private const int SessionInfoClass = 24; // WTS_INFO_CLASS.WTSSessionInfo

    // WTSINFOW starts with eight DWORDs and three strings of 32, 17 and 21 WCHARs (172 bytes),
    // is padded to 176, and continues with ConnectTime, DisconnectTime, LastInputTime, LogonTime.
    private const int LogonTimeOffset = 200;

    public WindowsSession() => Id = LogonFileTime() is { } logon ? $"logon-{logon:x}" : ThisRunOfTheApp();

    public string Id { get; }

    /// <summary>
    /// When the user signed in to the session this process runs in, as a Windows file time;
    /// null if Windows does not tell.
    /// </summary>
    internal static long? LogonFileTime()
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, CurrentSession, SessionInfoClass, out IntPtr info, out int size))
        {
            Log.Warning("Windows did not tell when this session began (error {Error})", Marshal.GetLastPInvokeError());
            return null;
        }

        try
        {
            long logon = size >= LogonTimeOffset + sizeof(long) ? Marshal.ReadInt64(info, LogonTimeOffset) : 0;
            return logon > 0 ? logon : null;
        }
        finally
        {
            WTSFreeMemory(info);
        }
    }

    private static string ThisRunOfTheApp()
    {
        using var process = Process.GetCurrentProcess();
        return $"app-{process.Id}-{process.StartTime.ToFileTimeUtc():x}";
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
