using FluentTaskScheduler.Settings;
using Microsoft.Win32;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// Against the real Run key of the current user, under a name of its own that is removed again.
/// Needs Windows, but no elevation.
/// </summary>
public sealed class RunKeyStartupTests : IDisposable
{
    private const string NeedsWindows = "the Run key only exists on Windows";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string Program = @"C:\Program Files\Fts Tests\FluentTaskScheduler.exe";

    private readonly string _name = "FtsTests_" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(_name, throwOnMissingValue: false);
        using RegistryKey? approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, writable: true);
        approval?.DeleteValue(_name, throwOnMissingValue: false);
    }

    private object? RunValue()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(_name);
    }

    [SkippableFact]
    public void Enabling_WritesTheQuotedProgramPath_AndDisablingRemovesIt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        var startup = new RunKeyStartup(_name, Program);
        Assert.False(startup.IsEnabled);

        Assert.True(startup.TrySetEnabled(true));

        Assert.True(startup.IsEnabled);
        Assert.Equal($"\"{Program}\"", RunValue());

        Assert.True(startup.TrySetEnabled(false));

        Assert.False(startup.IsEnabled);
        Assert.Null(RunValue());
    }

    [SkippableFact]
    public void AnEntryForAnotherCopyOfTheApp_DoesNotCount_AndIsTakenOverWhenEnabling()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        new RunKeyStartup(_name, @"D:\Old\FluentTaskScheduler.exe").TrySetEnabled(true);
        var startup = new RunKeyStartup(_name, Program);

        Assert.False(startup.IsEnabled);

        startup.TrySetEnabled(true);

        Assert.True(startup.IsEnabled);
        Assert.Equal($"\"{Program}\"", RunValue());
    }

    [SkippableFact]
    public void Disabling_LeavesTheEntryOfAnotherCopyAlone()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        const string portable = @"D:\Portable\FluentTaskScheduler.exe";
        new RunKeyStartup(_name, portable).TrySetEnabled(true);

        Assert.True(new RunKeyStartup(_name, Program).TrySetEnabled(false));

        Assert.Equal($"\"{portable}\"", RunValue());
    }

    [SkippableFact]
    public void Disabling_RemovesAnEntryThatTaskManagerHadSwitchedOff_AndWhatTaskManagerNotedAboutIt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        var startup = new RunKeyStartup(_name, Program);
        startup.TrySetEnabled(true);
        using (RegistryKey approval = Registry.CurrentUser.CreateSubKey(ApprovalKey))
            approval.SetValue(_name, new byte[] { 3, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0xDA, 0x01 }, RegistryValueKind.Binary);

        Assert.True(startup.TrySetEnabled(false));

        Assert.Null(RunValue());
        using RegistryKey? noted = Registry.CurrentUser.OpenSubKey(ApprovalKey);
        Assert.Null(noted?.GetValue(_name));
    }

    [SkippableFact]
    public void AnEntrySwitchedOffInTaskManager_ReadsAsOff_AndEnablingSwitchesItOnAgain()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        var startup = new RunKeyStartup(_name, Program);
        startup.TrySetEnabled(true);

        // What "Startup apps" writes when the user disables an entry: 03, then the time it happened.
        using (RegistryKey approval = Registry.CurrentUser.CreateSubKey(ApprovalKey))
            approval.SetValue(_name, new byte[] { 3, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0xDA, 0x01 }, RegistryValueKind.Binary);

        Assert.False(startup.IsEnabled);

        Assert.True(startup.TrySetEnabled(true));

        Assert.True(startup.IsEnabled);
    }

    [SkippableFact]
    public void AnEntryAllowedInTaskManager_ReadsAsOn()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), NeedsWindows);
        var startup = new RunKeyStartup(_name, Program);
        startup.TrySetEnabled(true);
        using (RegistryKey approval = Registry.CurrentUser.CreateSubKey(ApprovalKey))
            approval.SetValue(_name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

        Assert.True(startup.IsEnabled);
    }
}
