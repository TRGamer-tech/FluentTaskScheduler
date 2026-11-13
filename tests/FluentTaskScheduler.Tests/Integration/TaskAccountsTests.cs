using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>Accounts are resolved by Windows, so these need Windows but neither a sandbox nor elevation.</summary>
public class TaskAccountsTests
{
    private const string NeedsWindows = "account names are resolved by Windows";

    [SkippableTheory]
    [InlineData("S-1-5-18", TaskAccountKind.System)]
    [InlineData("SYSTEM", TaskAccountKind.System)]
    [InlineData(@"NT AUTHORITY\SYSTEM", TaskAccountKind.System)]
    [InlineData("S-1-5-19", TaskAccountKind.LocalService)]
    [InlineData("LOCAL SERVICE", TaskAccountKind.LocalService)]
    [InlineData("S-1-5-20", TaskAccountKind.NetworkService)]
    [InlineData("NETWORK SERVICE", TaskAccountKind.NetworkService)]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001", TaskAccountKind.User)]
    [InlineData(@"OTHERPC\nobody", TaskAccountKind.User)]
    public void AUserId_IsClassifiedByWhatItStandsFor(string userId, TaskAccountKind expected)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        Assert.Equal(expected, TaskAccounts.Classify(userId, TaskLogonType.InteractiveToken).Kind);
    }

    [SkippableFact]
    public void TheCurrentUser_IsRecognisedBySidAndByName()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();

        Assert.Equal(TaskAccount.CurrentUser, TaskAccounts.Classify(identity.User!.Value, TaskLogonType.InteractiveToken));
        Assert.Equal(TaskAccount.CurrentUser, TaskAccounts.Classify(identity.Name, TaskLogonType.InteractiveToken));
        Assert.Equal(TaskAccount.CurrentUser, TaskAccounts.Classify(Environment.UserName, TaskLogonType.S4U));
    }

    [SkippableFact]
    public void AnAccountFromAnotherMachine_KeepsItsNameAndIsReportedMissing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        const string foreign = "S-1-5-21-1111111111-2222222222-3333333333-1001";

        Assert.Equal(new TaskAccount(TaskAccountKind.User, foreign), TaskAccounts.Classify(foreign, TaskLogonType.InteractiveToken));
        Assert.False(TaskAccounts.Exists(foreign));
        Assert.True(TaskAccounts.Exists("SYSTEM"));
    }
}
