using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.Updates;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class StartupFlowTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly SettingsStore _settings;
    private readonly AppInfo _app = new(new Version(2, 0, 0));
    private readonly FakeReleaseNotes _notes = new();
    private readonly FakeUpdater _updater = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly UpdateService _updates;
    private readonly RecordingWelcomeDialogs _dialogs = new();
    private readonly ScriptedPrompts _prompts = new() { Confirm = false };

    public StartupFlowTests()
    {
        _settings = new SettingsStore(_folder.Paths);
        _updates = new UpdateService(_updater, _lifetime);
    }

    public void Dispose()
    {
        _settings.Dispose();
        _folder.Dispose();
    }

    /// <summary>The flow of one more window; the windows of an app share everything but their dialogs.</summary>
    private StartupFlow NewFlow(RecordingWelcomeDialogs? dialogs = null) =>
        new(_settings, _app, _notes, _updates, dialogs ?? _dialogs, _prompts);

    private void HasUsedTheAppBefore(string lastSeenVersion) =>
        _settings.Update(s => s with { HasCompletedOnboarding = true, LastSeenVersion = lastSeenVersion });

    [Fact]
    public async Task AtTheVeryFirstStart_TheTourShows_AndNoReleaseNotes()
    {
        _notes.Add("2.0.0");

        await NewFlow().RunAsync();

        Assert.Equal(["Tour"], _dialogs.Shown);
        Assert.Empty(_notes.Asked);
        Assert.True(_settings.Current.HasCompletedOnboarding);
        Assert.Equal("2.0.0", _settings.Current.LastSeenVersion);
    }

    [Fact]
    public async Task AtTheNextStart_NothingShows()
    {
        _notes.Add("2.0.0");
        await NewFlow().RunAsync();
        _dialogs.Shown.Clear();

        await NewFlow().RunAsync();

        Assert.Empty(_dialogs.Shown);
        Assert.Empty(_notes.Asked);
    }

    [Theory]
    [InlineData("1.9.0")]
    [InlineData("V1.9.0")]   // version 1.9 stored the tag of the release
    [InlineData("")]         // used the app before, but never saw any notes
    public async Task AfterAnUpdate_TheNotesOfTheRunningVersionShow_Once(string lastSeenVersion)
    {
        HasUsedTheAppBefore(lastSeenVersion);
        _notes.Add("2.0.0").Add("2.1.0");

        await NewFlow().RunAsync();
        await NewFlow().RunAsync();

        Assert.Equal(["ReleaseNotes 2.0.0"], _dialogs.Shown);
        Assert.Equal(["2.0.0"], _notes.Asked);
        Assert.Equal("2.0.0", _settings.Current.LastSeenVersion);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("V2.0.0")]
    [InlineData("2.1.0")]     // went back to an older build
    [InlineData("999.0.0")]
    public async Task WithoutAnUpdate_NoNotesAreAskedFor(string lastSeenVersion)
    {
        HasUsedTheAppBefore(lastSeenVersion);
        _notes.Add("2.0.0");

        await NewFlow().RunAsync();

        Assert.Empty(_dialogs.Shown);
        Assert.Empty(_notes.Asked);
        Assert.Equal(lastSeenVersion, _settings.Current.LastSeenVersion);
    }

    [Fact]
    public async Task WhenTheNotesCannotBeHad_NothingShows_AndTheNextStartTriesAgain()
    {
        HasUsedTheAppBefore("1.9.0");

        await NewFlow().RunAsync();

        Assert.Empty(_dialogs.Shown);
        Assert.Equal("1.9.0", _settings.Current.LastSeenVersion);

        _notes.Add("2.0.0");
        await NewFlow().RunAsync();

        Assert.Equal(["ReleaseNotes 2.0.0"], _dialogs.Shown);
    }

    [Fact]
    public async Task ASecondWindowOpenedWhileTheNotesAreFetched_DoesNotShowThemAsWell()
    {
        HasUsedTheAppBefore("1.9.0");
        _notes.Add("2.0.0");
        var secondWindow = new RecordingWelcomeDialogs();
        var arrivalForFirst = new TaskCompletionSource();
        var arrivalForSecond = new TaskCompletionSource();

        // Each window asks for the notes itself; a request waits for what was held up when it was made.
        _notes.Hold = arrivalForFirst;
        Task first = NewFlow().RunAsync();
        _notes.Hold = arrivalForSecond;
        Task second = NewFlow(secondWindow).RunAsync();

        arrivalForFirst.SetResult();
        await first;
        arrivalForSecond.SetResult();
        await second;

        Assert.Equal(["ReleaseNotes 2.0.0"], _dialogs.Shown);
        Assert.Empty(secondWindow.Shown);
    }

    [Fact]
    public async Task AWindowThatIsGoneByTheTimeTheNotesArrive_ShowsNothing_AndTheNotesStayUnseen()
    {
        HasUsedTheAppBefore("1.9.0");
        var arrival = new TaskCompletionSource();
        _notes.Add("2.0.0").Hold = arrival;
        StartupFlow flow = NewFlow();

        Task running = flow.RunAsync();
        flow.Dispose();
        arrival.SetResult();
        await running;

        Assert.Empty(_dialogs.Shown);
        Assert.Equal("1.9.0", _settings.Current.LastSeenVersion);
    }

    [Fact]
    public async Task AnUpdateFoundAfterStart_IsOfferedOnce_HoweverManyWindowsOpen()
    {
        HasUsedTheAppBefore("2.0.0");
        _updater.Available = "2.1.0";

        await NewFlow().RunAsync();
        await NewFlow().RunAsync();

        Assert.Single(_prompts.Shown);
        Assert.StartsWith("Dialog.UpdateAvailable.Title", _prompts.Shown[0]);
        Assert.Equal(1, _updater.Checks);
        Assert.False(_lifetime.ExitRequested);
    }

    [Fact]
    public async Task AgreeingToTheOffer_RestartsIntoTheUpdate()
    {
        HasUsedTheAppBefore("2.0.0");
        _updater.Available = "2.1.0";
        _prompts.Confirm = true;

        await NewFlow().RunAsync();

        Assert.Equal(1, _updater.InstallerStarts);
        Assert.True(_lifetime.ExitRequested);
    }

    [Theory]
    [InlineData(true, null)]     // up to date
    [InlineData(false, null)]    // a copy that cannot update itself
    [InlineData(true, "offline")]
    public async Task WithoutAnUpdateToInstall_NothingIsAsked(bool canUpdate, string? failure)
    {
        HasUsedTheAppBefore("2.0.0");
        _updater.CanUpdate = canUpdate;
        _updater.Failure = failure is null ? null : new HttpRequestException(failure);

        await NewFlow().RunAsync();

        Assert.Empty(_prompts.Shown);
        Assert.Empty(_dialogs.Shown);
    }

    [Fact]
    public async Task AWindowThatIsGoneByTheTimeTheUpdateIsReady_DoesNotAsk()
    {
        HasUsedTheAppBefore("2.0.0");
        _updater.Available = "2.1.0";
        var download = new TaskCompletionSource();
        _updater.Hold = download;
        StartupFlow flow = NewFlow();

        Task running = flow.RunAsync();
        flow.Dispose();
        download.SetResult();
        await running;

        Assert.Empty(_prompts.Shown);
    }
}
