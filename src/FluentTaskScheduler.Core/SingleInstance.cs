using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FluentTaskScheduler;

/// <summary>
/// Makes sure a user runs the app once per Windows session. A second start asks the first
/// instance to show its window and ends. The uninstaller's start asks it to end instead.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequested;
    private readonly EventWaitHandle _exitRequested;
    private RegisteredWaitHandle? _listener;
    private RegisteredWaitHandle? _exitListener;

    /// <param name="name">Identifies the app; instances with the same name exclude each other.</param>
    /// <param name="waitForPrevious">How long to wait for an instance that is about to exit, as when the app restarts itself.</param>
    public SingleInstance(string name, TimeSpan waitForPrevious)
    {
        // The app often runs as administrator, while a later start from the Start menu does not.
        // What an elevated process creates is by default out of reach for the same user's other
        // processes, so the user is granted access by name.
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        name = $"{name}_{user.Value}";

        var mutexSecurity = new MutexSecurity();
        mutexSecurity.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
        _mutex = MutexAcl.Create(initiallyOwned: false, name + "_Instance", out _, mutexSecurity);
        try
        {
            IsFirst = _mutex.WaitOne(waitForPrevious);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance died without releasing it; the mutex is ours now.
            IsFirst = true;
        }

        var eventSecurity = new EventWaitHandleSecurity();
        eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        _showRequested = EventWaitHandleAcl.Create(initialState: false, EventResetMode.AutoReset, name + "_Show", out _, eventSecurity);
        _exitRequested = EventWaitHandleAcl.Create(initialState: false, EventResetMode.AutoReset, name + "_Exit", out _, eventSecurity);
    }

    public bool IsFirst { get; }

    /// <summary>From a later instance: asks the first one to show itself.</summary>
    public void AskFirstToShow()
    {
        // Windows lets only the program the user is working with bring a window to the front.
        // Right after being started that is this instance, which passes the permission on.
        AllowSetForegroundWindow(AnyProcess);
        _showRequested.Set();
    }

    /// <summary>In the first instance: runs <paramref name="show"/> on a pool thread whenever a later instance asks.</summary>
    public void OnShowRequested(Action show) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(_showRequested, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>
    /// From a later instance, on the thread that created it: asks the first one to end and waits
    /// until it has, for as long as <paramref name="patience"/> lasts.
    /// </summary>
    /// <returns>False if the first instance is still running then.</returns>
    public bool AskFirstToExit(TimeSpan patience)
    {
        _exitRequested.Set();
        try
        {
            // The first instance holds the mutex until it ends.
            if (!_mutex.WaitOne(patience)) return false;
        }
        catch (AbandonedMutexException)
        {
            // It ended without releasing the mutex, which is ended all the same.
        }
        _mutex.ReleaseMutex();
        return true;
    }

    /// <summary>In the first instance: runs <paramref name="exit"/> on a pool thread when a later instance asks.</summary>
    public void OnExitRequested(Action exit) =>
        _exitListener = ThreadPool.RegisterWaitForSingleObject(_exitRequested, (_, _) => exit(), null, Timeout.Infinite, executeOnlyOnce: true);

    /// <summary>Must be called on the thread that created the instance, which is the one holding the mutex.</summary>
    public void Dispose()
    {
        _listener?.Unregister(null);
        _exitListener?.Unregister(null);
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showRequested.Dispose();
        _exitRequested.Dispose();
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
