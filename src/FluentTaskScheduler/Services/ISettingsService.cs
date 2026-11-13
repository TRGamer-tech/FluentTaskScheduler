using System;
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

        // Global snooze
        bool IsSnoozed { get; set; }
        DateTime? SnoozeUntilUtc { get; set; }
        bool SnoozeUntilReboot { get; set; }
        string SnoozeBootStamp { get; set; }
        bool SnoozeSuspendsScheduledTasks { get; set; }
        bool SnoozeIncludeMicrosoftTasks { get; set; }
        List<string> SnoozeDisabledTaskPaths { get; set; }

        // Task chaining
        bool EnableTaskPipelines { get; set; }

        void Load();
        /// <summary>Forces any pending debounced save to disk immediately (e.g. before app exit).</summary>
        void Flush();
        /// <summary>Sets width and height together as a single (debounced) settings write.</summary>
        void SetWindowSize(int width, int height);
        void AddSavedCategory(string category);
        void RemoveSavedCategory(string category);
        void AddSavedTag(string tag);
        void RemoveSavedTag(string tag);
        /// <summary>Writes the snooze fields together and flushes immediately (not debounced).</summary>
        void SaveSnoozeState(bool isSnoozed, DateTime? untilUtc, bool untilReboot, string bootStamp, List<string> disabledPaths);
        void ExportSettings(string targetPath);
        void ImportSettings(string sourcePath);
    }
}
