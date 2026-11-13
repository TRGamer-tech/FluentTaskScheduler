using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.Tests.Processes;

public sealed class WindowsToolsTests
{
    [Fact]
    public void ACommandLineForAUtf8Console_SwitchesTheCodePageFirst_AndIsReadAsUtf8()
    {
        ProcessRequest request = WindowsTools.InUtf8Console("\"C:\\Tools\\tool.exe\" /a \"b c\"");

        Assert.Equal(WindowsTools.InSystemFolder("cmd.exe"), request.FileName);
        Assert.Equal($"/d /v:off /s /c \"\"{WindowsTools.InSystemFolder("chcp.com")}\" 65001 >nul & \"C:\\Tools\\tool.exe\" /a \"b c\"\"", request.Arguments);
        Assert.Equal(Encoding.UTF8, request.OutputEncoding);
    }
}

/// <summary>Runs real programs; Windows only.</summary>
public sealed class ProcessRunnerTests
{
    private const string SkipReason = "runs Windows programs";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ProcessRunner _runner = new();
    private readonly ConcurrentQueue<ProcessOutput> _output = new();

    /// <summary>The lines as they ended up, without the states they went through while they were being written.</summary>
    private IEnumerable<ProcessOutput> Lines => _output.Where(line => line.IsComplete);

    private Task<int> Run(string commandLine, CancellationToken cancellation = default) =>
        _runner.RunAsync(WindowsTools.InUtf8Console(commandLine), _output.Enqueue, cancellation).WaitAsync(Patience);

    [SkippableFact]
    public async Task WhatAProgramWrites_ArrivesIntact_InLanguagesTheCodePageOfThisMachineCannotShow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        int exitCode = await Run("echo Zürich 東京 Ελλάδα");

        Assert.Equal(0, exitCode);
        Assert.Equal([new ProcessOutput("Zürich 東京 Ελλάδα", IsError: false)], Lines);
    }

    [SkippableFact]
    public async Task OutputAndErrors_AreToldApart_AndEachKeepsItsOrder()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        await Run("echo one& echo two& echo oops 1>&2& echo three");

        Assert.Equal(["one", "two", "three"], Lines.Where(line => !line.IsError).Select(line => line.Text.Trim()));
        Assert.Equal(["oops"], Lines.Where(line => line.IsError).Select(line => line.Text.Trim()));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task TheExitCode_IsThatOfTheCommand(int exitCode)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        Assert.Equal(exitCode, await Run($"exit {exitCode}"));
    }

    [SkippableFact]
    public async Task Text_IsReportedWhenItIsWritten_NotOnlyOnceItsLineHasEnded()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        // "set /p" writes its prompt without ending the line; the line ends seconds later, after the ping.
        await Run($"set /p answer=Working... & \"{WindowsTools.InSystemFolder("ping.exe")}\" -n 3 127.0.0.1 >nul & echo done");

        Assert.Contains(_output, line => !line.IsComplete && line.Text.TrimEnd() == "Working...");
        Assert.Contains(Lines, line => line.Text.StartsWith("Working...") && line.Text.TrimEnd().EndsWith("done"));
    }

    [SkippableFact]
    public async Task AProgramThatAsksForInput_IsToldThereIsNone_InsteadOfWaitingForever()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        await Run("set /p answer=Continue? & echo done");

        Assert.Contains(Lines, line => line.Text.Contains("done"));
    }

    [SkippableFact]
    public async Task Cancelling_EndsTheProgramAndWhatItStarted_AndTheRunIsCancelled()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        using var cancel = new CancellationTokenSource();

        // waitfor is started by cmd, so stopping has to reach further than the program that was asked for.
        // Nothing ever sends the signal it waits for.
        Task<int> run = Run($"echo started& \"{WindowsTools.InSystemFolder("waitfor.exe")}\" /t 300 FtsTests{Guid.NewGuid():N}", cancel.Token);
        await WaitUntil(() => !_output.IsEmpty && Process.GetProcessesByName("waitfor").Length > 0);
        var watch = Stopwatch.StartNew();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Stopping took {watch.Elapsed}.");
        await WaitUntil(() => Process.GetProcessesByName("waitfor").Length == 0);
    }

    [SkippableFact]
    public async Task ARunThatIsCancelledBeforeItStarts_StartsNothing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("echo never", new CancellationToken(canceled: true)));

        Assert.Empty(_output);
    }

    [SkippableFact]
    public async Task AProgramThatDoesNotExist_FailsWithTheReasonWindowsGives()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        var request = new ProcessRequest(@"C:\No such folder\no-such-program.exe");

        var failure = await Assert.ThrowsAsync<ProcessStartException>(() => _runner.RunAsync(request, _output.Enqueue, CancellationToken.None));

        Assert.Equal(request.FileName, failure.FileName);
        Assert.NotEmpty(failure.Message);
    }

    [SkippableFact]
    public async Task AProgramLeftBehindWithTheOutputStillOpen_DoesNotKeepTheRunFromEnding()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        var watch = Stopwatch.StartNew();

        // "start /b" leaves ping running with the same output as cmd, which itself ends at once.
        int exitCode = await Run($"echo before& start /b \"\" \"{WindowsTools.InSystemFolder("ping.exe")}\" -n 20 127.0.0.1");

        Assert.Equal(0, exitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The run took {watch.Elapsed}.");
        Assert.Contains(Lines, line => line.Text.Trim() == "before");

        int count = _output.Count;
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(count, _output.Count);
    }

    [SkippableFact]
    public async Task ProgramsThatWriteUtf16_AreReadWithTheEncodingTheRequestNames()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        // cmd /u writes the output of its own commands as UTF-16, as sfc.exe does with everything.
        var request = new ProcessRequest(WindowsTools.InSystemFolder("cmd.exe"), "/u /c echo Zürich 東京") { OutputEncoding = Encoding.Unicode };

        await _runner.RunAsync(request, _output.Enqueue, CancellationToken.None).WaitAsync(Patience);

        Assert.Equal([new ProcessOutput("Zürich 東京", IsError: false)], Lines);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Patience, "Timed out waiting.");
            await Task.Delay(50);
        }
    }
}
