using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Translates between Task Scheduler principals and the <see cref="TaskAccount"/> the editor shows.</summary>
internal static class TaskAccounts
{
    /// <summary>The user running this app, as DOMAIN\name.</summary>
    public static string CurrentUserName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    public static TaskAccount FromPrincipal(TaskPrincipal principal)
    {
        if (principal.LogonType == TaskLogonType.Group || !string.IsNullOrEmpty(principal.GroupId))
            return new TaskAccount(TaskAccountKind.Group, DisplayName(principal.GroupId ?? ""));

        string id = principal.UserId ?? "";
        return Classify(id, principal.LogonType);
    }

    /// <summary>Works out which account a principal's user ID (a SID or an account name) stands for.</summary>
    internal static TaskAccount Classify(string userId, TaskLogonType logonType)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return logonType == TaskLogonType.ServiceAccount ? TaskAccount.System : TaskAccount.CurrentUser;

        var sid = TryGetSid(userId);
        if (sid is not null)
        {
            if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)) return TaskAccount.System;
            if (sid.IsWellKnown(WellKnownSidType.LocalServiceSid)) return new TaskAccount(TaskAccountKind.LocalService);
            if (sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)) return new TaskAccount(TaskAccountKind.NetworkService);

            using var current = WindowsIdentity.GetCurrent();
            if (current.User is { } currentSid && sid.Equals(currentSid)) return TaskAccount.CurrentUser;
        }

        return new TaskAccount(TaskAccountKind.User, sid is null ? userId : DisplayName(sid.Value));
    }

    /// <summary>
    /// The user ID and logon type to register a task with for the given account. The service
    /// account names are the spellings the TaskScheduler library accepts; what a task reports
    /// as its own user ID ("LOCAL SERVICE") is not among them.
    /// </summary>
    public static (string? UserId, TaskLogonType LogonType) ToRegistration(TaskAccount account) => account.Kind switch
    {
        TaskAccountKind.System => ("SYSTEM", TaskLogonType.ServiceAccount),
        TaskAccountKind.LocalService => (@"NT AUTHORITY\LOCAL SERVICE", TaskLogonType.ServiceAccount),
        TaskAccountKind.NetworkService => (@"NT AUTHORITY\NETWORK SERVICE", TaskLogonType.ServiceAccount),
        TaskAccountKind.Group => (account.Name, TaskLogonType.Group),
        TaskAccountKind.User => (account.Name, TaskLogonType.InteractiveToken),
        _ => (CurrentUserName, TaskLogonType.InteractiveToken)
    };

    /// <summary>The user a "when this user logs on / locks the session" trigger should watch, or null for any user.</summary>
    public static string? TriggerUser(TaskAccount account) => account.Kind switch
    {
        TaskAccountKind.CurrentUser => CurrentUserName,
        TaskAccountKind.User => account.Name,
        _ => null
    };

    /// <summary>Whether Windows knows the account (a SID or a name) on this computer.</summary>
    public static bool Exists(string account) => TryGetSid(account) is { } sid && IsResolvable(sid);

    private static bool IsResolvable(SecurityIdentifier sid)
    {
        try { sid.Translate(typeof(NTAccount)); return true; }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException) { return false; }
    }

    private static SecurityIdentifier? TryGetSid(string account)
    {
        try
        {
            return account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(account)
                : (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException)
        {
            // Unknown here (another machine's account, a deleted user): treat it as a plain name.
            return null;
        }
    }

    /// <summary>Shows a SID as DOMAIN\name where Windows can resolve it; anything else is returned unchanged.</summary>
    internal static string DisplayName(string account)
    {
        if (!account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return account;
        try { return new SecurityIdentifier(account).Translate(typeof(NTAccount)).Value; }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException) { return account; }
    }
}
