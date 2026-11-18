using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.Updates;

namespace FluentTaskScheduler.Tests.Updates;

public class UpdateServiceTests
{
    private readonly FakeUpdater _updater = new();
    private readonly FakeAppLifetime _app = new();
    private readonly ScriptedPrompts _prompts = new();

    private UpdateService NewService() => new(_updater, _app);

    private static UpdateCheck Ready => new(UpdateStatus.Ready, "2.0.0");

    [Fact]
    public async Task WithNothingNewer_TheAppIsUpToDate()
    {
        Assert.Equal(new UpdateCheck(UpdateStatus.UpToDate), await NewService().CheckAsync());
    }

    [Fact]
    public async Task ANewerRelease_IsDownloaded_AndReportedReady()
    {
        _updater.Available = "2.0.0";

        Assert.Equal(new UpdateCheck(UpdateStatus.Ready, "2.0.0"), await NewService().CheckAsync());
    }

    [Fact]
    public async Task ACopyThatCannotUpdateItself_SaysSo_WithoutLooking()
    {
        _updater.CanUpdate = false;

        Assert.Equal(new UpdateCheck(UpdateStatus.Unavailable), await NewService().CheckAsync());
        Assert.Equal(0, _updater.Checks);
    }

    [Fact]
    public async Task AFailure_ComesBackAsAResult_AndTheNextCheckTriesAgain()
    {
        UpdateService service = NewService();
        _updater.Failure = new HttpRequestException("No such host is known.");

        Assert.Equal(new UpdateCheck(UpdateStatus.Failed, Error: "No such host is known."), await service.CheckAsync());

        _updater.Failure = null;
        _updater.Available = "2.0.0";

        Assert.Equal(UpdateStatus.Ready, (await service.CheckAsync()).Status);
        Assert.Equal(2, _updater.Checks);
    }

    [Fact]
    public async Task ACheckAskedForWhileOneIsRunning_JoinsIt()
    {
        UpdateService service = NewService();
        var download = new TaskCompletionSource();
        _updater.Hold = download;
        _updater.Available = "2.0.0";

        Task<UpdateCheck> first = service.CheckAsync();
        Task<UpdateCheck> second = service.CheckAsync();
        download.SetResult();

        Assert.Equal(await first, await second);
        Assert.Equal(1, _updater.Checks);
    }

    [Fact]
    public async Task TheCheckAfterStart_IsMadeOnce_AndEveryCallerGetsItsResult()
    {
        UpdateService service = NewService();
        _updater.Available = "2.0.0";

        UpdateCheck first = await service.CheckAfterStartAsync();
        UpdateCheck second = await service.CheckAfterStartAsync();

        Assert.Equal(new UpdateCheck(UpdateStatus.Ready, "2.0.0"), first);
        Assert.Equal(first, second);
        Assert.Equal(1, _updater.Checks);
    }

    [Fact]
    public async Task WhatOneCheckFound_IsOfferedOnce_HoweverManyGotThatCheck()
    {
        UpdateService service = NewService();
        _updater.Available = "2.0.0";
        var download = new TaskCompletionSource();
        _updater.Hold = download;

        Task<UpdateCheck> afterStart = service.CheckAfterStartAsync();
        Task<UpdateCheck> byHand = service.CheckAsync();
        download.SetResult();
        await service.OfferRestartAsync(_prompts, await afterStart);
        await service.OfferRestartAsync(_prompts, await byHand);
        await service.OfferRestartAsync(_prompts, await service.CheckAfterStartAsync());

        Assert.Single(_prompts.Shown);
        Assert.Equal(1, _updater.InstallerStarts);
    }

    [Fact]
    public async Task ANewCheck_OffersAgainWhatAnEarlierOneFound()
    {
        UpdateService service = NewService();
        _updater.Available = "2.0.0";
        _prompts.Confirm = false;

        await service.OfferRestartAsync(_prompts, await service.CheckAfterStartAsync());
        await service.OfferRestartAsync(_prompts, await service.CheckAsync());

        Assert.Equal(2, _prompts.Shown.Count);
        Assert.Equal(2, _updater.Checks);
    }

    [Fact]
    public async Task AgreeingToRestart_StartsTheInstaller_ThenExitsTheAppTheRegularWay()
    {
        await NewService().OfferRestartAsync(_prompts, Ready);

        Assert.Equal(["Dialog.UpdateAvailable.Title: Dialog.UpdateAvailable.ContentFormat [Dialog.UpdateAvailable.RestartNow] [Dialog.Common.Later]"], _prompts.Shown);
        Assert.Equal(1, _updater.InstallerStarts);
        Assert.True(_app.ExitRequested);
    }

    [Fact]
    public async Task DecliningToRestart_ChangesNothing()
    {
        _prompts.Confirm = false;

        await NewService().OfferRestartAsync(_prompts, Ready);

        Assert.Equal(0, _updater.InstallerStarts);
        Assert.False(_app.ExitRequested);
    }

    [Fact]
    public async Task WhenTheInstallerCannotBeStarted_TheUserIsTold_AndTheAppKeepsRunning()
    {
        _updater.Failure = new Exception("Update.exe process exited too soon (1).");

        await NewService().OfferRestartAsync(_prompts, Ready);

        Assert.Equal("Error: Settings.UpdateApplyFailed.Content", _prompts.Shown.Last());
        Assert.False(_app.ExitRequested);
    }
}
