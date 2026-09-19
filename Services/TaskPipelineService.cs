using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Watches the Task Scheduler operational log and starts downstream tasks when an upstream
    /// task finishes ("task chaining" / execution pipelines).
    ///
    /// Uses a push-based <see cref="EventLogWatcher"/> subscription rather than polling, so the
    /// service costs nothing while idle. The watcher and every <see cref="EventRecord"/> it hands
    /// us are disposed explicitly — leaking either one leaks a kernel handle per event.
    ///
    /// Only events that carry an explicit action result are treated as completions:
    ///   • 201 — action finished, <c>ResultCode</c> is the process exit code
    ///   • 103 / 203 — Task Scheduler could not start the task or launch the action (hard failure)
    /// Event 102 ("task finished") is deliberately ignored because it carries no exit code and
    /// would double-fire alongside 201.
    /// </summary>
    public static class TaskPipelineService
    {
        private const int MaxChainStartsPerMinute = 20;
        private const int MaxRememberedRecords = 500;
        private static readonly TimeSpan AncestryLifetime = TimeSpan.FromHours(1);
        private static readonly TimeSpan PipelineCacheLifetime = TimeSpan.FromSeconds(60);

        private static readonly object _lock = new();
        private static EventLogWatcher? _watcher;
        private static bool _isRunning;

        // taskPath (lower-case) → pipeline configuration
        private static Dictionary<string, TaskPipeline> _pipelineCache = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _taskNameCache = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _pipelineCacheStamp = DateTime.MinValue;

        // taskPath → the set of tasks that transitively caused it to start (cycle guard)
        private static readonly Dictionary<string, (HashSet<string> Ancestors, DateTime Expires)> _ancestry =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Queue<long> _processedRecordIds = new();
        private static readonly HashSet<long> _processedRecordIdSet = new();
        private static readonly Queue<DateTime> _recentChainStarts = new();

        /// <summary>Raised after a downstream task was started, so the UI can refresh.</summary>
        public static event EventHandler<PipelineTriggeredEventArgs>? PipelineTriggered;

        public static bool IsRunning { get { lock (_lock) return _isRunning; } }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        public static void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;

                if (!SettingsService.EnableTaskPipelines)
                {
                    LogService.Info("Task pipelines are disabled in settings; watcher not started.");
                    return;
                }

                try
                {
                    var query = new EventLogQuery(
                        "Microsoft-Windows-TaskScheduler/Operational",
                        PathType.LogName,
                        "*[System[(EventID=201 or EventID=103 or EventID=203)]]");

                    _watcher = new EventLogWatcher(query);
                    _watcher.EventRecordWritten += OnEventRecordWritten;
                    _watcher.Enabled = true;
                    _isRunning = true;
                    LogService.Info("TaskPipelineService started (event-driven, no polling).");
                }
                catch (EventLogNotFoundException ex)
                {
                    DisposeWatcher();
                    LogService.Error("Task pipelines unavailable: the Task Scheduler operational log was not found.", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    DisposeWatcher();
                    LogService.Error("Task pipelines unavailable: access denied subscribing to the Task Scheduler operational log.", ex);
                }
                catch (Exception ex)
                {
                    DisposeWatcher();
                    LogService.Error("Failed to start TaskPipelineService.", ex);
                }
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                DisposeWatcher();
                _isRunning = false;
                LogService.Info("TaskPipelineService stopped.");
            }
        }

        /// <summary>Applies a settings change without requiring an app restart.</summary>
        public static void ApplyEnabledSetting()
        {
            if (SettingsService.EnableTaskPipelines) Start();
            else Stop();
        }

        /// <summary>Forces the next event to re-read pipeline configuration (call after saving a task).</summary>
        public static void InvalidatePipelineCache()
        {
            lock (_lock) _pipelineCacheStamp = DateTime.MinValue;
        }

        private static void DisposeWatcher()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.Enabled = false;
                _watcher.EventRecordWritten -= OnEventRecordWritten;
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Error("Error while disposing the pipeline event watcher.", ex);
            }
            finally
            {
                _watcher = null;
            }
        }

        // ── Event handling ──────────────────────────────────────────────────────

        private static void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
        {
            // A null record means the subscription dropped events; EventException carries the reason.
            if (e.EventRecord == null)
            {
                LogService.Warn($"Pipeline watcher dropped events: {e.EventException?.Message ?? "unknown reason"}");
                return;
            }

            // Dispose is mandatory: each EventRecord owns an unmanaged event handle.
            using var record = e.EventRecord;

            try
            {
                if (!TryClaimRecord(record.RecordId)) return;

                var data = TaskServiceWrapper.ReadEventData(record);
                if (!data.TryGetValue("TaskName", out var taskPath) || string.IsNullOrWhiteSpace(taskPath)) return;

                bool succeeded;
                if (record.Id == 201)
                {
                    // HRESULT-style codes overflow int, so parse wide.
                    if (!data.TryGetValue("ResultCode", out var rc) || !long.TryParse(rc, out long exitCode))
                    {
                        LogService.Warn($"Pipeline: event 201 for '{taskPath}' had no readable ResultCode; ignoring.");
                        return;
                    }
                    succeeded = exitCode == 0;
                }
                else
                {
                    // 103 / 203 — the action never ran successfully.
                    succeeded = false;
                }

                HandleCompletion(taskPath, succeeded);
            }
            catch (Exception ex)
            {
                LogService.Error($"Pipeline watcher failed to process event {record.Id}.", ex);
            }
        }

        private static void HandleCompletion(string sourcePath, bool succeeded)
        {
            var pipeline = GetPipelineFor(sourcePath);
            if (pipeline == null || !pipeline.IsActive) return;

            var targets = succeeded ? pipeline.OnSuccessTasks : pipeline.OnFailureTasks;
            if (targets == null || targets.Count == 0) return;

            if (SnoozeService.IsActive)
            {
                foreach (var target in targets)
                    SnoozeService.RecordSuppressedRun(target, "Pipeline");

                LogService.Info($"Pipeline from '{sourcePath}' suppressed: global snooze is active.");
                return;
            }

            var service = new TaskServiceWrapper();
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target)) continue;

                if (!TryReserveChainStart())
                {
                    LogService.Warn($"Pipeline rate limit reached ({MaxChainStartsPerMinute}/min); skipping '{target}'.");
                    continue;
                }

                if (WouldLoop(sourcePath, target))
                {
                    LogService.Warn(
                        $"Pipeline loop prevented: '{target}' is already an ancestor of '{sourcePath}' in this chain.");
                    continue;
                }

                try
                {
                    RecordAncestry(sourcePath, target);
                    service.RunTask(target, "Pipeline");
                    LogService.Info(
                        $"Pipeline started '{target}' because '{sourcePath}' {(succeeded ? "succeeded" : "failed")}.");
                    NotificationService.ShowPipelineTriggered(
                        System.IO.Path.GetFileName(sourcePath), System.IO.Path.GetFileName(target), succeeded);
                    PipelineTriggered?.Invoke(null, new PipelineTriggeredEventArgs(sourcePath, target, succeeded));
                }
                catch (TaskSnoozedException)
                {
                    // Snooze turned on between the check above and the call — already recorded there.
                }
                catch (Exception ex)
                {
                    LogService.Error($"Pipeline could not start downstream task '{target}' (source '{sourcePath}').", ex);
                }
            }
        }

        // ── Pipeline configuration cache ────────────────────────────────────────

        private static TaskPipeline? GetPipelineFor(string taskPath)
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _pipelineCacheStamp <= PipelineCacheLifetime)
                {
                    return _pipelineCache.TryGetValue(taskPath, out var cached) ? cached : null;
                }
            }

            RefreshPipelineCache();

            lock (_lock)
            {
                return _pipelineCache.TryGetValue(taskPath, out var pipeline) ? pipeline : null;
            }
        }

        private static void RefreshPipelineCache()
        {
            try
            {
                var service = new TaskServiceWrapper();
                var pipelines = new Dictionary<string, TaskPipeline>(StringComparer.OrdinalIgnoreCase);
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var task in service.GetAllTasks(recursive: true))
                {
                    names[task.Path] = task.Name;
                    if (task.Pipeline != null && task.Pipeline.IsActive)
                        pipelines[task.Path] = task.Pipeline;
                }

                lock (_lock)
                {
                    _pipelineCache = pipelines;
                    _taskNameCache = names;
                    _pipelineCacheStamp = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to refresh the task pipeline cache.", ex);
                lock (_lock)
                {
                    // Back off for one cache lifetime so a persistent failure doesn't hammer the API.
                    _pipelineCacheStamp = DateTime.UtcNow;
                }
            }
        }

        /// <summary>Friendly name for a task path, falling back to the file name portion.</summary>
        public static string ResolveTaskName(string taskPath)
        {
            lock (_lock)
            {
                if (_taskNameCache.TryGetValue(taskPath, out var name)) return name;
            }
            return System.IO.Path.GetFileName(taskPath);
        }

        // ── Guards ──────────────────────────────────────────────────────────────

        /// <summary>Returns false when this event record was already handled (subscription replay).</summary>
        private static bool TryClaimRecord(long? recordId)
        {
            if (recordId == null) return true;

            lock (_lock)
            {
                if (!_processedRecordIdSet.Add(recordId.Value)) return false;
                _processedRecordIds.Enqueue(recordId.Value);
                while (_processedRecordIds.Count > MaxRememberedRecords)
                    _processedRecordIdSet.Remove(_processedRecordIds.Dequeue());
                return true;
            }
        }

        private static bool TryReserveChainStart()
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                while (_recentChainStarts.Count > 0 && _recentChainStarts.Peek() < cutoff)
                    _recentChainStarts.Dequeue();

                if (_recentChainStarts.Count >= MaxChainStartsPerMinute) return false;
                _recentChainStarts.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        /// <summary>
        /// True when starting <paramref name="target"/> would revisit a task that already appears
        /// earlier in the chain that led to <paramref name="source"/> (direct or indirect cycle).
        /// </summary>
        private static bool WouldLoop(string source, string target)
        {
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return true;

            lock (_lock)
            {
                PruneAncestry();
                return _ancestry.TryGetValue(source, out var entry) &&
                       entry.Ancestors.Contains(target);
            }
        }

        private static void RecordAncestry(string source, string target)
        {
            lock (_lock)
            {
                var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source };
                if (_ancestry.TryGetValue(source, out var sourceEntry))
                    ancestors.UnionWith(sourceEntry.Ancestors);

                _ancestry[target] = (ancestors, DateTime.UtcNow.Add(AncestryLifetime));
            }
        }

        /// <summary>Caller must hold <see cref="_lock"/>.</summary>
        private static void PruneAncestry()
        {
            if (_ancestry.Count == 0) return;
            var now = DateTime.UtcNow;
            var stale = _ancestry.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList();
            foreach (var key in stale) _ancestry.Remove(key);
        }
    }

    public class PipelineTriggeredEventArgs : EventArgs
    {
        public string SourceTaskPath { get; }
        public string TargetTaskPath { get; }
        public bool WasSuccess { get; }

        public PipelineTriggeredEventArgs(string source, string target, bool wasSuccess)
        {
            SourceTaskPath = source;
            TargetTaskPath = target;
            WasSuccess = wasSuccess;
        }
    }
}
