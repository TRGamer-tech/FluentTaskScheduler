using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FluentTaskScheduler.Services
{
    /// <summary>One snoozed task: when it should come back, and how it was found.</summary>
    public class TaskSnoozeEntry
    {
        public string TaskPath { get; set; } = "";

        /// <summary>Absolute end of the snooze, or null when <see cref="UntilReboot"/> is set.</summary>
        public DateTime? UntilUtc { get; set; }

        public bool UntilReboot { get; set; }

        /// <summary>
        /// Boot identity captured when an until-reboot snooze started. A different value means the
        /// machine has since restarted, so the snooze is over.
        /// </summary>
        public string BootStamp { get; set; } = "";

        /// <summary>
        /// Whether the task was enabled when it was snoozed. A task the user had already disabled
        /// themselves must stay disabled when the snooze ends.
        /// </summary>
        public bool WasEnabled { get; set; } = true;
    }

    /// <summary>
    /// Per-task snooze: disables a single task for a while and re-enables it when the window
    /// elapses. Independent of <see cref="SnoozeService"/>, which pauses everything at once.
    ///
    /// State lives in its own file rather than settings.json so that it is never carried into a
    /// settings export — a snooze is machine-local runtime state, like the global snooze (4.5).
    /// It is written before the task is touched, so a crash mid-operation still leaves a record
    /// that <see cref="Initialize"/> can finish acting on.
    /// </summary>
    public static class TaskSnoozeService
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, TaskSnoozeEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private static Timer? _expiryTimer;

        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "task_snoozes.json");

        /// <summary>Raised when any task's snooze starts, is cancelled, or elapses.</summary>
        public static event EventHandler? TaskSnoozeChanged;

        /// <summary>Factory for the task-service boundary, overridable in unit tests.</summary>
        public static Func<ITaskServiceWrapper> TaskServiceFactory { get; set; } = () => new TaskServiceWrapper();

        /// <summary>Test-only hook so tests can await the background restore deterministically.</summary>
        internal static System.Threading.Tasks.Task? LastBackgroundOperation { get; private set; }

        // ── State ───────────────────────────────────────────────────────────────

        /// <summary>True while the given task is snoozed. Expired entries report false.</summary>
        public static bool IsSnoozed(string taskPath)
        {
            if (string.IsNullOrWhiteSpace(taskPath)) return false;
            lock (_lock)
            {
                return _entries.TryGetValue(taskPath, out var e) && !HasElapsed(e);
            }
        }

        /// <summary>The live snooze for a task, or null when it is not snoozed.</summary>
        public static TaskSnoozeEntry? GetSnooze(string taskPath)
        {
            if (string.IsNullOrWhiteSpace(taskPath)) return null;
            lock (_lock)
            {
                if (_entries.TryGetValue(taskPath, out var e) && !HasElapsed(e)) return e;
            }
            return null;
        }

        /// <summary>Human-readable snooze status for a task, or "" when it is not snoozed.</summary>
        public static string StatusTextFor(string taskPath)
        {
            var entry = GetSnooze(taskPath);
            if (entry == null) return "";

            if (entry.UntilReboot)
            {
                return LocalizationService.GetString("Snooze.Status.UntilReboot", "Snoozed until restart");
            }

            if (entry.UntilUtc.HasValue)
            {
                string when = entry.UntilUtc.Value.ToLocalTime().ToString("g");
                return string.Format(
                    LocalizationService.GetString("Task.Snooze.Status", "Snoozed until {0}"), when);
            }

            return LocalizationService.GetString("Snooze.Status.Generic", "Snoozed");
        }

        /// <summary>
        /// Culture-invariant description for the log. Deliberately not <see cref="StatusTextFor"/>:
        /// log lines are diagnostics and must stay readable regardless of the UI language, and
        /// going through the resource APIs from a background/expiry thread is not worth the risk.
        /// </summary>
        private static string DescribeForLog(TaskSnoozeEntry e) =>
            e.UntilReboot
                ? "until reboot"
                : e.UntilUtc.HasValue
                    ? "until " + e.UntilUtc.Value.ToString("O")
                    : "indefinitely";

        private static bool HasElapsed(TaskSnoozeEntry e)
        {
            if (e.UntilReboot)
            {
                return !string.Equals(e.BootStamp, CurrentBootStamp(), StringComparison.Ordinal);
            }
            return !e.UntilUtc.HasValue || e.UntilUtc.Value <= DateTime.UtcNow;
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>
        /// Restores state from disk, re-enables anything whose snooze elapsed while the app was
        /// closed, and starts the timer that ends snoozes during this session.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                Load();

                List<TaskSnoozeEntry> elapsed;
                lock (_lock)
                {
                    elapsed = _entries.Values.Where(HasElapsed).ToList();
                    foreach (var e in elapsed) _entries.Remove(e.TaskPath);
                }

                if (elapsed.Count > 0)
                {
                    LogService.Info($"{elapsed.Count} task snooze(s) elapsed while the app was closed; restoring them.");
                    Save();
                    LastBackgroundOperation = System.Threading.Tasks.Task.Run(() => RestoreTasks(elapsed));
                }

                _expiryTimer?.Dispose();
                _expiryTimer = new Timer(CheckExpiry, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to initialize TaskSnoozeService", ex);
            }
        }

        public static void Shutdown()
        {
            _expiryTimer?.Dispose();
            _expiryTimer = null;
        }

        private static void CheckExpiry(object? state)
        {
            try
            {
                List<TaskSnoozeEntry> elapsed;
                lock (_lock)
                {
                    elapsed = _entries.Values.Where(HasElapsed).ToList();
                    if (elapsed.Count == 0) return;
                    foreach (var e in elapsed) _entries.Remove(e.TaskPath);
                }

                Save();
                RestoreTasks(elapsed);
                TaskSnoozeChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogService.Error("Task snooze expiry check failed", ex);
            }
        }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Snoozes a single task for a fixed duration.</summary>
        public static void Snooze(string taskPath, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                LogService.Warn($"Ignoring task snooze for '{taskPath}' with non-positive duration '{duration}'.");
                return;
            }
            Apply(taskPath, DateTime.UtcNow.Add(duration), untilReboot: false);
        }

        /// <summary>Snoozes a single task until the machine restarts.</summary>
        public static void SnoozeUntilReboot(string taskPath) => Apply(taskPath, null, untilReboot: true);

        /// <summary>Snoozes a single task until a specific local time (rolls to tomorrow if already past).</summary>
        public static void SnoozeUntilLocalTime(string taskPath, DateTime localEnd)
        {
            var end = localEnd;
            if (end <= DateTime.Now) end = end.AddDays(1);
            Apply(taskPath, end.ToUniversalTime(), untilReboot: false);
        }

        private static void Apply(string taskPath, DateTime? untilUtc, bool untilReboot)
        {
            if (string.IsNullOrWhiteSpace(taskPath)) return;

            try
            {
                var service = TaskServiceFactory();
                if (!service.TaskExists(taskPath))
                {
                    LogService.Warn($"Cannot snooze '{taskPath}': the task no longer exists.");
                    return;
                }

                // Remember the pre-snooze state so a task the user had already disabled is not
                // silently switched on when the snooze ends.
                bool wasEnabled = true;
                try
                {
                    var model = service.GetAllTasks(null, recursive: true)
                        .FirstOrDefault(t => string.Equals(t.Path, taskPath, StringComparison.OrdinalIgnoreCase));
                    if (model != null) wasEnabled = model.IsEnabled;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Could not read the current state of '{taskPath}' before snoozing: {ex.Message}");
                }

                var entry = new TaskSnoozeEntry
                {
                    TaskPath = taskPath,
                    UntilUtc = untilUtc,
                    UntilReboot = untilReboot,
                    BootStamp = untilReboot ? CurrentBootStamp() : "",
                    WasEnabled = wasEnabled
                };

                // Persist before touching the task, so an interrupted run still leaves a record.
                lock (_lock) { _entries[taskPath] = entry; }
                Save();

                service.DisableTask(taskPath);

                LogService.Info($"Task snoozed: {taskPath} ({DescribeForLog(entry)})");
                TaskSnoozeChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to snooze task '{taskPath}'", ex);
            }
        }

        /// <summary>Ends a task's snooze early and re-enables it.</summary>
        public static void Cancel(string taskPath)
        {
            if (string.IsNullOrWhiteSpace(taskPath)) return;

            TaskSnoozeEntry? entry;
            lock (_lock)
            {
                if (!_entries.TryGetValue(taskPath, out entry)) return;
                _entries.Remove(taskPath);
            }

            Save();
            RestoreTasks(new List<TaskSnoozeEntry> { entry });
            LogService.Info($"Task snooze cancelled: {taskPath}");
            TaskSnoozeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Re-enables tasks whose snooze is over, skipping any that were disabled beforehand.</summary>
        private static void RestoreTasks(List<TaskSnoozeEntry> entries)
        {
            if (entries.Count == 0) return;

            try
            {
                var service = TaskServiceFactory();
                foreach (var entry in entries)
                {
                    try
                    {
                        if (!entry.WasEnabled) continue;
                        if (!service.TaskExists(entry.TaskPath))
                        {
                            LogService.Warn($"Cannot restore '{entry.TaskPath}': the task no longer exists.");
                            continue;
                        }
                        service.EnableTask(entry.TaskPath);
                        LogService.Info($"Task snooze elapsed; re-enabled {entry.TaskPath}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"Failed to re-enable snoozed task '{entry.TaskPath}'", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to restore snoozed tasks", ex);
            }
        }

        // ── Persistence ─────────────────────────────────────────────────────────

        private static void Load()
        {
            try
            {
                if (!File.Exists(StatePath)) return;

                var data = JsonSerializer.Deserialize<List<TaskSnoozeEntry>>(File.ReadAllText(StatePath));
                if (data == null) return;

                lock (_lock)
                {
                    _entries.Clear();
                    foreach (var e in data)
                    {
                        if (!string.IsNullOrWhiteSpace(e.TaskPath)) _entries[e.TaskPath] = e;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"Could not read stored task snoozes: {ex.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                List<TaskSnoozeEntry> snapshot;
                lock (_lock) { snapshot = _entries.Values.ToList(); }

                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(snapshot));
            }
            catch (Exception ex)
            {
                LogService.Warn($"Could not persist task snoozes: {ex.Message}");
            }
        }

        /// <summary>
        /// Identifies the current boot, so an until-reboot snooze can tell whether the machine has
        /// restarted since. Mirrors the approach used by <see cref="SnoozeService"/>.
        /// </summary>
        private static string CurrentBootStamp()
        {
            var boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
            return new DateTime(boot.Year, boot.Month, boot.Day, boot.Hour, boot.Minute, 0, DateTimeKind.Utc)
                .ToString("O");
        }

        /// <summary>Test-only: drops all in-memory state so each test starts clean.</summary>
        internal static void ResetForTests()
        {
            lock (_lock) { _entries.Clear(); }
        }
    }
}
