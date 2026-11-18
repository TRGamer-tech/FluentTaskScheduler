using System.Runtime.InteropServices;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

public class TaskSchedulerErrorsTests
{
    private const int E_ACCESSDENIED = unchecked((int)0x80070005);
    private const int E_FAIL = unchecked((int)0x80004005);

    [Fact]
    public void AccessDenied_IsRecognisedByItsCode_WhateverTheMessageSays()
    {
        Assert.True(TaskSchedulerErrors.IsAccessDenied(new COMException("Zugriff verweigert", E_ACCESSDENIED)));
        Assert.True(TaskSchedulerErrors.IsAccessDenied(new UnauthorizedAccessException("Accès refusé.")));
        Assert.False(TaskSchedulerErrors.IsAccessDenied(new COMException("Access is denied.", E_FAIL)));
    }

    [Fact]
    public void AccessDenied_IsFoundOnAnInnerException()
    {
        var outer = new InvalidOperationException("The task could not be moved.", new COMException("Zugriff verweigert", E_ACCESSDENIED));

        Assert.True(TaskSchedulerErrors.IsAccessDenied(outer));
    }

    [Fact]
    public void AnythingElse_IsNotAccessDenied()
    {
        Assert.False(TaskSchedulerErrors.IsAccessDenied(new InvalidOperationException("Something else went wrong")));
        Assert.False(TaskSchedulerErrors.IsAccessDenied(null));
    }
}
