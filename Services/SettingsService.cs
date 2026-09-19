using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
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
    }

    /// <summary>Instance-based, injectable settings store (registered as a DI singleton). No static state.</summary>
    public sealed class SettingsService : ISettingsService
    {
        private AppSettings _settings = new();
        private readonly string _settingsFolder;
        private readonly string _settingsPath;

        public SettingsService()
        {
            _settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler");
            _settingsPath = Path.Combine(_settingsFolder, "settings.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Fallback to defaults on error
                _settings = new AppSettings();
            }
        }

        private void Save()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                {
                    Directory.CreateDirectory(_settingsFolder);
                }
                string json = JsonSerializer.Serialize(_settings);
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
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

        public bool ShowHiddenTasks
        {
            get => _settings.ShowHiddenTasks;
            set { _settings.ShowHiddenTasks = value; Save(); }
        }

        public void ExportSettings(string targetPath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
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
                    _settings = imported;
                    Save();
                    Serilog.Log.Information("{Message}", $"Settings imported from {sourcePath}");
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
