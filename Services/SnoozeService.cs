using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FluentTaskScheduler.Services
{
    /// <summary>Why a run never happened — surfaced in the dashboard execution stream.</summary>
    public class SuppressedRunEntry
    {
        public DateTime TimeUtc { get; set; }
        public string TaskPath { get; set; } = "";
        public string TaskName { get; set; } = "";
        /// <summary>"Manual" (the user pressed Run) or "Scheduled" (a trigger fired during snooze).</summary>
        public string Origin { get; set; } = "Manual";
    }

    /// <summary>
    /// Global "pause everything" switch. While active, run requests issued through this app are
    /// refused and recorded, and — when <see cref="SettingsService.SnoozeSuspendsScheduledTasks"/>
    /// is on — enabled tasks are disabled through the Task Scheduler API and restored afterwards.
    /// </summary>
    public static class SnoozeService
    {
        private const int MaxSuppressedEntries = 300;

        private static readonly object _lock = new();
        private static readonly List<SuppressedRunEntry> _suppressed = new();
        private static Timer? _expiryTimer;
        private static bool _lastKnownActive;

        private static readonly string SuppressedLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "snooze_suppressed.json");

        /// <summary>Raised whenever the snooze turns on, turns off, or its end time changes.</summary>
        public static event EventHandler? SnoozeChanged;

        /// <summary>Factory for the task-service boundary, overridable in unit tests.</summary>
        public static Func<ITaskServiceWrapper> TaskServiceFactory { get; set; } = () => new TaskServiceWrapper();

        /// <summary>
        /// Test-only hook: the most recently started background suspend/restore operation, so tests
        /// can await deterministic completion instead of guessing with Thread.Sleep.
        /// </summary>
        internal static System.Threading.Tasks.Task? LastBackgroundOperation { get; private set; }

        static SnoozeService()
        {
            LoadSuppressed();
        }

        // ── State ───────────────────────────────────────────────────────────────

        /// <summary>True while global snooze is in effect. Automatically false once the window elapsed.</summary>
        public static bool IsActive
        {
            get
            {
                if (!SettingsService.IsSnoozed) return false;

                if (SettingsService.SnoozeUntilReboot)
                {
                    // A different boot stamp means the machine restarted, which ends this snooze.
                    return string.Equals(SettingsService.SnoozeBootStamp, CurrentBootStamp(), StringComparison.Ordinal);
                }

                var until = SettingsService.SnoozeUntilUtc;
                return until.HasValue && until.Value > DateTime.UtcNow;
            }
        }

        /// <summary>Local end time of the current snooze, or null when it lasts until reboot / is off.</summary>
        public static DateTime? SnoozeEndsAtLocal
        {
            get
            {
                if (!IsActive || SettingsService.SnoozeUntilReboot) return null;
                return SettingsService.SnoozeUntilUtc?.ToLocalTime();
            }
        }

        public static bool IsUntilReboot => IsActive && SettingsService.SnoozeUntilReboot;

        /// <summary>Human-readable banner/tooltip text, e.g. "Global Snooze Active (Ends at 14:30)".</summary>
        public static string StatusText
        {
            get
            {
                if (!IsActive) return "";

                if (IsUntilReboot)
                {
                    return LocalizationService.GetString(
                        "Snooze.Status.UntilReboot", "Global Snooze Active (until next restart)");
                }

                var ends = SnoozeEndsAtLocal;
                if (ends == null) return LocalizationService.GetString("Snooze.Status.Generic", "Global Snooze Active");

                return string.Format(
                    LocalizationService.GetString("Snooze.Status.EndsAt", "Global Snooze Active (Ends at {0})"),
                    ends.Value.ToString("HH:mm"));
            }
        }

        public static IReadOnlyList<SuppressedRunEntry> SuppressedRuns
        {
            get { lock (_lock) return _suppressed.ToList(); }
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>
        /// Called once at startup: drops an expired snooze (restoring any suspended tasks) and
        /// starts the timer that ends timed snoozes while the app is running.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (SettingsService.IsSnoozed && !IsActive)
                {
                    LogService.Info("Stored global snooze had already expired at startup; clearing it.");
                    ClearSnoozeState();
                }
                else if (!SettingsService.IsSnoozed && SettingsService.SnoozeDisabledTaskPaths.Count > 0)
                {
                    // A previous restore was interrupted (crash/kill) before it finished clearing the
                    // disabled-task list — finish restoring whatever is left, on a background thread.
                    LogService.Warn("Found a leftover snooze-disabled task list from a previous run; resuming restore.");
                    var leftover = new List<string>(SettingsService.SnoozeDisabledTaskPaths);
                    LastBackgroundOperation = System.Threading.Tasks.Task.Run(() =>
                    {
                        RestoreSuspendedTasks(leftover);
                        SettingsService.SnoozeDisabledTaskPaths = new List<string>();
                    });
                }

                _lastKnownActive = IsActive;
                _expiryTimer?.Dispose();
                _expiryTimer = new Timer(CheckExpiry, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

                if (_lastKnownActive) LogService.Info($"Global snooze restored: {StatusText}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to initialize SnoozeService", ex);
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
                bool active = IsActive;
                if (active == _lastKnownActive) return;

                _lastKnownActive = active;
                if (!active)
                {
                    LogService.Info("Global snooze window elapsed; resuming normal operation.");
                    ClearSnoozeState();
                    NotificationService.ShowSnoozeEnded();
                }
                SnoozeChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogService.Error("Snooze expiry check failed", ex);
            }
        }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Snoozes everything for a fixed duration.</summary>
        public static void Snooze(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                LogService.Warn($"Ignoring snooze request with non-positive duration '{duration}'.");
                return;
            }
            Apply(DateTime.UtcNow.Add(duration), untilReboot: false);
        }

        /// <summary>Snoozes everything until the machine restarts.</summary>
        public static void SnoozeUntilReboot() => Apply(null, untilReboot: true);

        /// <summary>Snoozes everything until a specific local time (rolls to tomorrow if already past).</summary>
        public static void SnoozeUntilLocalTime(DateTime localEnd)
        {
            var end = localEnd;
            if (end <= DateTime.Now) end = end.AddDays(1);
            Apply(end.ToUniversalTime(), untilReboot: false);
        }

        private static void Apply(DateTime? untilUtc, bool untilReboot)
        {
            try
            {
                bool suspend = SettingsService.SnoozeSuspendsScheduledTasks;
                var candidatePaths = suspend ? GetSuspendCandidates() : new List<string>(SettingsService.SnoozeDisabledTaskPaths);

                // Persist the full candidate list *before* disabling anything, so a crash partway
                // through the sweep still leaves a record Initialize() can use to finish restoring.
                SettingsService.SaveSnoozeState(
                    isSnoozed: true,
                    untilUtc: untilUtc,
                    untilReboot: untilReboot,
                    bootStamp: untilReboot ? CurrentBootStamp() : "",
                    disabledPaths: candidatePaths);

                _lastKnownActive = true;
                LogService.Info($"Global snooze activated: {StatusText}");
                NotificationService.ShowSnoozeStarted(StatusText);
                SnoozeChanged?.Invoke(null, EventArgs.Empty);

                // The actual disabling of (potentially hundreds of) tasks happens off the calling
                // thread (UI thread for the dialog, message-pump thread for the tray menu) so it
                // never freezes the app.
                if (suspend && candidatePaths.Count > 0)
                {
                    LastBackgroundOperation = System.Threading.Tasks.Task.Run(() => DisableCandidates(candidatePaths));
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to activate global snooze", ex);
                throw;
            }
        }

        /// <summary>Ends the snooze immediately and restores anything that was suspended.</summary>
        public static void Cancel()
        {
            try
            {
                if (!SettingsService.IsSnoozed) return;
                ClearSnoozeState();
                _lastKnownActive = false;
                LogService.Info("Global snooze cancelled by user.");
                SnoozeChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to cancel global snooze", ex);
                throw;
            }
        }

        private static void ClearSnoozeState()
        {
            var paths = new List<string>(SettingsService.SnoozeDisabledTaskPaths);

            // Clear the "snoozed" flag right away so the UI reflects it immediately, but keep the
            // disabled-path list on disk until the restore actually finishes — if the app is killed
            // mid-restore, Initialize() on the next launch will pick up where this left off.
            SettingsService.SaveSnoozeState(false, null, false, "", paths);

            if (paths.Count > 0)
            {
                LastBackgroundOperation = System.Threading.Tasks.Task.Run(() =>
                {
                    RestoreSuspendedTasks(paths);
                    SettingsService.SnoozeDisabledTaskPaths = new List<string>();
                });
            }
        }

        // ── Optional hard suspension of scheduled triggers ───────────────────────

        /// <summary>Prefix excluded from suspension unless the user explicitly opts in.</summary>
        private const string MicrosoftTaskPrefix = @"\Microsoft\";

        /// <summary>
        /// Returns the paths of every currently-enabled task that snooze would disable, honoring
        /// the \Microsoft\ exclusion. This is a fast, read-only pass — no tasks are touched yet.
        /// </summary>
        internal static List<string> GetSuspendCandidates()
        {
            bool includeMicrosoft = SettingsService.SnoozeIncludeMicrosoftTasks;
            var service = TaskServiceFactory();

            var candidates = new List<string>();
            foreach (var task in service.GetAllTasks(recursive: true))
            {
                if (!task.IsEnabled || task.IsReadOnlyFallback || string.IsNullOrEmpty(task.Path)) continue;
                if (!includeMicrosoft && task.Path.StartsWith(MicrosoftTaskPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                candidates.Add(task.Path);
            }
            return candidates;
        }

        /// <summary>Actually disables the given tasks. Runs on a background thread — see <see cref="Apply"/>.</summary>
        internal static void DisableCandidates(List<string> paths)
        {
            var service = TaskServiceFactory();
            int changed = 0;
            foreach (var path in paths)
            {
                try
                {
                    service.SetTaskEnabled(path, false);
                    changed++;
                }
                catch (Exception ex)
                {
                    // Protected system tasks cannot be disabled — that is expected, not fatal.
                    LogService.Warn($"Snooze could not suspend task '{path}': {ex.Message}");
                }
            }
            LogService.Info($"Global snooze suspended {changed}/{paths.Count} scheduled task(s).");
        }

        internal static void RestoreSuspendedTasks(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;

            var service = TaskServiceFactory();
            int restored = 0;
            foreach (var path in paths)
            {
                try
                {
                    service.SetTaskEnabled(path, true);
                    restored++;
                }
                catch (Exception ex)
                {
                    LogService.Error($"Snooze could not re-enable task '{path}' after the snooze ended.", ex);
                }
            }
            LogService.Info($"Global snooze restored {restored}/{paths.Count} suspended task(s).");
        }

        // ── Suppression log ─────────────────────────────────────────────────────

        /// <summary>Records (and toasts) a run that was refused because snooze is active.</summary>
        public static void RecordSuppressedRun(string taskPath, string origin)
        {
            var entry = new SuppressedRunEntry
            {
                TimeUtc = DateTime.UtcNow,
                TaskPath = taskPath,
                TaskName = string.IsNullOrEmpty(taskPath) ? "" : Path.GetFileName(taskPath),
                Origin = origin
            };

            lock (_lock)
            {
                _suppressed.Insert(0, entry);
                if (_suppressed.Count > MaxSuppressedEntries)
                    _suppressed.RemoveRange(MaxSuppressedEntries, _suppressed.Count - MaxSuppressedEntries);
            }

            LogService.Info($"Run of '{taskPath}' suppressed by global snooze (origin: {origin}).");
            SaveSuppressed();
        }

        public static void ClearSuppressedRuns()
        {
            lock (_lock) _suppressed.Clear();
            SaveSuppressed();
        }

        private static void LoadSuppressed()
        {
            try
            {
                if (!File.Exists(SuppressedLogPath)) return;
                var data = JsonSerializer.Deserialize<List<SuppressedRunEntry>>(File.ReadAllText(SuppressedLogPath));
                if (data == null) return;
                lock (_lock)
                {
                    _suppressed.Clear();
                    _suppressed.AddRange(data.Take(MaxSuppressedEntries));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Could not read the snooze suppression log at '{SuppressedLogPath}'.", ex);
            }
        }

        private static void SaveSuppressed()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SuppressedLogPath)!);
                List<SuppressedRunEntry> snapshot;
                lock (_lock) snapshot = _suppressed.ToList();
                File.WriteAllText(SuppressedLogPath, JsonSerializer.Serialize(snapshot));
            }
            catch (Exception ex)
            {
                LogService.Error($"Could not write the snooze suppression log at '{SuppressedLogPath}'.", ex);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Approximate boot time, truncated to the minute so small clock drift between reads
        /// does not look like a restart.
        /// </summary>
        private static string CurrentBootStamp()
        {
            var boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
            return new DateTime(boot.Year, boot.Month, boot.Day, boot.Hour, boot.Minute, 0, DateTimeKind.Utc)
                .ToString("O");
        }
    }
}
