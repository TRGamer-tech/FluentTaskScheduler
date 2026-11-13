using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Logging;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Updates;
using Serilog;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The settings page. The preferences themselves live in the settings store: every property
/// here reads from it and writes straight to it, and whoever has to act on a change listens to
/// the store, not to this page.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly AppTheme[] Themes = [AppTheme.Default, AppTheme.Light, AppTheme.Dark];

    // First the choice to follow Windows, which is stored as no language at all.
    private static readonly string[] Languages = ["", .. AppLanguages.Supported];

    private static readonly int[] UsualLeadMinutes = [1, 5, 10, 15, 30];

    private readonly ISettingsStore _settings;
    private readonly IStartWithWindows _startWithWindows;
    private readonly AppLogging _logging;
    private readonly AppPaths _paths;
    private readonly AppInfo _app;
    private readonly UpdateService _updates;
    private readonly IReleaseNotesSource _releaseNotes;
    private readonly IWelcomeDialogs _dialogs;
    private readonly IFilePicker _files;
    private readonly IPrompts _prompts;
    private readonly ILauncher _launcher;
    private readonly IUiThread _ui;

    private int[] _leadMinutes = [];

    public SettingsViewModel(
        ISettingsStore settings,
        IStartWithWindows startWithWindows,
        AppLogging logging,
        AppPaths paths,
        AppInfo app,
        UpdateService updates,
        IReleaseNotesSource releaseNotes,
        IWelcomeDialogs dialogs,
        IFilePicker files,
        IPrompts prompts,
        ILauncher launcher,
        IUiThread ui)
    {
        _settings = settings;
        _startWithWindows = startWithWindows;
        _logging = logging;
        _paths = paths;
        _app = app;
        _updates = updates;
        _releaseNotes = releaseNotes;
        _dialogs = dialogs;
        _files = files;
        _prompts = prompts;
        _launcher = launcher;
        _ui = ui;

        ThemeNames = [Loc.Get("Settings.Theme.System"), Loc.Get("Settings.Theme.Light"), Loc.Get("Settings.Theme.Dark")];
        LanguageNames = [Loc.Get("Settings.Appearance.Language.Windows"), .. AppLanguages.Supported.Select(NameOfLanguage)];
        OfferLeadMinutes();

        Categories = new NameListViewModel(() => Current.SavedCategories, names => Change(s => s with { SavedCategories = names }));
        Tags = new NameListViewModel(() => Current.SavedTags, names => Change(s => s with { SavedTags = names }));

        _settings.Changed += OnSettingsChanged;
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>
    /// Makes the page read everything again. Needed when the page comes into view, for what can
    /// change behind its back: starting with Windows can also be switched in Task Manager.
    /// </summary>
    public void Refresh()
    {
        if (!_leadMinutes.Contains(Current.ReminderLeadMinutes)) OfferLeadMinutes();
        Categories.Refresh();
        Tags.Refresh();

        // No name: all properties have changed.
        OnPropertyChanged(string.Empty);
    }

    public IReadOnlyList<string> ThemeNames { get; }

    public int ThemeIndex
    {
        get => Array.IndexOf(Themes, Current.Theme);
        set
        {
            if (IsChoice(value, Themes)) Change(s => s with { Theme = Themes[value] });
        }
    }

    public IReadOnlyList<string> LanguageNames { get; }

    /// <summary>A stored language the app does not have counts as following Windows, because that is what happens then.</summary>
    public int LanguageIndex
    {
        get => Math.Max(0, Array.FindIndex(Languages, language => language.Equals(Current.Language, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (IsChoice(value, Languages)) Change(s => s with { Language = Languages[value] });
        }
    }

    public bool IsMicaEnabled
    {
        get => Current.IsMicaEnabled;
        set => Change(s => s with { IsMicaEnabled = value });
    }

    public bool IsOledMode
    {
        get => Current.IsOledMode;
        set => Change(s => s with { IsOledMode = value });
    }

    /// <summary>
    /// Whether the page is showing dark right now. Only the page can tell: while the theme follows
    /// Windows, the answer is with Windows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseOled), nameof(CanUseMica))]
    private bool _isDarkTheme;

    /// <summary>Black backgrounds only exist in the dark theme.</summary>
    public bool CanUseOled => IsDarkTheme;

    /// <summary>Black backgrounds leave nothing for Mica to show through.</summary>
    public bool CanUseMica => !(IsDarkTheme && Current.IsOledMode);

    public bool SmoothScrolling
    {
        get => Current.SmoothScrolling;
        set => Change(s => s with { SmoothScrolling = value });
    }

    public bool ShowNotifications
    {
        get => Current.ShowNotifications;
        set => Change(s => s with { ShowNotifications = value });
    }

    public bool EnableUpcomingReminders
    {
        get => Current.EnableUpcomingReminders;
        set => Change(s => s with { EnableUpcomingReminders = value });
    }

    public bool CanRemind => Current.ShowNotifications;

    /// <summary>The usual lead times, plus the stored one should it be none of them.</summary>
    public IReadOnlyList<string> ReminderLeadNames { get; private set; } = [];

    public int ReminderLeadIndex
    {
        get => Array.IndexOf(_leadMinutes, Current.ReminderLeadMinutes);
        set
        {
            if (IsChoice(value, _leadMinutes)) Change(s => s with { ReminderLeadMinutes = _leadMinutes[value] });
        }
    }

    public bool CanChooseReminderLead => Current.ShowNotifications && Current.EnableUpcomingReminders;

    public bool StartWithWindows
    {
        get => _startWithWindows.IsEnabled;
        set
        {
            if (value == _startWithWindows.IsEnabled) return;
            if (!_startWithWindows.TrySetEnabled(value)) _ = _prompts.ShowErrorAsync(Loc.Get("Settings.Sys.Startup.Failed"));

            // Whatever was asked for, the switch is to show what Windows says now.
            OnPropertyChanged();
        }
    }

    public bool EnableTrayIcon
    {
        get => Current.EnableTrayIcon;
        set => Change(s => s with { EnableTrayIcon = value });
    }

    public bool MinimizeToTray
    {
        get => Current.MinimizeToTray;
        set => Change(s => s with { MinimizeToTray = value });
    }

    public bool CanMinimizeToTray => Current.EnableTrayIcon;

    public bool EnableTaskPipelines
    {
        get => Current.EnableTaskPipelines;
        set => Change(s => s with { EnableTaskPipelines = value });
    }

    public bool ShowHiddenTasks
    {
        get => Current.ShowHiddenTasks;
        set => Change(s => s with { ShowHiddenTasks = value });
    }

    public bool ConfirmDelete
    {
        get => Current.ConfirmDelete;
        set => Change(s => s with { ConfirmDelete = value });
    }

    public bool EnableLogging
    {
        get => Current.EnableLogging;
        set => Change(s => s with { EnableLogging = value });
    }

    public bool SeparateLogFiles
    {
        get => Current.SeparateLogFiles;
        set => Change(s => s with { SeparateLogFiles = value });
    }

    public bool CanSeparateLogFiles => Current.EnableLogging;

    public bool EnableExecutionHistoryLog
    {
        get => Current.EnableExecutionHistoryLog;
        set => Change(s => s with { EnableExecutionHistoryLog = value });
    }

    public NameListViewModel Categories { get; }
    public NameListViewModel Tags { get; }

    public string VersionText => Loc.Format("Settings.About.VersionFormat", _app.VersionText);

    /// <summary>What the last look for an update found; before the first look, what looking is good for.</summary>
    [ObservableProperty]
    private string _updateMessage = Loc.Get("Settings.About.Update.Desc");

    [RelayCommand]
    private Task OpenAppLogAsync() => OpenLogAsync(LogKind.App);

    [RelayCommand]
    private Task OpenErrorLogAsync() => OpenLogAsync(LogKind.Errors);

    [RelayCommand]
    private Task OpenCrashLogAsync() => OpenLogAsync(LogKind.Crashes);

    [RelayCommand]
    private Task OpenDataFolderAsync() => OpenFolderAsync(_paths.DataFolder);

    [RelayCommand]
    private Task OpenExecutionHistoryFolderAsync() => OpenFolderAsync(_paths.ExecutionHistoryFolder);

    [RelayCommand]
    private async Task ExportAsync()
    {
        string? path = await _files.PickSaveFileAsync(Loc.Get("Settings.Data.Export.Title"), Loc.Get("Settings.Data.FileType"), "json", "FluentTaskScheduler_Settings");
        if (path is null) return;

        try
        {
            _settings.Export(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _prompts.ShowMessageAsync(Loc.Get("Settings.Export.Failed.Title"), Loc.Format("Settings.Export.Failed.ContentFormat", path, ex.Message));
            return;
        }
        await _prompts.ShowMessageAsync(Loc.Get("Settings.Export.Success.Title"), Loc.Format("Settings.Export.Success.ContentFormat", path));
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        string? path = await _files.PickOpenFileAsync(Loc.Get("Settings.Data.Import.Title"), Loc.Get("Settings.Data.FileType"), "json");
        if (path is null) return;

        try
        {
            _settings.Import(path);
        }
        catch (InvalidDataException)
        {
            await _prompts.ShowMessageAsync(Loc.Get("Settings.Import.Failed.Title"), Loc.Format("Settings.Import.Failed.NotSettingsFormat", path));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _prompts.ShowMessageAsync(Loc.Get("Settings.Import.Failed.Title"), Loc.Format("Settings.Import.Failed.ContentFormat", path, ex.Message));
            return;
        }
        await _prompts.ShowMessageAsync(Loc.Get("Settings.Import.Success.Title"), Loc.Get("Settings.Import.Success.Content"));
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateMessage = Loc.Get("Settings.About.Update.Checking");
        UpdateCheck check = await _updates.CheckAsync();
        UpdateMessage = check.Status switch
        {
            UpdateStatus.UpToDate => Loc.Get("Settings.UpToDate.Content"),
            UpdateStatus.Ready => Loc.Format("Settings.About.Update.ReadyFormat", check.Version),
            UpdateStatus.Unavailable => Loc.Get("Settings.About.Update.Unavailable"),
            _ => Loc.Format("Settings.UpdateError.ContentFormat", check.Error)
        };
        if (check.Status == UpdateStatus.Ready) await _updates.OfferRestartAsync(_prompts, check);
    }

    [RelayCommand]
    private async Task ShowReleaseNotesAsync()
    {
        if (await _releaseNotes.FindAsync(_app.VersionText) is { } notes) await _dialogs.ShowReleaseNotesAsync(notes);
        else await _prompts.ShowMessageAsync(Loc.Get("Settings.WhatsNew.Title"), Loc.Get("Settings.WhatsNew.FetchFailed"));
    }

    [RelayCommand]
    private Task ShowTourAsync() => _dialogs.ShowTourAsync();

    [RelayCommand]
    private Task OpenRepositoryAsync() => OpenAsync(AppInfo.RepositoryUrl);

    [RelayCommand]
    private Task ReportProblemAsync() => OpenAsync(AppInfo.IssuesUrl);

    private AppSettings Current => _settings.Current;

    private void Change(Func<AppSettings, AppSettings> change) => _settings.Update(change);

    // A list reports -1 while nothing is selected in it, which is also the case while its items are being replaced.
    private static bool IsChoice(int index, Array choices) => index >= 0 && index < choices.Length;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        // While the window is being resized its size is stored many times a second. The page shows
        // neither that nor the last folder, so such a change is no reason to read everything again.
        AppSettings shown = change.Current;
        AppSettings before = change.Previous with { WindowWidth = shown.WindowWidth, WindowHeight = shown.WindowHeight, LastFolderPath = shown.LastFolderPath };

        // The store announces a change on whichever thread made it.
        if (before != shown) _ui.Post(Refresh);
    }

    private void OfferLeadMinutes()
    {
        _leadMinutes = UsualLeadMinutes.Append(Current.ReminderLeadMinutes).Distinct().Order().ToArray();
        ReminderLeadNames = _leadMinutes
            .Select(minutes => minutes == 1 ? Loc.Get("Settings.Lead.1m") : Loc.Format("Settings.Lead.MinutesFormat", minutes))
            .ToArray();
    }

    private async Task OpenLogAsync(LogKind kind)
    {
        if (_logging.CurrentFile(kind) is { } file) await OpenAsync(file);
        else await _prompts.ShowMessageAsync(Loc.Get("Settings.Adv.Logging.Title"), Loc.Get("Settings.Adv.Logs.Empty"));
    }

    private async Task OpenFolderAsync(string folder)
    {
        try
        {
            // A folder of the app only comes into being with the first file the app puts there.
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Opening it fails next, and that is what the user is told.
            Log.Warning(ex, "Could not create the folder {Folder}", folder);
        }
        await OpenAsync(folder);
    }

    private async Task OpenAsync(string target)
    {
        if (!_launcher.TryOpen(target)) await _prompts.ShowErrorAsync(Loc.Format("Settings.OpenFailedFormat", target));
    }

    private static string NameOfLanguage(string tag) => tag switch
    {
        "en-US" => "English",
        "de-DE" => "Deutsch",
        "ja-JP" => "日本語",
        "zh-CN" => "简体中文",
        _ => CultureInfo.GetCultureInfo(tag).NativeName
    };
}
