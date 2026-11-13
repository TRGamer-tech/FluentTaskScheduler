using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>The session is identified by Windows, so these need Windows.</summary>
public class WindowsSessionTests
{
    private const string NeedsWindows = "the session is identified by Windows";

    [SkippableFact]
    public void TheSessionIdentity_IsNotEmpty_AndTheSameEveryTimeItIsAsked()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);

        var session = new WindowsSession();

        Assert.NotEmpty(session.Id);
        Assert.Equal(session.Id, session.Id);
        Assert.Equal(session.Id, new WindowsSession().Id);
    }

    [SkippableFact]
    public void WhereAUserIsSignedIn_TheIdentityIsTheMomentOfSigningIn()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        Skip.IfNot(Environment.UserInteractive, "a process started by a service has no logon to ask for");

        long? logon = WindowsSession.LogonFileTime();

        Assert.True(logon.HasValue, "Windows did not report a logon time for this session; the app would treat every start as a new session.");
        Assert.InRange(DateTimeOffset.FromFileTime(logon.Value), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal($"logon-{logon.Value:x}", new WindowsSession().Id);
    }
}
