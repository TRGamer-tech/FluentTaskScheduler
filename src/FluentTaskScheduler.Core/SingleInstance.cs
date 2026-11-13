using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FluentTaskScheduler;

/// <summary>
/// Makes sure a user runs the app once per Windows session. A second start asks the first
/// instance to show its window and ends.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequested;
    private RegisteredWaitHandle? _listener;

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

    /// <summary>Must be called on the thread that created the instance, which is the one holding the mutex.</summary>
    public void Dispose()
    {
        _listener?.Unregister(null);
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showRequested.Dispose();
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
