using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Services
{
    public class AppSettings
    {
        public string Theme { get; set; } = "Default";
        public bool IsOledMode { get; set; } = false;
        public string Language { get; set; } = "en-US";
        public bool ConfirmDelete { get; set; } = true;
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

        private static void Load()
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
    }
}
