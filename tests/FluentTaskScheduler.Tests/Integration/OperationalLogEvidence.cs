using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Win32.TaskScheduler;
using Xunit.Abstractions;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FluentTaskScheduler.Tests.Integration;

/// <summary>
/// Not a test of the app. It records what the Task Scheduler operational log really contains for
/// each kind of run (and what subscribing to the log does while it is switched off), so the
/// history model is built on observed events instead of on what the documentation suggests.
/// The findings are its output; it only fails when a scenario could not be carried out.
/// </summary>
[Collection(SchedulerSandbox.Collection)]
public sealed class OperationalLogEvidence
{
    private const string Channel = "Microsoft-Windows-TaskScheduler/Operational";
    private const string Provider = "Microsoft-Windows-TaskScheduler";
    private const string LongRunning = "ping -n 30 127.0.0.1 > nul";
    private const string MissingExe = @"C:\FtsTests\does-not-exist.exe";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(1);

    private readonly ITestOutputHelper _out;
    private readonly HashSet<int> _shownAsXml = [];
    private readonly HashSet<long> _reported = [];
    private readonly List<string> _failures = [];
    private DateTime _sinceUtc;
    private string _folderPath = "";
    private TaskService? _service;

    public OperationalLogEvidence(ITestOutputHelper output) => _out = output;

    [SkippableFact]
    public void TheOperationalLog_ShowsWhatEachKindOfRunLeavesBehind()
    {
        Skip.IfNot(SchedulerSandbox.IsElevated, "needs an elevated token");

        _sinceUtc = DateTime.UtcNow.AddSeconds(-1);
        bool wasEnabled = IsChannelEnabled();
        Line($"Channel enabled before the test: {wasEnabled}");
        PrintChannelConfiguration();

        Recorder? subscribedWhileDisabled = null;
        Recorder? subscribedWhileEnabled = null;
        try
        {
            using var sandbox = new SchedulerSandbox();
            _service = sandbox.Service;
            _folderPath = sandbox.FolderPath;
            try
            {
                Scenario("The channel is switched off: what do a subscription and a query do?", () =>
                {
                    if (wasEnabled) SetChannelEnabled(false);
                    Line($"  enabled now: {IsChannelEnabled()}");
                    Line("  newest event readable while disabled: " + Safe(DescribeNewestEvent));
                    subscribedWhileDisabled = new Recorder("subscribed while the channel was disabled");
                });

                var enabling = Stopwatch.StartNew();
                Scenario("Switching the channel on", () =>
                {
                    SetChannelEnabled(true);
                    Line($"  enabled now: {IsChannelEnabled()} (saving took {enabling.ElapsedMilliseconds} ms)");
                    subscribedWhileEnabled = new Recorder("subscribed after the channel was enabled");
                });

                // Registered first and collected last: its minute passes while the other scenarios run.
                string? limited = null;
                DateTime limitedStart = DateTime.Now.AddSeconds(10);
                Scenario("(f) registration of the task with an execution time limit, started by a time trigger", () =>
                {
                    limited = Register(sandbox, "F_TimeLimit", definition =>
                    {
                        definition.Settings.ExecutionTimeLimit = TimeLimit;
                        definition.Triggers.Add(new TimeTrigger(limitedStart));
                    }, Cmd("ping -n 300 127.0.0.1 > nul"));
                    Line($"  trigger at {limitedStart:HH:mm:ss} local time, limit {TimeLimit}");
                });

                Scenario("How long after enabling does the first run show up in the log?", () =>
                {
                    string warmUp = Register(sandbox, "W_WarmUp", null, Cmd("exit 0"));
                    Start(warmUp);
                    bool logged = WaitUntil(() => ReadLog(withText: false).Any(e => Mentions(e, warmUp) && e.Id is not (106 or 140)), TimeSpan.FromSeconds(30));
                    Line($"  first run event visible: {logged}, {enabling.ElapsedMilliseconds} ms after enabling started");
                    WaitForEnd(warmUp);
                    Report(warmUp);
                });

                Scenario("(a) one action that exits with 0", () => Finish(Register(sandbox, "A_ExitZero", null, Cmd("exit 0"))));

                Scenario("(b) one action that exits with 3", () => Finish(Register(sandbox, "B_ExitThree", null, Cmd("exit 3"))));

                // The log showed exit code 3 as 2147942403; these two tell how larger and negative codes are logged.
                Scenario("(b2) one action that exits with 70000, which does not fit in 16 bits", () =>
                    Finish(Register(sandbox, "B2_ExitLarge", null, Cmd("exit 70000"))));

                Scenario("(b3) one action that exits with -1", () => Finish(Register(sandbox, "B3_ExitNegative", null, Cmd("exit -1"))));

                Scenario("(c) an executable that does not exist", () =>
                    Finish(Register(sandbox, "C_MissingExe", null, new ExecAction(MissingExe))));

                Scenario("(d) a long-running action, stopped through the API after it started", () =>
                {
                    string task = Register(sandbox, "D_Stopped", null, Cmd(LongRunning));
                    Start(task);
                    Line($"  reached Running: {WaitUntil(() => StateOf(task) == TaskState.Running, TimeSpan.FromSeconds(15))}");
                    WaitForQuietLog();
                    Stop(task);
                    WaitForEnd(task);
                    Report(task);
                });

                Scenario("(e) two actions: the first exits with 2, the second with 0", () =>
                    Finish(Register(sandbox, "E_TwoActions", null, Cmd("exit 2"), Cmd("exit 0"))));

                Scenario("(g) started again while an instance is running, policy IgnoreNew", () =>
                {
                    string task = Register(sandbox, "G_IgnoreNew", definition => definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew, Cmd(LongRunning));
                    Start(task);
                    Line($"  reached Running: {WaitUntil(() => StateOf(task) == TaskState.Running, TimeSpan.FromSeconds(15))}");
                    WaitForQuietLog();
                    Line("  second request, plain Run():");
                    Start(task, ignoreConstraints: false);
                    WaitForQuietLog();
                    Line("  third request, RunEx(IgnoreConstraints) as the app starts tasks:");
                    Start(task);
                    WaitForQuietLog();
                    Line($"  running instances now: {Safe(() => Fresh(task).GetInstances().Count.ToString(CultureInfo.InvariantCulture))}");
                    Report(task);
                    Line("  now stopping the instance that is still running");
                    Stop(task);
                    WaitForEnd(task);
                    Report(task);
                });

                Scenario("(h) two instances of one task at the same time, policy Parallel", () =>
                {
                    string task = Register(sandbox, "H_Parallel", definition => definition.Settings.MultipleInstances = TaskInstancesPolicy.Parallel, Cmd("ping -n 4 127.0.0.1 > nul"));
                    Start(task);
                    Thread.Sleep(700);
                    Start(task);
                    WaitForEnd(task);
                    Report(task);
                });

                Scenario("(i) a task is registered, changed, disabled, enabled and deleted", () =>
                {
                    string path = Register(sandbox, "I_Lifecycle", null, Cmd("exit 0"));
                    var task = Fresh(path);
                    task.Definition.RegistrationInfo.Description = "changed";
                    task.RegisterChanges();
                    task.Enabled = false;
                    task.Enabled = true;
                    sandbox.Folder.DeleteTask("I_Lifecycle");
                    WaitForQuietLog();
                    ReportEvents(path);
                });

                Scenario("(j) the stored password of the task's account is no longer valid", () =>
                {
                    string user = "ftsev" + Guid.NewGuid().ToString("N")[..6];
                    string password = "Ft5!" + Guid.NewGuid().ToString("N")[..9]; // net.exe asks for confirmation above 14 characters
                    Line("  " + Shell("net.exe", $"user {user} {password} /add"));
                    try
                    {
                        var definition = sandbox.Service.NewTask();
                        definition.Actions.Add(Cmd("exit 0"));
                        string task = sandbox.Folder.RegisterTaskDefinition("J_LogonFails", definition, TaskCreation.CreateOrUpdate, user, password, TaskLogonType.Password).Path;
                        Line("  " + Shell("net.exe", $"user {user} Zz9!{Guid.NewGuid().ToString("N")[..9]}"));
                        Finish(task);
                    }
                    finally
                    {
                        Line("  " + Shell("net.exe", $"user {user} /delete"));
                    }
                });

                Scenario("(k1) two actions: the first exits with 0, the second cannot be launched", () =>
                    Finish(Register(sandbox, "K1_SecondMissing", null, Cmd("exit 0"), new ExecAction(MissingExe))));

                Scenario("(k2) two actions: the first cannot be launched, the second exits with 0", () =>
                    Finish(Register(sandbox, "K2_FirstMissing", null, new ExecAction(MissingExe), Cmd("exit 0"))));

                Scenario("(m) started again while an instance is running, policy StopExisting", () =>
                {
                    string task = Register(sandbox, "M_StopExisting", definition => definition.Settings.MultipleInstances = TaskInstancesPolicy.StopExisting, Cmd(LongRunning));
                    Start(task);
                    Line($"  reached Running: {WaitUntil(() => StateOf(task) == TaskState.Running, TimeSpan.FromSeconds(15))}");
                    WaitForQuietLog();
                    Line("  second request:");
                    Start(task);
                    WaitForQuietLog();
                    Report(task);
                    Line("  now stopping the instance that is still running");
                    Stop(task);
                    WaitForEnd(task);
                    Report(task);
                });

                Scenario("(n) started again while an instance is running, policy Queue", () =>
                {
                    string task = Register(sandbox, "N_Queue", definition => definition.Settings.MultipleInstances = TaskInstancesPolicy.Queue, Cmd("ping -n 4 127.0.0.1 > nul"));
                    Start(task);
                    Line($"  reached Running: {WaitUntil(() => StateOf(task) == TaskState.Running, TimeSpan.FromSeconds(15))}");
                    Line("  second request:");
                    Start(task);
                    WaitForEnd(task);
                    // The queued instance starts when the first one ends; give it its turn.
                    WaitUntil(() => StateOf(task) is TaskState.Running or TaskState.Queued, TimeSpan.FromSeconds(5));
                    WaitForEnd(task);
                    Report(task);
                });

                Scenario("(f) the task with an execution time limit, started by a time trigger", () =>
                {
                    if (limited is null) throw new InvalidOperationException("The task could not be registered.");
                    TimeSpan remaining = limitedStart + TimeLimit + TimeSpan.FromSeconds(50) - DateTime.Now;
                    bool started = WaitUntil(() => StateOf(limited) == TaskState.Running || Fresh(limited).LastRunTime > DateTime.Now.AddMinutes(-10), TimeSpan.FromSeconds(30));
                    Line($"  started by its trigger: {started}");
                    bool ended = WaitUntil(() => StateOf(limited) != TaskState.Running, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                    Line($"  ended without being stopped by this test: {ended}");
                    WaitForQuietLog();
                    Report(limited);
                    if (!ended)
                    {
                        Stop(limited);
                        WaitForEnd(limited);
                        Report(limited);
                    }
                });
            }
            finally
            {
                foreach (var task in sandbox.Folder.Tasks.ToList())
                {
                    try { task.Stop(); }
                    catch (Exception ex) { Line($"Stopping {task.Path} during clean-up threw {Describe(ex)}"); }
                }
            }
        }
        finally
        {
            // Deleting the sandbox logged its own events; give them a moment before the final reading.
            List<LoggedEvent> log = [];
            Scenario("Events in the same period that belong to none of the tasks above", () =>
            {
                WaitForQuietLog();
                log = ReadLog(withText: true);
                foreach (var e in log.Where(e => !_reported.Contains(e.RecordId))) Print(e, "");
            });
            subscribedWhileDisabled?.Dispose();
            subscribedWhileEnabled?.Dispose();
            Scenario("What the two subscriptions received", () =>
            {
                subscribedWhileDisabled?.Print(Line, log);
                subscribedWhileEnabled?.Print(Line, log);
            });
            Scenario("What the provider declares for each event", PrintProviderMetadata);
            Scenario("Putting the channel back", () =>
            {
                SetChannelEnabled(wasEnabled);
                Line($"  enabled now: {IsChannelEnabled()} (before the test: {wasEnabled})");
            });
        }

        Assert.Empty(_failures);
    }

    private void Scenario(string title, System.Action body)
    {
        Line("");
        Line("=== " + title);
        try
        {
            body();
        }
        catch (Exception ex)
        {
            _failures.Add(title);
            Line("  FAILED: " + ex);
        }
    }

    private static ExecAction Cmd(string command) => new("cmd.exe", "/c " + command);

    /// <returns>The path of the registered task.</returns>
    private static string Register(SchedulerSandbox sandbox, string name, Action<TaskDefinition>? configure, params ExecAction[] actions)
    {
        var definition = sandbox.Service.NewTask();
        definition.RegistrationInfo.Description = "Operational log evidence";
        foreach (var action in actions) definition.Actions.Add(action);
        configure?.Invoke(definition);
        return sandbox.Folder.RegisterTaskDefinition(name, definition).Path;
    }

    /// <summary>
    /// A new object for the task every time. The object a task was started through must not be
    /// kept next to the instance object the start returns: both hold the same COM reference, so
    /// releasing one leaves the other unusable.
    /// </summary>
    private ScheduledTask Fresh(string path) =>
        _service!.GetTask(path) ?? throw new InvalidOperationException($"There is no task at {path}.");

    private TaskState StateOf(string path) => Fresh(path).State;

    private void Finish(string path)
    {
        Start(path);
        WaitForEnd(path);
        Report(path);
    }

    private void Start(string path, bool ignoreConstraints = true)
    {
        string call = ignoreConstraints ? "RunEx(IgnoreConstraints)" : "Run()";
        try
        {
            var task = Fresh(path);
            RunningTask? instance = ignoreConstraints ? task.RunEx(TaskRunFlags.IgnoreConstraints, 0, null) : task.Run();
            Line($"  {call} returned {(instance is null ? "null" : "instance " + Safe(() => instance.InstanceGuid.ToString("B")))}");
        }
        catch (Exception ex)
        {
            Line($"  {call} threw {Describe(ex)}");
        }
    }

    private void Stop(string path)
    {
        try
        {
            Fresh(path).Stop();
            Line("  Stop() returned");
        }
        catch (Exception ex)
        {
            Line($"  Stop() threw {Describe(ex)}");
        }
    }

    /// <summary>Waits until the run that was just requested is over and the log has caught up with it.</summary>
    private void WaitForEnd(string path)
    {
        bool attempted = WaitUntil(
            () => StateOf(path) is TaskState.Running or TaskState.Queued || ReadLog(withText: false).Any(e => Mentions(e, path) && e.Id is not (106 or 140)),
            TimeSpan.FromSeconds(20));
        if (!attempted) Line("  nothing happened within 20 s of the start request");

        bool ended = WaitUntil(() => StateOf(path) is not (TaskState.Running or TaskState.Queued), TimeSpan.FromSeconds(60));
        if (!ended) Line("  still running after 60 s");
        WaitForQuietLog();
    }

    /// <summary>Events reach the log a moment after the fact; this waits until no new ones arrive for the sandbox.</summary>
    private void WaitForQuietLog()
    {
        int last = -1;
        var quiet = Stopwatch.StartNew();
        var total = Stopwatch.StartNew();
        while (total.Elapsed < TimeSpan.FromSeconds(12))
        {
            int count = ReadLog(withText: false).Count(e => Mentions(e, _folderPath));
            if (count != last)
            {
                last = count;
                quiet.Restart();
            }
            else if (quiet.Elapsed > TimeSpan.FromSeconds(1.5))
            {
                return;
            }
            Thread.Sleep(250);
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }

    private void Report(string path)
    {
        var task = Fresh(path);
        Line($"  state={Safe(() => task.State.ToString())} LastTaskResult={Safe(() => $"0x{task.LastTaskResult:X8} ({task.LastTaskResult})")} " +
             $"LastRunTime={Safe(() => task.LastRunTime.ToString("O", CultureInfo.InvariantCulture))} missedRuns={Safe(() => task.NumberOfMissedRuns.ToString(CultureInfo.InvariantCulture))}");
        ReportEvents(path);
    }

    /// <summary>
    /// Prints the events of one task that were not printed before: those that name the task, and
    /// those that only share an activity ID with one that does.
    /// </summary>
    private void ReportEvents(string taskPath)
    {
        var log = ReadLog(withText: true);
        var naming = log.Where(e => Mentions(e, taskPath)).ToList();
        var activities = naming.Where(e => e.ActivityId is { } id && id != Guid.Empty).Select(e => e.ActivityId!.Value).ToHashSet();

        int printed = 0;
        foreach (var e in log)
        {
            bool names = naming.Contains(e);
            if (!names && !(e.ActivityId is { } id && activities.Contains(id))) continue;
            if (!_reported.Add(e.RecordId)) continue;
            Print(e, names ? "" : " (attributed by activity ID only)");
            printed++;
        }
        if (printed == 0) Line("  no new events for " + taskPath);
    }

    private void Print(LoggedEvent e, string note)
    {
        Line($"  #{e.RecordId} {e.TimeUtc.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)} id={e.Id} v{e.Version} level={e.Level} ({e.LevelName}) " +
             $"task={e.Task} ({e.TaskName}) opcode={e.Opcode} ({e.OpcodeName}) keywords=0x{e.Keywords:X} " +
             $"activity={Show(e.ActivityId)} related={Show(e.RelatedActivityId)} pid={e.ProcessId} tid={e.ThreadId} user={e.User}{note}");
        Line("      data: " + (e.Data.Count == 0 ? "(none)" : string.Join(" | ", e.Data.Select(d => $"{d.Key}={d.Value}"))));
        Line("      text: " + Flat(e.Message));
        if (_shownAsXml.Add(e.Id)) Line("      xml:  " + Flat(e.Xml));
    }

    private static bool Mentions(LoggedEvent e, string path) =>
        e.Data.Any(d => d.Value.Contains(path, StringComparison.OrdinalIgnoreCase));

    private static string Show(Guid? id) => id is { } value ? value.ToString("B") : "(none)";

    private static string Flat(string? text) => (text ?? "(null)").ReplaceLineEndings(" ");

    private sealed record LoggedEvent(
        long RecordId, int Id, byte Version, DateTime TimeUtc, byte Level, string LevelName, int Task, string TaskName,
        short Opcode, string OpcodeName, long Keywords, Guid? ActivityId, Guid? RelatedActivityId, int ProcessId, int ThreadId,
        string User, IReadOnlyList<KeyValuePair<string, string>> Data, string Message, string Xml);

    /// <summary>Every event in the channel since the test began, oldest first.</summary>
    /// <param name="withText">Also look up the display names and the message, which is slow.</param>
    private List<LoggedEvent> ReadLog(bool withText)
    {
        string since = _sinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var events = new List<LoggedEvent>();
        using var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName, $"*[System[TimeCreated[@SystemTime>='{since}']]]"));
        while (reader.ReadEvent() is { } record)
        {
            using (record) events.Add(ToLoggedEvent(record, withText));
        }
        return events;
    }

    private static LoggedEvent ToLoggedEvent(EventRecord record, bool withText)
    {
        string xml = Safe(record.ToXml);
        var data = new List<KeyValuePair<string, string>>();
        try
        {
            foreach (var element in XElement.Parse(xml).Descendants().Where(e => e.Name.LocalName == "Data"))
                data.Add(new((string?)element.Attribute("Name") ?? "(unnamed)", element.Value));
        }
        catch (System.Xml.XmlException ex)
        {
            data.Add(new("(the event XML could not be parsed)", ex.Message));
        }

        return new LoggedEvent(
            record.RecordId ?? -1, record.Id, record.Version ?? 0, (record.TimeCreated ?? default).ToUniversalTime(),
            record.Level ?? 0, withText ? Safe(() => record.LevelDisplayName) : "",
            record.Task ?? 0, withText ? Safe(() => record.TaskDisplayName) : "",
            record.Opcode ?? 0, withText ? Safe(() => record.OpcodeDisplayName) : "",
            record.Keywords ?? 0, record.ActivityId, record.RelatedActivityId, record.ProcessId ?? 0, record.ThreadId ?? 0,
            record.UserId?.Value ?? "(none)", data, withText ? Safe(record.FormatDescription) : "", xml);
    }

    private static string DescribeNewestEvent()
    {
        using var reader = new EventLogReader(new EventLogQuery(Channel, PathType.LogName) { ReverseDirection = true });
        using var newest = reader.ReadEvent();
        return newest is null
            ? "the query works and the log is empty"
            : $"the query works; newest is #{newest.RecordId}, id {newest.Id}, from {newest.TimeCreated?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}";
    }

    private static bool IsChannelEnabled()
    {
        using var configuration = new EventLogConfiguration(Channel);
        return configuration.IsEnabled;
    }

    private static void SetChannelEnabled(bool enabled)
    {
        using var configuration = new EventLogConfiguration(Channel);
        if (configuration.IsEnabled == enabled) return;
        configuration.IsEnabled = enabled;
        configuration.SaveChanges();
    }

    private void PrintChannelConfiguration()
    {
        try
        {
            using var configuration = new EventLogConfiguration(Channel);
            Line($"  type={configuration.LogType} mode={configuration.LogMode} maxSize={configuration.MaximumSizeInBytes} file={configuration.LogFilePath}");
            Line($"  access={configuration.SecurityDescriptor}");
        }
        catch (Exception ex)
        {
            Line("  the channel configuration could not be read: " + Describe(ex));
        }
    }

    private void PrintProviderMetadata()
    {
        using var metadata = new ProviderMetadata(Provider);
        foreach (var e in metadata.Events.OrderBy(e => e.Id).ThenBy(e => e.Version))
        {
            Line($"  {e.Id} v{e.Version} log={Safe(() => e.LogLink.LogName)} level={Safe(() => e.Level.Value.ToString(CultureInfo.InvariantCulture))} " +
                 $"task={Safe(() => $"{e.Task.Value} {e.Task.DisplayName}")} opcode={Safe(() => $"{e.Opcode.Value} {e.Opcode.DisplayName}")} " +
                 $"fields=({Safe(() => Fields(e.Template))}) :: {Flat(Safe(() => e.Description))}");
        }
    }

    private static string Fields(string? template) =>
        string.IsNullOrWhiteSpace(template)
            ? ""
            : string.Join(", ", XElement.Parse(template).Elements().Select(field => $"{(string?)field.Attribute("name")}:{(string?)field.Attribute("inType")}"));

    private static string Shell(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return $"{file} exit {process.ExitCode}: {Flat(output.Trim())}";
    }

    private static string Safe(Func<string?> read)
    {
        try
        {
            return read() ?? "(null)";
        }
        catch (Exception ex)
        {
            return $"(threw {Describe(ex)})";
        }
    }

    private static string Describe(Exception? ex) =>
        ex is null ? "(no exception)" : $"{ex.GetType().Name} 0x{ex.HResult:X8}: {Flat(ex.Message)}";

    private void Line(string text) => _out.WriteLine(text);

    /// <summary>A subscription to the whole channel that notes what it is given, when and on which thread.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<string> _notes = [];
        private readonly List<(long RecordId, int Id, DateTime CreatedUtc, DateTime ReceivedUtc, int Thread)> _received = [];
        private readonly EventLogWatcher _watcher = new(new EventLogQuery(Channel, PathType.LogName));
        private readonly string _name;

        public Recorder(string name)
        {
            _name = name;
            _watcher.EventRecordWritten += OnEvent;
            try
            {
                _watcher.Enabled = true;
                Note($"Enabled = true returned on thread {Environment.CurrentManagedThreadId}");
            }
            catch (Exception ex)
            {
                Note("Enabled = true threw " + Describe(ex));
            }
        }

        private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord is null)
                {
                    Note($"callback without a record on thread {Environment.CurrentManagedThreadId}: {Describe(e.EventException)}");
                    return;
                }

                using var record = e.EventRecord;
                lock (_gate)
                {
                    _received.Add((record.RecordId ?? -1, record.Id, (record.TimeCreated ?? default).ToUniversalTime(), DateTime.UtcNow, Environment.CurrentManagedThreadId));
                }
            }
            catch (Exception ex)
            {
                Note("the callback threw " + Describe(ex));
            }
        }

        private void Note(string text)
        {
            lock (_gate) _notes.Add(text);
        }

        public void Print(Action<string> line, IReadOnlyList<LoggedEvent> log)
        {
            lock (_gate)
            {
                line($"  Watcher '{_name}':");
                foreach (string note in _notes) line("    " + note);

                var seen = _received.Select(r => r.RecordId).ToHashSet();
                var missing = log.Where(e => !seen.Contains(e.RecordId)).Select(e => e.RecordId).ToList();
                line($"    received {_received.Count} events, the log holds {log.Count} for the period; in the log but not received: {missing.Count}" +
                     (missing.Count > 0 ? $" (first #{missing[0]}, last #{missing[^1]})" : ""));
                if (_received.Count == 0) return;

                var delays = _received.Select(r => (r.ReceivedUtc - r.CreatedUtc).TotalMilliseconds).ToList();
                line($"    delay between an event's time and its callback, ms: min {delays.Min():F0}, average {delays.Average():F0}, max {delays.Max():F0}");
                line($"    callback threads: {string.Join(", ", _received.Select(r => r.Thread).Distinct())}");
                line($"    in log order: {_received.Select(r => r.RecordId).SequenceEqual(_received.Select(r => r.RecordId).Order())}");
                line($"    first received: {string.Join(", ", _received.Take(8).Select(r => $"#{r.RecordId} id {r.Id}"))}");
            }
        }

        public void Dispose()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                _watcher.Enabled = false;
                _watcher.Dispose();
                Note($"stopping took {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Note("stopping threw " + Describe(ex));
            }
        }
    }
}
