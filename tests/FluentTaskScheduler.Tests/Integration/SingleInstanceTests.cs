using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FluentTaskScheduler.Tests.Integration;

public class SingleInstanceTests
{
    private readonly string _name = "FtsTests_" + Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public void ASecondInstance_IsNotFirst_AndCanAskTheFirstToShow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Needs Windows.");
        using var first = new SingleInstance(_name, TimeSpan.Zero);
        using var asked = new ManualResetEventSlim();
        first.OnShowRequested(asked.Set);

        bool secondIsFirst = OnAnotherThread(() =>
        {
            using var second = new SingleInstance(_name, TimeSpan.Zero);
            if (!second.IsFirst) second.AskFirstToShow();
            return second.IsFirst;
        });

        Assert.True(first.IsFirst);
        Assert.False(secondIsFirst);
        Assert.True(asked.Wait(TimeSpan.FromSeconds(10)), "The first instance was never asked to show itself.");
    }

    [SkippableFact]
    public void ALaterInstance_CanAskTheFirstToEnd_AndLearnsThatItHas()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Needs Windows.");
        using var asked = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();

        // The first instance, on a thread of its own as in a process of its own: it ends when asked.
        var first = new Thread(() =>
        {
            using var instance = new SingleInstance(_name, TimeSpan.Zero);
            instance.OnExitRequested(asked.Set);
            ready.Set();
            asked.Wait(TimeSpan.FromSeconds(20));
        });
        first.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        using var later = new SingleInstance(_name, TimeSpan.Zero);
        bool ended = later.AskFirstToExit(TimeSpan.FromSeconds(10));
        first.Join();

        Assert.False(later.IsFirst);
        Assert.True(asked.IsSet, "The first instance was never asked to end.");
        Assert.True(ended, "The later instance did not learn that the first one ended.");
    }

    [SkippableFact]
    public void AFirstInstanceThatDoesNotEnd_IsNotWaitedForLongerThanAsked()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Needs Windows.");
        using var first = new SingleInstance(_name, TimeSpan.Zero);

        bool ended = OnAnotherThread(() =>
        {
            using var later = new SingleInstance(_name, TimeSpan.Zero);
            return later.AskFirstToExit(TimeSpan.FromMilliseconds(200));
        });

        Assert.False(ended);
    }

    [SkippableFact]
    public void AfterTheFirstInstanceIsGone_TheNextOneIsFirst()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Needs Windows.");
        new SingleInstance(_name, TimeSpan.Zero).Dispose();

        using var next = new SingleInstance(_name, TimeSpan.Zero);

        Assert.True(next.IsFirst);
    }

    [SkippableFact]
    public void AnInstanceThatIsAboutToExit_IsWaitedFor()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Needs Windows.");
        using var releaseFirst = new ManualResetEventSlim();
        using var firstRuns = new ManualResetEventSlim();
        var firstThread = new Thread(() =>
        {
            using var first = new SingleInstance(_name, TimeSpan.Zero);
            firstRuns.Set();
            releaseFirst.Wait();
        });
        firstThread.Start();
        firstRuns.Wait();

        Task<bool> restarted = Task.Run(() => OnAnotherThread(() =>
        {
            using var second = new SingleInstance(_name, TimeSpan.FromSeconds(30));
            return second.IsFirst;
        }));
        Thread.Sleep(300);
        releaseFirst.Set();

        Assert.True(restarted.Result, "The restarted instance did not take over.");
        firstThread.Join();
    }

    /// <summary>
    /// The app as administrator, then started again the ordinary way: the second start has the
    /// same user but not the administrator's rights, and still has to reach the first one.
    /// </summary>
    [SkippableFact]
    public void AnInstanceWithoutAdministratorRights_ReachesAnElevatedFirstOne()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "Needs an elevated process to start from.");
        using var first = new SingleInstance(_name, TimeSpan.Zero);
        using var asked = new ManualResetEventSlim();
        first.OnShowRequested(asked.Set);

        using SafeAccessTokenHandle ordinaryUser = TokenWithoutAdministratorRights();
        bool secondIsFirst = OnAnotherThread(() => WindowsIdentity.RunImpersonated(ordinaryUser, () =>
        {
            using var second = new SingleInstance(_name, TimeSpan.Zero);
            if (!second.IsFirst) second.AskFirstToShow();
            return second.IsFirst;
        }));

        Assert.False(secondIsFirst);
        Assert.True(asked.Wait(TimeSpan.FromSeconds(10)), "The first instance was never asked to show itself.");
    }

    // The mutex belongs to a thread, and a thread may take its own mutex again: a second instance
    // on the same thread would wrongly be first.
    private static T OnAnotherThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start();
        thread.Join();
        return failure is null ? result : throw new InvalidOperationException("The other instance failed: " + failure.Message, failure);
    }

    /// <summary>This process's token the way User Account Control hands it to programs that are not elevated.</summary>
    private static SafeAccessTokenHandle TokenWithoutAdministratorRights()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!CreateRestrictedToken(identity.AccessToken, LuaToken, 0, 0, 0, 0, 0, 0, out SafeAccessTokenHandle token))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return token;
    }

    private const uint LuaToken = 0x4;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existingToken, uint flags,
        uint disableSidCount, nint sidsToDisable,
        uint deletePrivilegeCount, nint privilegesToDelete,
        uint restrictedSidCount, nint sidsToRestrict,
        out SafeAccessTokenHandle newToken);
}
