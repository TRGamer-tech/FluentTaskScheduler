using FluentTaskScheduler.Scheduling;
using Serilog;

namespace FluentTaskScheduler.Snoozing;

/// <summary>
/// The snooze as a second process sees it. The command line runs for a moment, often while the
/// app itself is running, and has to honour the snooze without becoming a second
/// <see cref="SnoozeService"/>: it starts no timers, re-enables no tasks and writes nothing.
/// <para>
/// <see cref="BlocksRun"/> reads the state file anew at every call and judges it by the clock and
/// the Windows session of that moment. A snooze that has run out does not block, even if the app
/// has not ended it yet; ending it and re-enabling its tasks stays the app's job. A state file
/// that cannot be read blocks nothing, which is what the app makes of such a file as well.
/// </para>
/// <para>
/// <see cref="RecordSuppressedRun"/> deliberately records nothing; it writes a line to the log.
/// The state file has one writer. A second one would have to read the file, add its line and
/// replace the file, and would thereby undo whatever the app wrote in between. If that was the
/// end of a snooze, the next start would end that snooze once more and enable its tasks again,
/// including any the user has disabled since. Whoever started the command line learns of the
/// refusal from its result.
/// </para>
/// <para>
/// An "until restart" snooze lasts as long as the Windows session it was started in. A process
/// outside that session, such as a scheduled task that runs whether the user is logged on or
/// not, cannot recognise the session and takes such a snooze to be over.
/// </para>
/// </summary>
public sealed class ReadOnlySnoozeGate : ISnoozeGate
{
    private readonly AppPaths _paths;
    private readonly SnoozeStateFile _file;
    private readonly IWindowsSession _session;
    private readonly TimeProvider _time;

    public ReadOnlySnoozeGate(AppPaths paths, IWindowsSession session, TimeProvider? time = null)
    {
        _paths = paths;
        _file = new SnoozeStateFile(paths.File(SnoozeService.StateFileName));
        _session = session;
        _time = time ?? TimeProvider.System;
    }

    public bool BlocksRun(string taskPath)
    {
        if (!_file.TryRead(out SnoozeFileContent? stored)) return false;

        // Until the app has started once after the upgrade, a snooze of version 1.9 is still the one in force.
        stored ??= LegacySnoozeState.Read(_paths).Content;
        if (stored is null) return false;

        return stored.Ledger
            .EndElapsed(_time.GetUtcNow(), _session.Id)
            .ToState(stored.SuppressedRuns, unreadableStateFile: null)
            .BlocksRun(taskPath);
    }

    public void RecordSuppressedRun(string taskPath, RunOrigin origin) =>
        Log.Information("A run of {Path} was refused because of a snooze ({Origin}); a second process does not add it to the app's record", taskPath, origin);
}
