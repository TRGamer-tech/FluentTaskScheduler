using System.ComponentModel;
using System.Diagnostics;

namespace FluentTaskScheduler.Processes;

/// <inheritdoc cref="IProcessRunner"/>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>How long output that was written before the program ended is given to arrive.</summary>
    private static readonly TimeSpan DrainTime = TimeSpan.FromMilliseconds(500);

    // Creating a process takes long enough to be felt when it happens on the UI thread.
    public Task<int> RunAsync(ProcessRequest request, Action<ProcessOutput> onOutput, CancellationToken cancellation) =>
        Task.Run(() => RunOnThreadPoolAsync(request, onOutput, cancellation), CancellationToken.None);

    private static async Task<int> RunOnThreadPoolAsync(ProcessRequest request, Action<ProcessOutput> onOutput, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(request.FileName, request.Arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = request.OutputEncoding,
                StandardErrorEncoding = request.OutputEncoding
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new ProcessStartException(request.FileName, ex);
        }

        // Nobody can answer a prompt: a program that asks for input reads "end of input" and
        // carries on or fails, instead of waiting forever.
        process.StandardInput.Close();

        var gate = new object();
        bool ended = false;

        Task Pump(StreamReader reader, bool isError) => ConsoleLines.ReadAsync(reader, (line, isComplete) =>
        {
            lock (gate)
            {
                if (!ended) onOutput(new ProcessOutput(line, isError, isComplete));
            }
        });

        void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
            {
                Serilog.Log.Warning(ex, "{Program} could not be stopped completely", request.FileName);
            }
        }

        Task reading = Task.WhenAll(Pump(process.StandardOutput, isError: false), Pump(process.StandardError, isError: true));

        int exitCode;
        using (cancellation.Register(Kill))
        {
            await process.WaitForExitAsync(CancellationToken.None);
            exitCode = process.ExitCode;
        }

        // The output streams only end when every process holding them has ended, and a program
        // started by this one may have inherited them and run on. What this one wrote arrives
        // within moments; nothing after that is waited for.
        await Task.WhenAny(reading, Task.Delay(DrainTime, CancellationToken.None));
        lock (gate) ended = true;

        // A read that is still waiting keeps the streams in use, so the process object has to stay until it returns.
        _ = reading.ContinueWith(done =>
        {
            if (done.Exception is { } failure) Serilog.Log.Warning(failure, "Reading the output of {Program} failed", request.FileName);
            process.Dispose();
        }, TaskScheduler.Default);

        cancellation.ThrowIfCancellationRequested();
        return exitCode;
    }
}
