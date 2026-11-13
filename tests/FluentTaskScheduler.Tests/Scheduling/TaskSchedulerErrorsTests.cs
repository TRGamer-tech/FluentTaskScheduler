using System.Runtime.InteropServices;

using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

// Access-denied detection relies on the HRESULT: matching the message text would only work
// on a Windows that speaks the language the match was written in.
public class TaskSchedulerErrorsTests
{
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);

    [Fact]
    public void ReturnsTrue_ForExceptionWithAccessDeniedHResult()
    {
        var ex = new UnauthorizedAccessException("some message");
        typeof(Exception).GetProperty(nameof(Exception.HResult))!.SetValue(ex, E_ACCESSDENIED);

        Assert.True(TaskSchedulerErrors.IsAccessDenied(ex));
    }

    [Fact]
    public void ReturnsTrue_ForComExceptionWithAccessDeniedErrorCode()
    {
        var ex = new COMException("Zugriff verweigert auf Japanisch oder sonst was", E_ACCESSDENIED);
        Assert.True(TaskSchedulerErrors.IsAccessDenied(ex));
    }

    [Fact]
    public void ReturnsTrue_WhenAccessDeniedHResultIsOnAnInnerException()
    {
        var inner = new COMException("inner", E_ACCESSDENIED);
        var outer = new InvalidOperationException("outer", inner);

        Assert.True(TaskSchedulerErrors.IsAccessDenied(outer));
    }

    [Fact]
    public void ReturnsFalse_ForUnrelatedException()
    {
        var ex = new InvalidOperationException("Something else went wrong");
        Assert.False(TaskSchedulerErrors.IsAccessDenied(ex));
    }

    [Fact]
    public void ReturnsFalse_ForNull()
    {
        Assert.False(TaskSchedulerErrors.IsAccessDenied(null));
    }

    [Fact]
    public void IsNotFooledByMessageTextAlone_RegressionForOldStringMatchingBehavior()
    {
        // The old implementation also matched on "Access is denied" / "Zugriff verweigert" in
        // the message text. A message that happens to contain similar wording but a completely
        // unrelated HResult must NOT be treated as access-denied.
        var ex = new Exception("Access is denied") { HResult = unchecked((int)0x80004005) }; // E_FAIL
        Assert.False(TaskSchedulerErrors.IsAccessDenied(ex));
    }
}
