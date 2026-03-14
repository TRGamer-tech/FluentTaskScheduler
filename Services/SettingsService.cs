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
        public bool RunOnStartup { get; set; } = false;
        public bool SmoothScrolling { get; set; } = false;
        public int WindowWidth { get; set; } = 1200;
        public int WindowHeight { get; set; } = 800;
        public string LastFolderPath { get; set; } = "\\";
        public string LastSeenVersion { get; set; } = "";
        public bool HasCompletedOnboarding { get; set; } = false;
        public bool EnableUpcomingReminders { get; set; } = true;
        public int ReminderLeadMinutes { get; set; } = 5;
        public List<string> SavedCategories { get; set; } = new() { "Work", "Personal", "Maintenance", "System" };
        public List<string> SavedTags { get; set; } = new() { "urgent", "sync", "database", "cleanup" };
    }

    public static class SettingsService
    {
        private static AppSettings _settings = new();
        private static string SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler");
        private static string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        static SettingsService()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch 
            {
                // Fallback to defaults on error
                _settings = new AppSettings();
            }
        }

        private static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }
                string json = JsonSerializer.Serialize(_settings);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static ElementTheme Theme
        {
            get => Enum.TryParse<ElementTheme>(_settings.Theme, out var t) ? t : ElementTheme.Default;
            set
            {
                _settings.Theme = value.ToString();
                Save();
            }
        }

        public static bool IsOledMode
        {
            get => _settings.IsOledMode;
            set
            {
                _settings.IsOledMode = value;
                Save();
            }
        }

        public static bool IsMicaEnabled
        {
            get => _settings.IsMicaEnabled;
            set
            {
                _settings.IsMicaEnabled = value;
                Save();
            }
        }

        public static string Language
        {
            get => _settings.Language;
            set
            {
                _settings.Language = value;
                Save();
            }
        }

        public static bool ConfirmDelete
        {
            get => _settings.ConfirmDelete;
            set
            {
                _settings.ConfirmDelete = value;
                Save();
            }
        }

        public static bool ShowNotifications
        {
            get => _settings.ShowNotifications;
            set
            {
                _settings.ShowNotifications = value;
                Save();
            }
        }

        public static bool EnableTrayIcon
        {
            get => _settings.EnableTrayIcon;
            set
            {
                _settings.EnableTrayIcon = value;
                Save();
            }
        }

        public static bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set
            {
                _settings.MinimizeToTray = value;
                Save();
            }
        }

        public static bool EnableLogging
        {
            get => _settings.EnableLogging;
            set
            {
                _settings.EnableLogging = value;
                Save();
            }
        }

        public static bool RunOnStartup
        {
            get => _settings.RunOnStartup;
            set
            {
                _settings.RunOnStartup = value;
                Save();
            }
        }

        public static bool SmoothScrolling
        {
            get => _settings.SmoothScrolling;
            set
            {
                _settings.SmoothScrolling = value;
                Save();
            }
        }

        public static int WindowWidth
        {
            get => _settings.WindowWidth;
            set { _settings.WindowWidth = value; Save(); }
        }

        public static int WindowHeight
        {
            get => _settings.WindowHeight;
            set { _settings.WindowHeight = value; Save(); }
        }

        public static string LastFolderPath
        {
            get => _settings.LastFolderPath;
            set { _settings.LastFolderPath = value; Save(); }
        }

        public static string LastSeenVersion
        {
            get => _settings.LastSeenVersion;
            set { _settings.LastSeenVersion = value; Save(); }
        }

        public static bool HasCompletedOnboarding
        {
            get => _settings.HasCompletedOnboarding;
            set { _settings.HasCompletedOnboarding = value; Save(); }
        }

        public static bool EnableUpcomingReminders
        {
            get => _settings.EnableUpcomingReminders;
            set { _settings.EnableUpcomingReminders = value; Save(); }
        }

        public static int ReminderLeadMinutes
        {
            get => _settings.ReminderLeadMinutes;
            set { _settings.ReminderLeadMinutes = value; Save(); }
        }

        public static List<string> SavedCategories
        {
            get => _settings.SavedCategories;
            set { _settings.SavedCategories = value; Save(); }
        }

        public static List<string> SavedTags
        {
            get => _settings.SavedTags;
            set { _settings.SavedTags = value; Save(); }
        }

        public static void ExportSettings(string targetPath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(targetPath, json);
                LogService.Info($"Settings exported to {targetPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to export settings", ex);
                throw;
            }
        }

        public static void ImportSettings(string sourcePath)
        {
            try
            {
                string json = File.ReadAllText(sourcePath);
                var imported = JsonSerializer.Deserialize<AppSettings>(json);
                if (imported != null)
                {
                    _settings = imported;
                    Save();
                    LogService.Info($"Settings imported from {sourcePath}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to import settings", ex);
                throw;
            }
        }
    }
}
