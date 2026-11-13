using System.Reflection;
using FluentTaskScheduler.Logging;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.Updates;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly SettingsStore _settings;
    private readonly FakeStartWithWindows _startup = new();
    private readonly AppLogging _logging;
    private readonly AppInfo _app = new(new Version(2, 0, 0));
    private readonly FakeUpdater _updater = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly UpdateService _updates;
    private readonly FakeReleaseNotes _notes = new();
    private readonly RecordingWelcomeDialogs _dialogs = new();
    private readonly ScriptedFilePicker _files = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly SettingsViewModel _page;
    private readonly List<string?> _changed = [];

    public SettingsViewModelTests()
    {
        _settings = new SettingsStore(_folder.Paths);
        _logging = new AppLogging(_folder.Paths);
        _updates = new UpdateService(_updater, _lifetime);
        _page = NewPage();
        _page.PropertyChanged += (_, e) => _changed.Add(e.PropertyName);
    }

    public void Dispose()
    {
        _page.Dispose();
        _logging.Logger.Dispose();
        _settings.Dispose();
        _folder.Dispose();
    }

    private SettingsViewModel NewPage() => new(
        _settings, _startup, _logging, _folder.Paths, _app, _updates, _notes, _dialogs, _files, _prompts, _launcher, new InlineUiThread());

    private static readonly string[] SwitchNames =
    [
        nameof(AppSettings.IsMicaEnabled),
        nameof(AppSettings.IsOledMode),
        nameof(AppSettings.SmoothScrolling),
        nameof(AppSettings.ShowNotifications),
        nameof(AppSettings.EnableUpcomingReminders),
        nameof(AppSettings.EnableTrayIcon),
        nameof(AppSettings.MinimizeToTray),
        nameof(AppSettings.EnableTaskPipelines),
        nameof(AppSettings.ShowHiddenTasks),
        nameof(AppSettings.ConfirmDelete),
        nameof(AppSettings.EnableLogging),
        nameof(AppSettings.SeparateLogFiles),
        nameof(AppSettings.EnableExecutionHistoryLog)
    ];

    public static IEnumerable<object[]> Switches => SwitchNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Switches))]
    public void ASwitch_ShowsItsSetting_AndWritesItToTheStore(string setting)
    {
        PropertyInfo onPage = typeof(SettingsViewModel).GetProperty(setting)!;
        PropertyInfo stored = typeof(AppSettings).GetProperty(setting)!;
        bool before = (bool)stored.GetValue(_settings.Current)!;
        Assert.Equal(before, onPage.GetValue(_page));

        onPage.SetValue(_page, !before);

        Assert.Equal(!before, stored.GetValue(_settings.Current));
        Assert.Equal(!before, onPage.GetValue(_page));
        Assert.Equal(!before, onPage.GetValue(NewPage()));
    }

    [Fact]
    public void EveryYesOrNoPreference_HasItsSwitch()
    {
        // Not preferences the page offers: two are noted by the app itself, two belong to the snooze dialog.
        string[] elsewhere =
        [
            nameof(AppSettings.HasCompletedOnboarding),
            nameof(AppSettings.HasSeenTrayHint),
            nameof(AppSettings.SnoozeSuspendsScheduledTasks),
            nameof(AppSettings.SnoozeIncludeMicrosoftTasks)
        ];

        IEnumerable<string> flags = typeof(AppSettings).GetProperties().Where(p => p.PropertyType == typeof(bool)).Select(p => p.Name).Except(elsewhere);

        Assert.Equal(flags.Order(), SwitchNames.Order());
    }

    [Fact]
    public void AChangeMadeElsewhere_ReachesThePage()
    {
        _settings.Update(s => s with { ConfirmDelete = false, Theme = AppTheme.Dark });

        Assert.False(_page.ConfirmDelete);
        Assert.Equal(2, _page.ThemeIndex);
        Assert.Contains("", _changed);
    }

    [Fact]
    public void AChangeToWhatThePageDoesNotShow_LeavesItAlone()
    {
        _settings.Update(s => s with { WindowWidth = 1500, WindowHeight = 900, LastFolderPath = @"\Backups" });

        Assert.Empty(_changed);
    }

    [Fact]
    public void ADisposedPage_NoLongerListensToTheStore()
    {
        _page.Dispose();

        _settings.Update(s => s with { ConfirmDelete = false });

        Assert.Empty(_changed);
    }

    [Fact]
    public void TheTheme_IsChosenFromFollowWindowsLightAndDark()
    {
        Assert.Equal(["Settings.Theme.System", "Settings.Theme.Light", "Settings.Theme.Dark"], _page.ThemeNames);
        Assert.Equal(0, _page.ThemeIndex);

        _page.ThemeIndex = 1;
        Assert.Equal(AppTheme.Light, _settings.Current.Theme);

        _page.ThemeIndex = 2;
        Assert.Equal(AppTheme.Dark, _settings.Current.Theme);

        _page.ThemeIndex = -1;
        Assert.Equal(AppTheme.Dark, _settings.Current.Theme);
    }

    [Fact]
    public void TheLanguage_IsChosenFromTheLanguageOfWindowsAndTheTranslations()
    {
        Assert.Equal(["Settings.Appearance.Language.Windows", "English", "Deutsch", "日本語", "简体中文"], _page.LanguageNames);
        Assert.Equal(0, _page.LanguageIndex);

        _page.LanguageIndex = 2;
        Assert.Equal("de-DE", _settings.Current.Language);
        Assert.Equal(2, _page.LanguageIndex);

        _page.LanguageIndex = 0;
        Assert.Equal("", _settings.Current.Language);

        _page.LanguageIndex = -1;
        Assert.Equal("", _settings.Current.Language);
    }

    [Theory]
    [InlineData("ja-jp", 3)]
    [InlineData("fr-FR", 0)]
    public void AStoredLanguage_ShowsAsTheChoiceItAmountsTo(string stored, int expectedIndex)
    {
        _settings.Update(s => s with { Language = stored });

        Assert.Equal(expectedIndex, _page.LanguageIndex);
    }

    [Fact]
    public void BlackBackgrounds_NeedTheDarkTheme_AndLeaveNoRoomForMica()
    {
        Assert.False(_page.CanUseOled);
        Assert.True(_page.CanUseMica);

        _page.IsDarkTheme = true;
        Assert.True(_page.CanUseOled);
        Assert.True(_page.CanUseMica);
        Assert.Contains(nameof(SettingsViewModel.CanUseOled), _changed);

        _page.IsOledMode = true;
        Assert.False(_page.CanUseMica);

        _page.IsDarkTheme = false;
        Assert.True(_page.CanUseMica);
    }

    [Fact]
    public void WhatDependsOnAnotherSwitch_IsOnlyOfferedWhileThatOneIsOn()
    {
        Assert.True(_page.CanRemind);
        Assert.True(_page.CanChooseReminderLead);
        Assert.True(_page.CanMinimizeToTray);
        Assert.True(_page.CanSeparateLogFiles);

        _page.EnableUpcomingReminders = false;
        Assert.True(_page.CanRemind);
        Assert.False(_page.CanChooseReminderLead);

        _page.EnableUpcomingReminders = true;
        _page.ShowNotifications = false;
        Assert.False(_page.CanRemind);
        Assert.False(_page.CanChooseReminderLead);

        _page.EnableTrayIcon = false;
        Assert.False(_page.CanMinimizeToTray);

        _page.EnableLogging = false;
        Assert.False(_page.CanSeparateLogFiles);
    }

    [Fact]
    public void TheReminderLead_IsChosenFromTheUsualTimes()
    {
        Assert.Equal(5, _page.ReminderLeadNames.Count);
        Assert.Equal("Settings.Lead.1m", _page.ReminderLeadNames[0]);
        Assert.Equal(1, _page.ReminderLeadIndex);

        _page.ReminderLeadIndex = 4;

        Assert.Equal(30, _settings.Current.ReminderLeadMinutes);
        Assert.Equal(4, _page.ReminderLeadIndex);
    }

    [Fact]
    public void AReminderLeadThatIsNoneOfTheUsual_IsOfferedToo_InItsPlace()
    {
        _settings.Update(s => s with { ReminderLeadMinutes = 7 });

        Assert.Equal(6, _page.ReminderLeadNames.Count);
        Assert.Equal(2, _page.ReminderLeadIndex);

        _page.ReminderLeadIndex = 3;

        Assert.Equal(10, _settings.Current.ReminderLeadMinutes);
    }

    [Fact]
    public void StartingWithWindows_ShowsWhatWindowsSays_AndChangesItThere()
    {
        _startup.IsEnabled = true;
        Assert.True(_page.StartWithWindows);

        _page.StartWithWindows = false;

        Assert.False(_startup.IsEnabled);
        Assert.Empty(_prompts.Shown);
    }

    [Fact]
    public void WhenWindowsRefusesToStartTheApp_TheSwitchGoesBack_AndTheUserIsTold()
    {
        _startup.Refuses = true;

        _page.StartWithWindows = true;

        Assert.False(_page.StartWithWindows);
        Assert.Contains(nameof(SettingsViewModel.StartWithWindows), _changed);
        Assert.Equal(["Error: Settings.Sys.Startup.Failed"], _prompts.Shown);
    }

    [Fact]
    public void Refresh_MakesThePageReadWhatChangedBehindItsBack()
    {
        _startup.IsEnabled = true;

        _page.Refresh();

        Assert.Contains("", _changed);
        Assert.True(_page.StartWithWindows);
    }

    [Fact]
    public void Categories_AreKeptInTheSettings()
    {
        Assert.Empty(_page.Categories.Items);

        _page.Categories.NewName = "Personal";
        _page.Categories.AddCommand.Execute(null);
        _page.Categories.NewName = "  Backups ";
        _page.Categories.AddCommand.Execute(null);

        Assert.Equal(["Personal", "Backups"], _settings.Current.SavedCategories);
        Assert.Equal(_settings.Current.SavedCategories, _page.Categories.Items);

        _page.Categories.RemoveCommand.Execute("Personal");

        Assert.Equal(["Backups"], _settings.Current.SavedCategories);
        Assert.Empty(_settings.Current.SavedTags);
    }

    [Fact]
    public void Tags_AreKeptInTheSettings_AndFollowAChangeMadeElsewhere()
    {
        Assert.Empty(_page.Tags.Items);

        _page.Tags.NewName = "nightly";
        _page.Tags.AddCommand.Execute(null);

        Assert.Equal(["nightly"], _settings.Current.SavedTags);

        _settings.Update(s => s with { SavedTags = ["one"] });

        Assert.Equal(["one"], _page.Tags.Items);
    }

    [Fact]
    public async Task Export_WritesTheSettingsToTheChosenFile_AndSaysWhere()
    {
        string file = _files.Choice = _folder.File("exported.json");
        _page.ConfirmDelete = false;

        await _page.ExportCommand.ExecuteAsync(null);

        Assert.Contains("\"ConfirmDelete\": false", File.ReadAllText(file));
        Assert.Equal(["save json FluentTaskScheduler_Settings"], _files.Asked);
        Assert.Equal(["Settings.Export.Success.Title: Settings.Export.Success.ContentFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task Export_DoesNothing_WhenNoFileIsChosen()
    {
        await _page.ExportCommand.ExecuteAsync(null);

        Assert.Empty(_prompts.Shown);
        Assert.DoesNotContain(Directory.GetFiles(_folder.Path), file => file != _folder.Paths.SettingsFile);
    }

    [Fact]
    public async Task Export_SaysSo_WhenTheFileCannotBeWritten()
    {
        _files.Choice = Path.Combine(_folder.Path, "no such folder", "exported.json");

        await _page.ExportCommand.ExecuteAsync(null);

        Assert.Equal(["Settings.Export.Failed.Title: Settings.Export.Failed.ContentFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task Import_TakesOverTheSettingsOfTheChosenFile()
    {
        string file = _files.Choice = _folder.File("other.json");
        File.WriteAllText(file, """{ "Theme": "Dark", "ConfirmDelete": false, "SavedTags": ["imported"] }""");

        await _page.ImportCommand.ExecuteAsync(null);

        Assert.Equal(AppTheme.Dark, _settings.Current.Theme);
        Assert.False(_page.ConfirmDelete);
        Assert.Equal(["imported"], _page.Tags.Items);
        Assert.Equal(["open json"], _files.Asked);
        Assert.Equal(["Settings.Import.Success.Title: Settings.Import.Success.Content"], _prompts.Shown);
    }

    [Theory]
    [InlineData("""{ "name": "some other program's file" }""")]
    [InlineData("not json at all")]
    public async Task Import_RefusesAFileThatHoldsNoSettings_AndChangesNothing(string content)
    {
        string file = _files.Choice = _folder.File("other.json");
        File.WriteAllText(file, content);
        _page.ConfirmDelete = false;

        await _page.ImportCommand.ExecuteAsync(null);

        Assert.False(_settings.Current.ConfirmDelete);
        Assert.Equal(["Settings.Import.Failed.Title: Settings.Import.Failed.NotSettingsFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task Import_SaysSo_WhenTheFileCannotBeRead()
    {
        _files.Choice = _folder.File("gone.json");

        await _page.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["Settings.Import.Failed.Title: Settings.Import.Failed.ContentFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task ALog_OpensInItsProgram_OnceThereIsOne()
    {
        await _page.OpenErrorLogCommand.ExecuteAsync(null);

        Assert.Empty(_launcher.Opened);
        Assert.Equal(["Settings.Adv.Logging.Title: Settings.Adv.Logs.Empty"], _prompts.Shown);

        _logging.Logger.Information("something happened");
        _logging.Logger.Error("something failed");
        _logging.Logger.Fatal("something crashed");
        await _page.OpenAppLogCommand.ExecuteAsync(null);
        await _page.OpenErrorLogCommand.ExecuteAsync(null);
        await _page.OpenCrashLogCommand.ExecuteAsync(null);

        Assert.Equal([_folder.File("App_Log.txt"), _folder.File("Error_Log.txt"), _folder.File("Crash_Log.txt")], _launcher.Opened);
    }

    [Fact]
    public async Task TheFoldersOfTheApp_OpenInExplorer_EvenBeforeTheAppPutAnythingThere()
    {
        await _page.OpenDataFolderCommand.ExecuteAsync(null);
        await _page.OpenExecutionHistoryFolderCommand.ExecuteAsync(null);

        Assert.Equal([_folder.Paths.DataFolder, _folder.Paths.ExecutionHistoryFolder], _launcher.Opened);
        Assert.True(Directory.Exists(_folder.Paths.ExecutionHistoryFolder));
    }

    [Fact]
    public async Task AFolderThatCannotBeMade_IsReportedLikeAnythingElseThatCannotBeOpened()
    {
        File.WriteAllText(_folder.Paths.ExecutionHistoryFolder, "a file where the folder should be");
        _launcher.Fails = true;

        await _page.OpenExecutionHistoryFolderCommand.ExecuteAsync(null);

        Assert.Equal(["Error: Settings.OpenFailedFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task WhenWindowsCannotOpenSomething_TheUserIsTold()
    {
        _launcher.Fails = true;

        await _page.OpenRepositoryCommand.ExecuteAsync(null);

        Assert.Equal([AppInfo.RepositoryUrl], _launcher.Opened);
        Assert.Equal(["Error: Settings.OpenFailedFormat"], _prompts.Shown);
    }

    [Fact]
    public async Task ReportingAProblem_OpensTheIssuesOfTheProject()
    {
        await _page.ReportProblemCommand.ExecuteAsync(null);

        Assert.Equal(["https://github.com/TRGamer-tech/FluentTaskScheduler/issues"], _launcher.Opened);
    }

    [Fact]
    public async Task CheckingForUpdates_SaysWhenTheAppIsUpToDate()
    {
        Assert.Equal("Settings.About.Update.Desc", _page.UpdateMessage);

        await _page.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Settings.UpToDate.Content", _page.UpdateMessage);
        Assert.Empty(_prompts.Shown);
    }

    [Fact]
    public async Task CheckingForUpdates_OffersToRestart_WhenANewerVersionIsReady()
    {
        _updater.Available = "2.1.0";
        _prompts.Confirm = false;

        await _page.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Settings.About.Update.ReadyFormat", _page.UpdateMessage);
        Assert.StartsWith("Dialog.UpdateAvailable.Title", Assert.Single(_prompts.Shown));
        Assert.False(_lifetime.ExitRequested);
    }

    [Fact]
    public async Task CheckingForUpdates_ExplainsACopyThatCannotUpdateItself_InsteadOfCallingItUpToDate()
    {
        _updater.CanUpdate = false;

        await _page.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Settings.About.Update.Unavailable", _page.UpdateMessage);
    }

    [Fact]
    public async Task CheckingForUpdates_SaysWhenItFailed()
    {
        _updater.Failure = new HttpRequestException("offline");

        await _page.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Settings.UpdateError.ContentFormat", _page.UpdateMessage);
    }

    [Fact]
    public async Task WhileAnUpdateCheckRuns_AnotherCannotBeStarted()
    {
        var download = new TaskCompletionSource();
        _updater.Hold = download;

        Task running = _page.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Settings.About.Update.Checking", _page.UpdateMessage);
        Assert.True(_page.CheckForUpdatesCommand.IsRunning);
        Assert.False(_page.CheckForUpdatesCommand.CanExecute(null));

        download.SetResult();
        await running;

        Assert.True(_page.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckingForUpdates_WhileTheCheckAfterStartRuns_AsksAboutTheUpdateOnce()
    {
        _settings.Update(s => s with { HasCompletedOnboarding = true, LastSeenVersion = "2.0.0" });
        _updater.Available = "2.1.0";
        _prompts.Confirm = false;
        var download = new TaskCompletionSource();
        _updater.Hold = download;

        Task afterStart = new StartupFlow(_settings, _app, _notes, _updates, _dialogs, _prompts).RunAsync();
        Task byHand = _page.CheckForUpdatesCommand.ExecuteAsync(null);
        download.SetResult();
        await Task.WhenAll(afterStart, byHand);

        Assert.StartsWith("Dialog.UpdateAvailable.Title", Assert.Single(_prompts.Shown));
        Assert.Equal("Settings.About.Update.ReadyFormat", _page.UpdateMessage);
        Assert.Equal(1, _updater.Checks);
    }

    [Fact]
    public async Task TheReleaseNotes_AreThoseOfTheRunningVersion()
    {
        _notes.Add("2.0.0").Add("2.1.0");

        await _page.ShowReleaseNotesCommand.ExecuteAsync(null);

        Assert.Equal(["ReleaseNotes 2.0.0"], _dialogs.Shown);
        Assert.Empty(_prompts.Shown);
    }

    [Fact]
    public async Task WithoutReleaseNotes_TheUserIsToldWhyNothingShows()
    {
        await _page.ShowReleaseNotesCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Shown);
        Assert.Equal(["Settings.WhatsNew.Title: Settings.WhatsNew.FetchFailed"], _prompts.Shown);
    }

    [Fact]
    public async Task TheWelcomeTour_CanBeShownAgain()
    {
        await _page.ShowTourCommand.ExecuteAsync(null);

        Assert.Equal(["Tour"], _dialogs.Shown);
    }
}
