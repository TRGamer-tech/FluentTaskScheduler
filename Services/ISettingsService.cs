using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Services
{
    /// <summary>Persisted app settings, backed by a JSON file under %LocalAppData%.</summary>
    public interface ISettingsService
    {
        ElementTheme Theme { get; set; }
        bool IsOledMode { get; set; }
        bool IsMicaEnabled { get; set; }
        string Language { get; set; }
        bool ConfirmDelete { get; set; }
        bool ShowNotifications { get; set; }
        bool EnableTrayIcon { get; set; }
        bool MinimizeToTray { get; set; }
        bool EnableLogging { get; set; }
        bool SeparateLogFiles { get; set; }
        bool EnableExecutionHistoryLog { get; set; }
        bool RunOnStartup { get; set; }
        bool SmoothScrolling { get; set; }
        int WindowWidth { get; set; }
        int WindowHeight { get; set; }
        string LastFolderPath { get; set; }
        string LastSeenVersion { get; set; }
        bool HasCompletedOnboarding { get; set; }
        bool EnableUpcomingReminders { get; set; }
        int ReminderLeadMinutes { get; set; }
        List<string> SavedCategories { get; set; }
        List<string> SavedTags { get; set; }
        bool ShowHiddenTasks { get; set; }

        void Load();
        void ExportSettings(string targetPath);
        void ImportSettings(string sourcePath);
    }
}
