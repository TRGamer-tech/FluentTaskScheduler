using System;
using System.Runtime.InteropServices;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests
{
    // Covers item 3.9: access-denied detection must rely on the HRESULT, not English/German
    // substring matching that silently fails on any other OS display language.
    public class IsAccessDeniedTests
    {
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);

        [Fact]
        public void ReturnsTrue_ForExceptionWithAccessDeniedHResult()
        {
            var ex = new UnauthorizedAccessException("some message");
            typeof(Exception).GetProperty(nameof(Exception.HResult))!.SetValue(ex, E_ACCESSDENIED);

            Assert.True(TaskServiceWrapper.IsAccessDenied(ex));
        }

        [Fact]
        public void ReturnsTrue_ForComExceptionWithAccessDeniedErrorCode()
        {
            var ex = new COMException("Zugriff verweigert auf Japanisch oder sonst was", E_ACCESSDENIED);
            Assert.True(TaskServiceWrapper.IsAccessDenied(ex));
        }

        [Fact]
        public void ReturnsTrue_WhenAccessDeniedHResultIsOnAnInnerException()
        {
            var inner = new COMException("inner", E_ACCESSDENIED);
            var outer = new InvalidOperationException("outer", inner);

            Assert.True(TaskServiceWrapper.IsAccessDenied(outer));
        }

        [Fact]
        public void ReturnsFalse_ForUnrelatedException()
        {
            var ex = new InvalidOperationException("Something else went wrong");
            Assert.False(TaskServiceWrapper.IsAccessDenied(ex));
        }

        [Fact]
        public void ReturnsFalse_ForNull()
        {
            Assert.False(TaskServiceWrapper.IsAccessDenied(null));
        }

        [Fact]
        public void IsNotFooledByMessageTextAlone_RegressionForOldStringMatchingBehavior()
        {
            // The old implementation also matched on "Access is denied" / "Zugriff verweigert" in
            // the message text. A message that happens to contain similar wording but a completely
            // unrelated HResult must NOT be treated as access-denied.
            var ex = new Exception("Access is denied") { HResult = unchecked((int)0x80004005) }; // E_FAIL
            Assert.False(TaskServiceWrapper.IsAccessDenied(ex));
        }
    }
}
