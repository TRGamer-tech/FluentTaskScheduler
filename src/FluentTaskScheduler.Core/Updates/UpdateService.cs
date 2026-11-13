using FluentTaskScheduler.Localization;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Updates;

/// <summary>
/// What the update library does for this copy of the app. The library reports failure with
/// exceptions of every kind, plain <see cref="Exception"/> included, and so may these methods.
/// </summary>
public interface IUpdater
{
    /// <summary>
    /// False when this copy cannot update itself: it was not set up by the installer, or its
    /// folder cannot be written by the current user.
    /// </summary>
    bool CanUpdate { get; }

    /// <summary>Looks for a newer release and downloads it.</summary>
    /// <returns>The version that is now ready to be installed; null when the app is the newest.</returns>
    Task<string?> DownloadLatestAsync();

    /// <summary>
    /// Starts the installer for the downloaded release. It waits for this process to end, installs
    /// the release and starts the app again.
    /// </summary>
    Task StartInstallerAsync();
}

public enum UpdateStatus
{
    /// <summary>The app is the newest release.</summary>
    UpToDate,

    /// <summary>A newer release has been downloaded; a restart installs it.</summary>
    Ready,

    /// <summary>This copy of the app cannot update itself.</summary>
    Unavailable,

    /// <summary>Looking for the release or downloading it failed.</summary>
    Failed
}

/// <param name="Version">The release that is ready, when one is.</param>
/// <param name="Error">What went wrong, when the check failed.</param>
public sealed record UpdateCheck(UpdateStatus Status, string Version = "", string Error = "");

/// <summary>Keeps the app up to date: finds and downloads newer releases, and installs one when the user agrees to restart.</summary>
public sealed class UpdateService(IUpdater updater, IAppLifetime lifetime)
{
    private readonly object _gate = new();
    private Task<UpdateCheck>? _check;
    private bool _checkedAfterStart;

    /// <summary>
    /// Looks for a newer release and downloads it. A call made while a check is under way gets the
    /// result of that check. Failures come back as a result, never as an exception.
    /// </summary>
    public Task<UpdateCheck> CheckAsync()
    {
        lock (_gate)
        {
            if (_check is not { IsCompleted: false }) _check = RunCheckAsync();
            return _check;
        }
    }

    /// <summary>
    /// The check that is made once, when the app has started. Its result goes to the first caller
    /// only, so that what it finds is brought up once however many windows are opened; every
    /// later caller gets null.
    /// </summary>
    public async Task<UpdateCheck?> CheckAfterStartAsync()
    {
        lock (_gate)
        {
            if (_checkedAfterStart) return null;
            _checkedAfterStart = true;
        }
        return await CheckAsync();
    }

    /// <summary>Asks whether to restart now so that the downloaded <paramref name="version"/> gets installed, and does it if the user agrees.</summary>
    public async Task OfferRestartAsync(IPrompts prompts, string version)
    {
        bool restart = await prompts.ConfirmAsync(
            Loc.Get("Dialog.UpdateAvailable.Title"),
            Loc.Format("Dialog.UpdateAvailable.ContentFormat", version),
            Loc.Get("Dialog.UpdateAvailable.RestartNow"));
        if (!restart) return;

        try
        {
            await updater.StartInstallerAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "The installer for update {Version} could not be started", version);
            await prompts.ShowErrorAsync(Loc.Get("Settings.UpdateApplyFailed.Content"));
            return;
        }

        // The installer is waiting for this process. Leaving the regular way lets everything save and stop first.
        lifetime.Exit();
    }

    private async Task<UpdateCheck> RunCheckAsync()
    {
        if (!updater.CanUpdate) return new UpdateCheck(UpdateStatus.Unavailable);

        try
        {
            // On a pool thread: the library reads its package folder before it first awaits anything.
            string? version = await Task.Run(updater.DownloadLatestAsync);
            if (version is null) return new UpdateCheck(UpdateStatus.UpToDate);

            Serilog.Log.Information("Update {Version} has been downloaded", version);
            return new UpdateCheck(UpdateStatus.Ready, version);
        }
        catch (Exception ex)
        {
            // Being offline when the app starts is the usual reason, hence no more than a warning.
            Serilog.Log.Warning(ex, "The update check failed");
            return new UpdateCheck(UpdateStatus.Failed, Error: ex.Message);
        }
    }
}
