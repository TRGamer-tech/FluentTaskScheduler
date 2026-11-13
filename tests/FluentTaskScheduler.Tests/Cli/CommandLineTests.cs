using System.Text.Json;
using FluentTaskScheduler.Cli;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Cli;

public class CommandLineTests
{
    private readonly FakeTaskStore _store = new FakeTaskStore().Add(@"\Backup").Add(@"\Jobs\Report", enabled: false);
    private readonly FakeSnoozeGate _snooze = new();
    private readonly StringWriter _output = new();

    private int Run(params string[] arguments) =>
        new CommandLine(_store, new TaskRunner(_store, _snooze, new RecordingNotifier()), _output).Run(arguments);

    [Theory]
    [InlineData(true, "--list")]
    [InlineData(true, "--RUN", @"\Backup")]
    [InlineData(true, "/?")]
    [InlineData(true, "--frobnicate")]
    [InlineData(false)]
    [InlineData(false, "--squirrel-install", "1.9.0")]
    [InlineData(false, "--veloapp-updated", "1.9.1")]
    [InlineData(false, "-ToastActivated")]
    [InlineData(false, "-Embedding")]
    [InlineData(false, "--after-restart")]
    [InlineData(false, @"C:\some\file.xml")]
    public void OnlySwitchesMeantForIt_GoToTheCommandLine(bool expected, params string[] arguments) =>
        Assert.Equal(expected, CommandLine.Handles(arguments));

    [Fact]
    public void List_PrintsEveryTaskAsJson()
    {
        Assert.Equal(CommandLine.Success, Run("--list"));

        using var json = JsonDocument.Parse(_output.ToString());
        var tasks = json.RootElement.EnumerateArray().ToList();
        Assert.Equal([@"\Backup", @"\Jobs\Report"], tasks.Select(task => task.GetProperty("Path").GetString()));
        Assert.Equal(["Ready", "Disabled"], tasks.Select(task => task.GetProperty("State").GetString()));
        Assert.Equal("Report", tasks[1].GetProperty("Name").GetString());
    }

    [Fact]
    public void Run_StartsTheTask()
    {
        Assert.Equal(CommandLine.Success, Run("--run", @"\Backup"));
        Assert.True(_store.IsRunning(@"\Backup"));
    }

    [Fact]
    public void Run_WhileTasksAreSnoozed_StartsNothing_AndFails()
    {
        _snooze.Snoozed = true;

        Assert.Equal(CommandLine.Failed, Run("--run", @"\Backup"));

        Assert.False(_store.IsRunning(@"\Backup"));
        Assert.Contains("snoozed", _output.ToString());
    }

    [Fact]
    public void EnableAndDisable_ChangeTheTask()
    {
        Assert.Equal(CommandLine.Success, Run("--enable", @"\Jobs\Report"));
        Assert.True(_store.IsEnabled(@"\Jobs\Report"));

        Assert.Equal(CommandLine.Success, Run("--disable", @"\Jobs\Report"));
        Assert.False(_store.IsEnabled(@"\Jobs\Report"));
    }

    [Theory]
    [InlineData("--run")]
    [InlineData("--enable")]
    [InlineData("--disable")]
    public void ATaskThatDoesNotExist_IsReported_WithAFailingExitCode(string command)
    {
        Assert.Equal(CommandLine.Failed, Run(command, @"\Nope"));
        Assert.Contains(@"Task not found: \Nope", _output.ToString());
    }

    [Fact]
    public void ATaskWindowsProtects_IsReportedAsAccessDenied()
    {
        _store.Protected.Add(@"\Backup");

        Assert.Equal(CommandLine.Failed, Run("--disable", @"\Backup"));
        Assert.Contains("Access denied", _output.ToString());
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("--run")]
    [InlineData("--enable")]
    public void WhatIsNotUnderstood_GetsTheUsage_AndItsOwnExitCode(params string[] arguments)
    {
        Assert.Equal(CommandLine.UsageError, Run(arguments));
        Assert.Contains("usage:", _output.ToString());
        Assert.Empty(_store.Calls);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_PrintsTheUsage_AndSucceeds(string switchName)
    {
        Assert.Equal(CommandLine.Success, Run(switchName));
        Assert.Contains("--list", _output.ToString());
    }
}
