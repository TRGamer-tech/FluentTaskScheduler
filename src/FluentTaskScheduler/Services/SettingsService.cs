using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Services
{
    public class AppSettings
    {
        public string Theme { get; set; } = "Default";
        public bool IsOledMode { get; set; } = false;
        public bool IsMicaEnabled { get; set; } = true;
        public string Language { get; set; } = "en-US";
        public bool ConfirmDelete { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool EnableTrayIcon { get; set; } = true;
        public bool MinimizeToTray { get; set; } = false;
        public bool EnableLogging { get; set; } = true;
        public bool SeparateLogFiles { get; set; } = true;
        public bool EnableExecutionHistoryLog { get; set; } = false;
        public bool RunOnStartup { get; set; } = false;
        public bool SmoothScrolling { get; set; } = true;
        public int WindowWidth { get; set; } = 1200;
        public int WindowHeight { get; set; } = 800;
        public string LastFolderPath { get; set; } = "\\";
        public string LastSeenVersion { get; set; } = "";
        public bool HasCompletedOnboarding { get; set; } = false;
        public bool EnableUpcomingReminders { get; set; } = true;
        public int ReminderLeadMinutes { get; set; } = 5;
        public bool ShowHiddenTasks { get; set; } = true;
        public List<string> SavedCategories { get; set; } = new() { "Work", "Personal", "Maintenance", "System" };
        public List<string> SavedTags { get; set; } = new() { "urgent", "sync", "database", "cleanup" };

        // ── Global snooze ────────────────────────────────────────────────────────
        public bool IsSnoozed { get; set; } = false;
        /// <summary>Absolute UTC end of the snooze window. Null when snoozing until reboot.</summary>
        public DateTime? SnoozeUntilUtc { get; set; } = null;
        /// <summary>When true the snooze lasts until the machine is restarted instead of a fixed time.</summary>
        public bool SnoozeUntilReboot { get; set; } = false;
        /// <summary>Boot timestamp captured when an "until reboot" snooze started, used to detect a restart.</summary>
        public string SnoozeBootStamp { get; set; } = "";
        /// <summary>When true, starting a snooze also disables scheduled triggers via the Task Scheduler API.</summary>
        public bool SnoozeSuspendsScheduledTasks { get; set; } = false;
        /// <summary>
        /// When true, trigger suspension also touches tasks under \Microsoft\ (Defender, Windows
        /// Update, maintenance, etc). Off by default — disabling those can break OS functionality.
        /// </summary>
        public bool SnoozeIncludeMicrosoftTasks { get; set; } = false;
        /// <summary>Tasks this app disabled when the current snooze started; re-enabled when it ends.</summary>
        public List<string> SnoozeDisabledTaskPaths { get; set; } = new();

        // ── Task chaining ────────────────────────────────────────────────────────
        /// <summary>Master switch for the pipeline watcher that starts downstream tasks on completion.</summary>
        public bool EnableTaskPipelines { get; set; } = true;
    }

    /// <summary>
    /// Instance-based, injectable settings store (registered as a DI singleton). No static state:
    /// tests and other callers can create isolated instances pointed at any folder.
    /// </summary>
    public sealed class SettingsService : ISettingsService, IDisposable
    {
        private AppSettings _settings = new();
        private readonly string _settingsFolder;
        private readonly string _settingsPath;

        // Guards all reads/writes of _settings and the on-disk file: Save() is called from the UI
        // thread, the snooze timer thread, and pipeline/reminder threads, so unguarded concurrent
        // File.WriteAllText calls could interleave and corrupt settings.json.
        private readonly object _lock = new();
        private Timer? _debounceTimer;
        private const int DebounceMilliseconds = 500;

        public SettingsService() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler"))
        {
        }

        public SettingsService(string storageFolder)
        {
            _settingsFolder = storageFolder;
            _settingsPath = Path.Combine(_settingsFolder, "settings.json");
            Load();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_settingsPath))
                    {
                        string json = File.ReadAllText(_settingsPath);
                        _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to defaults on error
                    _settings = new AppSettings();
                    Serilog.Log.Error(ex, "Failed to load settings from '{Path}', reverting to defaults.", _settingsPath);
                }
            }
        }

        /// <summary>
        /// Schedules a write to disk after a short debounce window, coalescing bursts of property
        /// changes (e.g. continuous window-resize events) into a single write.
        /// </summary>
        private void Save()
        {
            lock (_lock)
            {
                _debounceTimer ??= new Timer(_ => SaveImmediate(), null, Timeout.Infinite, Timeout.Infinite);
                _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }

        /// <summary>Forces any pending debounced save to happen immediately (e.g. before app exit).</summary>
        public void Flush()
        {
            lock (_lock)
            {
                _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            SaveImmediate();
        }

        private void SaveImmediate()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(_settingsFolder))
                    {
                        Directory.CreateDirectory(_settingsFolder);
                    }
                    string json = JsonSerializer.Serialize(_settings);

                    // Write-then-replace so a crash or concurrent read never observes a truncated file.
                    string tempPath = _settingsPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(_settingsPath))
                    {
                        File.Replace(tempPath, _settingsPath, null);
                    }
                    else
                    {
                        File.Move(tempPath, _settingsPath);
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to save settings to '{Path}'.", _settingsPath);
                }
            }
        }

        public ElementTheme Theme
        {
            get => Enum.TryParse<ElementTheme>(_settings.Theme, out var t) ? t : ElementTheme.Default;
            set
            {
                _settings.Theme = value.ToString();
                Save();
            }
        }

        public bool IsOledMode
        {
            get => _settings.IsOledMode;
            set
            {
                _settings.IsOledMode = value;
                Save();
            }
        }

        public bool IsMicaEnabled
        {
            get => _settings.IsMicaEnabled;
            set
            {
                _settings.IsMicaEnabled = value;
                Save();
            }
        }

        public string Language
        {
            get => _settings.Language;
            set
            {
                _settings.Language = value;
                Save();
            }
        }

        public bool ConfirmDelete
        {
            get => _settings.ConfirmDelete;
            set
            {
                _settings.ConfirmDelete = value;
                Save();
            }
        }

        public bool ShowNotifications
        {
            get => _settings.ShowNotifications;
            set
            {
                _settings.ShowNotifications = value;
                Save();
            }
        }

        public bool EnableTrayIcon
        {
            get => _settings.EnableTrayIcon;
            set
            {
                _settings.EnableTrayIcon = value;
                Save();
            }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set
            {
                _settings.MinimizeToTray = value;
                Save();
            }
        }

        public bool EnableLogging
        {
            get => _settings.EnableLogging;
            set
            {
                _settings.EnableLogging = value;
                Save();
            }
        }
        
        public bool SeparateLogFiles
        {
            get => _settings.SeparateLogFiles;
            set
            {
                _settings.SeparateLogFiles = value;
                Save();
            }
        }

        public bool EnableExecutionHistoryLog
        {
            get => _settings.EnableExecutionHistoryLog;
            set
            {
                _settings.EnableExecutionHistoryLog = value;
                Save();
            }
        }

        public bool RunOnStartup
        {
            get => _settings.RunOnStartup;
            set
            {
                _settings.RunOnStartup = value;
                Save();
            }
        }

        public bool SmoothScrolling
        {
            get => _settings.SmoothScrolling;
            set
            {
                _settings.SmoothScrolling = value;
                Save();
            }
        }

        public int WindowWidth
        {
            get => _settings.WindowWidth;
            set { _settings.WindowWidth = value; Save(); }
        }

        public int WindowHeight
        {
            get => _settings.WindowHeight;
            set { _settings.WindowHeight = value; Save(); }
        }

        /// <summary>Sets width and height together as a single (debounced) settings write.</summary>
        public void SetWindowSize(int width, int height)
        {
            lock (_lock)
            {
                _settings.WindowWidth = width;
                _settings.WindowHeight = height;
            }
            Save();
        }

        public string LastFolderPath
        {
            get => _settings.LastFolderPath;
            set { _settings.LastFolderPath = value; Save(); }
        }

        public string LastSeenVersion
        {
            get => _settings.LastSeenVersion;
            set { _settings.LastSeenVersion = value; Save(); }
        }

        public bool HasCompletedOnboarding
        {
            get => _settings.HasCompletedOnboarding;
            set { _settings.HasCompletedOnboarding = value; Save(); }
        }

        public bool EnableUpcomingReminders
        {
            get => _settings.EnableUpcomingReminders;
            set { _settings.EnableUpcomingReminders = value; Save(); }
        }

        public int ReminderLeadMinutes
        {
            get => _settings.ReminderLeadMinutes;
            set { _settings.ReminderLeadMinutes = value; Save(); }
        }

        public List<string> SavedCategories
        {
            get => _settings.SavedCategories;
            set { _settings.SavedCategories = value; Save(); }
        }

        public List<string> SavedTags
        {
            get => _settings.SavedTags;
            set { _settings.SavedTags = value; Save(); }
        }

        // The getters above return the live internal list, so callers used to mutate it directly
        // and then reassign the property to itself just to trigger a save
        // (`SavedCategories.Add(x); SavedCategories = SavedCategories;`). These do the same thing
        // properly — mutate under the lock and save once (see 4.5).
        public void AddSavedCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            lock (_lock)
            {
                if (_settings.SavedCategories.Contains(category)) return;
                _settings.SavedCategories.Add(category);
            }
            Save();
        }

        public void RemoveSavedCategory(string category)
        {
            lock (_lock) { _settings.SavedCategories.Remove(category); }
            Save();
        }

        public void AddSavedTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            lock (_lock)
            {
                if (_settings.SavedTags.Contains(tag)) return;
                _settings.SavedTags.Add(tag);
            }
            Save();
        }

        public void RemoveSavedTag(string tag)
        {
            lock (_lock) { _settings.SavedTags.Remove(tag); }
            Save();
        }

        public bool ShowHiddenTasks
        {
            get => _settings.ShowHiddenTasks;
            set { _settings.ShowHiddenTasks = value; Save(); }
        }

        // ── Global snooze ────────────────────────────────────────────────────────

        public bool IsSnoozed
        {
            get => _settings.IsSnoozed;
            set { _settings.IsSnoozed = value; Save(); }
        }

        public DateTime? SnoozeUntilUtc
        {
            get => _settings.SnoozeUntilUtc;
            set { _settings.SnoozeUntilUtc = value; Save(); }
        }

        public bool SnoozeUntilReboot
        {
            get => _settings.SnoozeUntilReboot;
            set { _settings.SnoozeUntilReboot = value; Save(); }
        }

        public string SnoozeBootStamp
        {
            get => _settings.SnoozeBootStamp;
            set { _settings.SnoozeBootStamp = value; Save(); }
        }

        public bool SnoozeSuspendsScheduledTasks
        {
            get => _settings.SnoozeSuspendsScheduledTasks;
            set { _settings.SnoozeSuspendsScheduledTasks = value; Save(); }
        }

        public bool SnoozeIncludeMicrosoftTasks
        {
            get => _settings.SnoozeIncludeMicrosoftTasks;
            set { _settings.SnoozeIncludeMicrosoftTasks = value; Save(); }
        }

        public List<string> SnoozeDisabledTaskPaths
        {
            get => _settings.SnoozeDisabledTaskPaths;
            set { _settings.SnoozeDisabledTaskPaths = value ?? new List<string>(); Save(); }
        }

        public bool EnableTaskPipelines
        {
            get => _settings.EnableTaskPipelines;
            set { _settings.EnableTaskPipelines = value; Save(); }
        }

        /// <summary>
        /// Writes several snooze fields in one go so a single Save() hits disk instead of one per property.
        /// </summary>
        /// <summary>
        /// Writes several snooze fields in one go and flushes immediately (not debounced) so that a
        /// crash mid-sweep in <see cref="SnoozeService"/> can still recover the disabled-task list.
        /// </summary>
        public void SaveSnoozeState(bool isSnoozed, DateTime? untilUtc, bool untilReboot, string bootStamp, List<string> disabledPaths)
        {
            lock (_lock)
            {
                _settings.IsSnoozed = isSnoozed;
                _settings.SnoozeUntilUtc = untilUtc;
                _settings.SnoozeUntilReboot = untilReboot;
                _settings.SnoozeBootStamp = bootStamp ?? "";
                _settings.SnoozeDisabledTaskPaths = disabledPaths ?? new List<string>();
            }
            SaveImmediate();
        }

        /// <summary>
        /// Resets fields that describe this machine's current runtime state rather than a durable
        /// user preference: window geometry and the live global-snooze window. Exporting these lets
        /// importing the file on another machine (or re-importing it later) resurrect a stale snooze
        /// or an unreasonable window size; importing them lets a hand-edited/foreign file inject
        /// bogus runtime state (see 4.5).
        /// </summary>
        private void ClearRuntimeState(AppSettings s)
        {
            var defaults = new AppSettings();
            s.WindowWidth = defaults.WindowWidth;
            s.WindowHeight = defaults.WindowHeight;
            s.IsSnoozed = false;
            s.SnoozeUntilUtc = null;
            s.SnoozeUntilReboot = false;
            s.SnoozeBootStamp = "";
            s.SnoozeDisabledTaskPaths = new List<string>();
        }

        public void ExportSettings(string targetPath)
        {
            try
            {
                string json;
                lock (_lock)
                {
                    var exportCopy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_settings)) ?? new AppSettings();
                    ClearRuntimeState(exportCopy);
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    json = JsonSerializer.Serialize(exportCopy, options);
                }
                File.WriteAllText(targetPath, json);
                Serilog.Log.Information("{Message}", $"Settings exported to {targetPath}");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to export settings");
                throw;
            }
        }

        public void ImportSettings(string sourcePath)
        {
            try
            {
                string json = File.ReadAllText(sourcePath);
                var imported = JsonSerializer.Deserialize<AppSettings>(json);
                if (imported != null)
                {
                    ClearRuntimeState(imported);
                    lock (_lock)
                    {
                        _settings = imported;
                    }
                    SaveImmediate();
                    Serilog.Log.Information("{Message}", $"Settings imported from {sourcePath}");
                }
                else
                {
                    throw new InvalidDataException($"'{sourcePath}' did not contain valid settings data.");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to import settings");
                throw;
            }
        }
    }
}
