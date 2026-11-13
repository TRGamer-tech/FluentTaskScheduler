using System.Text;

namespace FluentTaskScheduler.Processes;

/// <summary>A program to run without a window, with everything it writes captured.</summary>
/// <param name="FileName">The program; a full path unless it is meant to be searched for.</param>
/// <param name="Arguments">Its command line after the program name, quoted as the program expects.</param>
public sealed record ProcessRequest(string FileName, string Arguments = "")
{
    /// <summary>
    /// The encoding the program writes in. Console programs use the code page of their console
    /// unless they are told otherwise; see <see cref="WindowsTools.InUtf8Console"/>.
    /// </summary>
    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    /// <summary>The folder the program starts in; null for the current folder of this app.</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>One line a program wrote, as it stands at the moment.</summary>
/// <param name="IsError">True for a line written to the error stream.</param>
/// <param name="IsComplete">
/// False for a line the program has not ended yet. Whatever the same stream reports next takes
/// its place: the line with more text, or what the program wrote over it (a progress display).
/// </param>
public readonly record struct ProcessOutput(string Text, bool IsError, bool IsComplete = true);

/// <summary>Runs programs and reports what they write while they run.</summary>
public interface IProcessRunner
{
    /// <summary>Starts the program and completes with its exit code once it has ended.</summary>
    /// <param name="onOutput">
    /// Called with what the program writes as it arrives, on a background thread, one line at a
    /// time and never after the returned task has completed. Lines of one stream arrive in the
    /// order they were written; how the two streams interleave is only approximately that of the
    /// program.
    /// </param>
    /// <param name="cancellation">
    /// Ends the program together with everything it started; the returned task is then cancelled.
    /// </param>
    /// <exception cref="ProcessStartException">The program could not be started.</exception>
    Task<int> RunAsync(ProcessRequest request, Action<ProcessOutput> onOutput, CancellationToken cancellation);
}

/// <summary>
/// A program could not be started: it does not exist, is not a program, or Windows refused.
/// The message is the reason Windows gave, in the language of Windows.
/// </summary>
public sealed class ProcessStartException(string fileName, Exception inner) : Exception(inner.Message, inner)
{
    public string FileName { get; } = fileName;
}
