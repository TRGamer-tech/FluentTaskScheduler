using System.Runtime.InteropServices;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace FluentTaskScheduler.Updates;

/// <summary>Updates the app from the project's releases on GitHub, with Velopack.</summary>
public sealed class VelopackUpdater : IUpdater
{
    // Releases are packed once per processor architecture, each as a channel of its own.
    private static readonly string Channel = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    private readonly UpdateManager? _manager;

    public VelopackUpdater()
    {
        // Program leaves Velopack alone when the app's folder cannot be written, as in an
        // installation for all users that runs without administrator rights. Then nothing
        // describes the installation, and there is nothing to update it with.
        if (!VelopackLocator.IsCurrentSet) return;

        _manager = new UpdateManager(
            new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false),
            new UpdateOptions { ExplicitChannel = Channel });
    }

    /// <summary>Also false for a copy the installer did not set up, such as a build run from its output folder.</summary>
    public bool CanUpdate => _manager is { IsInstalled: true };

    public async Task<string?> DownloadLatestAsync()
    {
        UpdateInfo? update = await Manager.CheckForUpdatesAsync();
        if (update is null) return null;

        await Manager.DownloadUpdatesAsync(update);
        return update.TargetFullRelease.Version.ToString();
    }

    // No release is named: the installer takes the newest one that has been downloaded.
    public Task StartInstallerAsync() => Manager.WaitExitThenApplyUpdatesAsync(null, silent: false, restart: true);

    private UpdateManager Manager => _manager ?? throw new InvalidOperationException("This copy of the app cannot update itself.");
}
