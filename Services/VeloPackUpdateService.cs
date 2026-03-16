using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace FluentTaskScheduler.Services
{
    public static class VeloPackUpdateService
    {
        private const string GitHubRepoUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler";

        private static UpdateManager? _updateManager;

        private static UpdateManager GetManager()
        {
            _updateManager ??= new UpdateManager(new GithubSource(GitHubRepoUrl, null, false));
            return _updateManager;
        }

        /// <summary>
        /// Returns the current installed app version via VeloPack, or null if not installed via VeloPack.
        /// </summary>
        public static string? GetCurrentVersion()
        {
            try
            {
                var mgr = GetManager();
                return mgr.IsInstalled ? mgr.CurrentVersion?.ToString() : null;
            }
            catch (Exception ex)
            {
                LogService.Info($"[VeloPackUpdate] Could not get current version: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks for updates and returns the new version info, or null if up-to-date or not installed via VeloPack.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var mgr = GetManager();
                if (!mgr.IsInstalled)
                {
                    LogService.Info("[VeloPackUpdate] App is not installed via VeloPack, skipping update check.");
                    return null;
                }

                var updateInfo = await mgr.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    LogService.Info($"[VeloPackUpdate] Update available: {updateInfo.TargetFullRelease.Version}");
                }
                else
                {
                    LogService.Info("[VeloPackUpdate] App is up-to-date.");
                }

                return updateInfo;
            }
            catch (Exception ex)
            {
                LogService.Info($"[VeloPackUpdate] Update check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads and applies the update, then optionally restarts the app.
        /// </summary>
        public static async Task<bool> DownloadAndApplyAsync(UpdateInfo updateInfo, Action<int>? progressCallback = null)
        {
            try
            {
                var mgr = GetManager();

                await mgr.DownloadUpdatesAsync(updateInfo, progress => progressCallback?.Invoke(progress));

                LogService.Info("[VeloPackUpdate] Update downloaded successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Info($"[VeloPackUpdate] Download/apply failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Applies a previously downloaded update and restarts the application.
        /// </summary>
        public static void ApplyAndRestart(UpdateInfo updateInfo)
        {
            try
            {
                var mgr = GetManager();
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                LogService.Info($"[VeloPackUpdate] Restart failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Convenience method: checks, downloads, and prompts for restart.
        /// Returns true if an update was downloaded and is ready to install.
        /// </summary>
        public static async Task<(bool UpdateReady, UpdateInfo? Info, string? NewVersion)> CheckAndDownloadAsync(Action<int>? progressCallback = null)
        {
            var updateInfo = await CheckForUpdatesAsync();
            if (updateInfo == null)
                return (false, null, null);

            string newVersion = updateInfo.TargetFullRelease.Version.ToString();
            bool downloaded = await DownloadAndApplyAsync(updateInfo, progressCallback);

            return (downloaded, updateInfo, newVersion);
        }
    }
}
