using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => _folder.Dispose();

    private SettingsStore NewStore() => new(_folder.Paths, _time);

    [Fact]
    public void WithoutAFile_TheDefaultsApply_AndNothingIsWritten()
    {
        using var store = NewStore();

        Assert.Equal(new AppSettings(), store.Current);
        Assert.False(File.Exists(_folder.Paths.SettingsFile));
    }

    [Fact]
    public void AChange_IsVisibleAtOnce_AndOnDiskAfterTheSaveDelay()
    {
        using var store = NewStore();

        store.Update(s => s with { ConfirmDelete = false });

        Assert.False(store.Current.ConfirmDelete);
        Assert.False(File.Exists(_folder.Paths.SettingsFile));

        _time.Advance(TimeSpan.FromSeconds(1));

        using var reopened = NewStore();
        Assert.False(reopened.Current.ConfirmDelete);
    }

    [Fact]
    public void ABurstOfChanges_IsWrittenOnce_WithTheLastValues()
    {
        using var store = NewStore();
        for (int width = 900; width <= 1000; width++)
        {
            store.Update(s => s with { WindowWidth = width });
            _time.Advance(TimeSpan.FromMilliseconds(100)); // shorter than the delay: keeps postponing the write
            Assert.False(File.Exists(_folder.Paths.SettingsFile));
        }

        _time.Advance(TimeSpan.FromSeconds(1));

        using var reopened = NewStore();
        Assert.Equal(1000, reopened.Current.WindowWidth);
    }

    [Fact]
    public void Flush_WritesPendingChangesNow()
    {
        using var store = NewStore();
        store.Update(s => s with { Theme = AppTheme.Dark, SavedTags = ["one", "two"] });

        store.Flush();

        using var reopened = NewStore();
        Assert.Equal(AppTheme.Dark, reopened.Current.Theme);
        Assert.Equal(["one", "two"], reopened.Current.SavedTags);
    }

    [Fact]
    public void Disposing_SavesWhatIsPending()
    {
        var store = NewStore();
        store.Update(s => s with { MinimizeToTray = true });

        store.Dispose();

        using var reopened = NewStore();
        Assert.True(reopened.Current.MinimizeToTray);
    }

    [Fact]
    public void Changed_ReportsTheSettingsBeforeAndAfter_AndOnlyForRealChanges()
    {
        using var store = NewStore();
        var events = new List<SettingsChangedEventArgs>();
        store.Changed += (_, e) => events.Add(e);

        store.Update(s => s with { ReminderLeadMinutes = 15 });
        store.Update(s => s with { ReminderLeadMinutes = 15 });
        store.Update(s => s);

        var change = Assert.Single(events);
        Assert.Equal(5, change.Previous.ReminderLeadMinutes);
        Assert.Equal(15, change.Current.ReminderLeadMinutes);
    }

    [Fact]
    public void AFileWrittenByVersion19_IsReadIncludingItsStringTheme()
    {
        // As 1.9 wrote it: compact, the theme as a name, and state the store no longer owns.
        File.WriteAllText(_folder.Paths.SettingsFile,
            """{"Theme":"Dark","IsOledMode":true,"IsMicaEnabled":false,"Language":"de-DE","ConfirmDelete":false,"RunOnStartup":true,"WindowWidth":1400,"WindowHeight":900,"LastFolderPath":"\\Backups","SavedCategories":["Work"],"SavedTags":[],"IsSnoozed":true,"SnoozeUntilUtc":"2026-01-01T10:00:00Z","SnoozeDisabledTaskPaths":["\\A"],"EnableTaskPipelines":false}""");

        using var store = NewStore();

        Assert.Equal(AppTheme.Dark, store.Current.Theme);
        Assert.True(store.Current.IsOledMode);
        Assert.False(store.Current.IsMicaEnabled);
        Assert.Equal("de-DE", store.Current.Language);
        Assert.False(store.Current.ConfirmDelete);
        Assert.Equal(1400, store.Current.WindowWidth);
        Assert.Equal(@"\Backups", store.Current.LastFolderPath);
        Assert.Equal(["Work"], store.Current.SavedCategories);
        Assert.Empty(store.Current.SavedTags);
        Assert.False(store.Current.EnableTaskPipelines);
        Assert.True(store.Current.ShowNotifications); // not in the file: default
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"Theme\": \"Dar")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"Theme\": \"Purple\" }")]
    [InlineData("{ \"WindowWidth\": \"wide\" }")]
    public void AnUnreadableFile_IsSetAside_AndTheDefaultsApply(string content)
    {
        File.WriteAllText(_folder.Paths.SettingsFile, content);

        using var store = NewStore();

        Assert.Equal(new AppSettings(), store.Current);
        Assert.NotNull(store.UnreadableFileBackup);
        Assert.Equal(content, File.ReadAllText(store.UnreadableFileBackup));
        Assert.False(File.Exists(_folder.Paths.SettingsFile));
    }

    [Fact]
    public void ValuesOutOfRange_AreBroughtBackIntoRange()
    {
        using var store = NewStore();

        store.Update(s => s with
        {
            ReminderLeadMinutes = 100_000,
            WindowWidth = 0,
            WindowHeight = -5,
            LastFolderPath = " ",
            SavedCategories = ["Work", " work ", "", "Home"]
        });

        Assert.Equal(AppSettings.MaxReminderLeadMinutes, store.Current.ReminderLeadMinutes);
        Assert.Equal(AppSettings.DefaultWindowWidth, store.Current.WindowWidth);
        Assert.Equal(AppSettings.DefaultWindowHeight, store.Current.WindowHeight);
        Assert.Equal("\\", store.Current.LastFolderPath);
        Assert.Equal(["Work", "Home"], store.Current.SavedCategories);
    }

    [Theory]
    [InlineData(@"\Backups", @"\Backups")]
    [InlineData("\\", "\\")]
    [InlineData("", "")]
    [InlineData("Backups", "\\")]
    [InlineData(null, "\\")]
    public void TheLastFolder_IsAFolderPath_OrEmptyForTheRootFolder_AndAnythingElseMeansAllTasks(string? stored, string expected)
    {
        using var store = NewStore();

        store.Update(s => s with { LastFolderPath = stored! });

        Assert.Equal(expected, store.Current.LastFolderPath);
    }

    [Fact]
    public void ExportThenImport_CarriesThePreferencesOver_ButNotTheWindowOfTheOtherMachine()
    {
        string exported = _folder.File("exported.json");
        using (var source = NewStore())
        {
            source.Update(s => s with { Theme = AppTheme.Light, ConfirmDelete = false, WindowWidth = 2000, LastFolderPath = @"\Elsewhere" });
            source.Export(exported);
        }
        File.Delete(_folder.Paths.SettingsFile);

        using var target = NewStore();
        target.Update(s => s with { WindowWidth = 1024 });
        target.Import(exported);

        Assert.Equal(AppTheme.Light, target.Current.Theme);
        Assert.False(target.Current.ConfirmDelete);
        Assert.Equal(1024, target.Current.WindowWidth);
        Assert.Equal("\\", target.Current.LastFolderPath);

        using var reopened = NewStore();
        Assert.Equal(AppTheme.Light, reopened.Current.Theme);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"name\": \"something else entirely\" }")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{ \"ConfirmDelete\": \"maybe\" }")]
    public void ImportingAFileThatIsNotSettings_IsRefused_AndChangesNothing(string content)
    {
        string file = _folder.File("other.json");
        File.WriteAllText(file, content);
        using var store = NewStore();
        store.Update(s => s with { ConfirmDelete = false });
        bool raised = false;
        store.Changed += (_, _) => raised = true;

        Assert.Throws<InvalidDataException>(() => store.Import(file));

        Assert.False(store.Current.ConfirmDelete);
        Assert.False(raised);
    }
}
