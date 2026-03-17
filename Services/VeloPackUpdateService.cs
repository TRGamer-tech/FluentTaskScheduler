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
        /// Throws exceptions on failure to allow caller handling.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
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

        /// <summary>
        /// Downloads and applies the update, then optionally restarts the app.
        /// </summary>
        public static async Task<bool> DownloadAndApplyAsync(UpdateInfo updateInfo, Action<int>? progressCallback = null)
        {
            var mgr = GetManager();

            await mgr.DownloadUpdatesAsync(updateInfo, progress => progressCallback?.Invoke(progress));

            LogService.Info("[VeloPackUpdate] Update downloaded successfully.");
            return true;
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

        public enum UpdateResultStatus
        {
            NoUpdate,
            UpdateReady,
            Error
        }

        public record UpdateCheckResult(UpdateResultStatus Status, UpdateInfo? Info = null, string? NewVersion = null, string? ErrorMessage = null);

        /// <summary>
        /// Convenience method: checks, downloads, and prompts for restart.
        /// Returns a result object containing status and metadata.
        /// </summary>
        public static async Task<UpdateCheckResult> CheckAndDownloadAsync(Action<int>? progressCallback = null)
        {
            try
            {
                var updateInfo = await CheckForUpdatesAsync();
                if (updateInfo == null)
                    return new UpdateCheckResult(UpdateResultStatus.NoUpdate);

                string newVersion = updateInfo.TargetFullRelease.Version.ToString();
                bool downloaded = await DownloadAndApplyAsync(updateInfo, progressCallback);

                if (downloaded)
                    return new UpdateCheckResult(UpdateResultStatus.UpdateReady, updateInfo, newVersion);
                else
                    return new UpdateCheckResult(UpdateResultStatus.Error, ErrorMessage: "Download failed.");
            }
            catch (Exception ex)
            {
                LogService.Info($"[VeloPackUpdate] Update process failed: {ex.Message}");
                string message = ex.Message;
                if (message.Contains("access to the path", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Access denied. If this application is installed in Program Files (Machine-Wide), updates may require administrator privileges or the application might be in a restricted environment.";
                }
                return new UpdateCheckResult(UpdateResultStatus.Error, ErrorMessage: message);
            }
        }
    }
}
