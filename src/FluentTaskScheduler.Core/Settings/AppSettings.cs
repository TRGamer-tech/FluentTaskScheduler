namespace FluentTaskScheduler.Settings;

/// <summary>Member names are the stored format.</summary>
public enum AppTheme
{
    /// <summary>Follow Windows.</summary>
    Default,
    Light,
    Dark
}

/// <summary>
/// The user's preferences, as an immutable snapshot. Read them from <see cref="ISettingsStore.Current"/>
/// and change them with <see cref="ISettingsStore.Update"/>. Property names are the stored format.
/// </summary>
public sealed record AppSettings
{
    public AppTheme Theme { get; init; }

    /// <summary>Pure black backgrounds; only applies while the app is dark.</summary>
    public bool IsOledMode { get; init; }
    public bool IsMicaEnabled { get; init; } = true;

    /// <summary>BCP-47 tag of the UI language; empty to follow Windows.</summary>
    public string Language { get; init; } = "";

    public bool ConfirmDelete { get; init; } = true;
    public bool ShowNotifications { get; init; } = true;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool EnableTrayIcon { get; init; } = true;
    public bool MinimizeToTray { get; init; }

    public bool EnableLogging { get; init; } = true;
    public bool SeparateLogFiles { get; init; } = true;
    public bool EnableExecutionHistoryLog { get; init; }
    public bool SmoothScrolling { get; init; } = true;

    public int WindowWidth { get; init; } = DefaultWindowWidth;
    public int WindowHeight { get; init; } = DefaultWindowHeight;
    public string LastFolderPath { get; init; } = "\\";

    /// <summary>App version whose release notes were last shown.</summary>
    public string LastSeenVersion { get; init; } = "";
    public bool HasCompletedOnboarding { get; init; }

    public bool EnableUpcomingReminders { get; init; } = true;
    public int ReminderLeadMinutes { get; init; } = 5;
    public bool ShowHiddenTasks { get; init; } = true;

    public ValueList<string> SavedCategories { get; init; } = ["Work", "Personal", "Maintenance", "System"];
    public ValueList<string> SavedTags { get; init; } = ["urgent", "sync", "database", "cleanup"];

    /// <summary>Snoozing all tasks also disables their triggers in Task Scheduler until the snooze ends.</summary>
    public bool SnoozeSuspendsScheduledTasks { get; init; }

    /// <summary>Suspending triggers includes tasks under \Microsoft\. Off by default: disabling those can break Windows.</summary>
    public bool SnoozeIncludeMicrosoftTasks { get; init; }

    /// <summary>Completion actions are carried out while the app runs.</summary>
    public bool EnableTaskPipelines { get; init; } = true;

    public const int DefaultWindowWidth = 1200;
    public const int DefaultWindowHeight = 800;
    public const int MinReminderLeadMinutes = 1;
    public const int MaxReminderLeadMinutes = 120;

    /// <summary>
    /// Brings values that a hand-edited or foreign file may hold back into what the app can work
    /// with. Called for everything that enters the store.
    /// </summary>
    internal AppSettings Normalized() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.Default,
        Language = Language?.Trim() ?? "",
        LastFolderPath = string.IsNullOrWhiteSpace(LastFolderPath) ? "\\" : LastFolderPath,
        LastSeenVersion = LastSeenVersion ?? "",
        WindowWidth = WindowWidth >= 320 ? WindowWidth : DefaultWindowWidth,
        WindowHeight = WindowHeight >= 240 ? WindowHeight : DefaultWindowHeight,
        ReminderLeadMinutes = Math.Clamp(ReminderLeadMinutes, MinReminderLeadMinutes, MaxReminderLeadMinutes),
        SavedCategories = Clean(SavedCategories),
        SavedTags = Clean(SavedTags)
    };

    private static ValueList<string> Clean(ValueList<string>? items) =>
        items is null
            ? []
            : items.Where(item => !string.IsNullOrWhiteSpace(item))
                   .Select(item => item.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToValueList();
}
