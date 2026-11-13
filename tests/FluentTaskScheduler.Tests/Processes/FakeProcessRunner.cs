using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.Tests.Processes;

/// <summary>
/// Stands in for real programs. Every start is recorded as a <see cref="FakeProcess"/>, which
/// the test then lets write and end; nothing happens by itself or on another thread.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>The programs started so far, oldest first.</summary>
    public List<FakeProcess> Started { get; } = [];

    /// <summary>Set to make the next starts fail the way a missing program does.</summary>
    public Exception? StartFailure { get; set; }

    /// <summary>The program started last.</summary>
    public FakeProcess Last => Started[^1];

    public Task<int> RunAsync(ProcessRequest request, Action<ProcessOutput> onOutput, CancellationToken cancellation)
    {
        if (StartFailure is { } failure) return Task.FromException<int>(new ProcessStartException(request.FileName, failure));

        var process = new FakeProcess(request, onOutput, cancellation);
        Started.Add(process);
        return process.Completion;
    }
}

/// <summary>A program that runs until the test ends it or its cancellation is asked for.</summary>
public sealed class FakeProcess
{
    private readonly TaskCompletionSource<int> _exit = new();
    private readonly Action<ProcessOutput> _onOutput;

    internal FakeProcess(ProcessRequest request, Action<ProcessOutput> onOutput, CancellationToken cancellation)
    {
        Request = request;
        _onOutput = onOutput;
        cancellation.Register(() => _exit.TrySetCanceled(cancellation));
    }

    public ProcessRequest Request { get; }
    public bool WasStopped => _exit.Task.IsCanceled;
    internal Task<int> Completion => _exit.Task;

    public FakeProcess Writes(string line)
    {
        _onOutput(new ProcessOutput(line, IsError: false));
        return this;
    }

    /// <summary>Writes text without ending the line, as a program does for a prompt or a progress display.</summary>
    public FakeProcess WritesUnfinished(string text)
    {
        _onOutput(new ProcessOutput(text, IsError: false, IsComplete: false));
        return this;
    }

    public FakeProcess WritesError(string line)
    {
        _onOutput(new ProcessOutput(line, IsError: true));
        return this;
    }

    public void Exits(int exitCode = 0) => _exit.TrySetResult(exitCode);
}
