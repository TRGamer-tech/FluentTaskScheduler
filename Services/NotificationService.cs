using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FluentTaskScheduler.Services
{
    public static class NotificationService
    {
        private static ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        public static void ShowTaskStarted(string taskName)
        {
            if (!Settings.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText($"Task Started: {taskName}")
                .AddText("The task has been triggered manually.")
                .Show();
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!Settings.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText($"Task Failed: {taskName}")
                .AddText(error)
                .Show();
        }
        public static void ShowUpcomingTask(string taskName, int minutesUntilRun)
        {
            if (!Settings.ShowNotifications || !Settings.EnableUpcomingReminders) return;

            string timeLabel = minutesUntilRun <= 1 ? "less than a minute" : $"{minutesUntilRun} minutes";
            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText($"Upcoming Task: {taskName}")
                .AddText($"Scheduled to run in {timeLabel}.")
                .Show();
        }

        public static void ShowSnoozeStarted(string statusText)
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText(LocalizationService.GetString("Snooze.Toast.Started.Title", "Global Snooze Enabled"))
                .AddText(statusText)
                .Show();
        }

        public static void ShowSnoozeEnded()
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText(LocalizationService.GetString("Snooze.Toast.Ended.Title", "Global Snooze Ended"))
                .AddText(LocalizationService.GetString("Snooze.Toast.Ended.Body", "Scheduled tasks are running normally again."))
                .Show();
        }

        public static void ShowRunSuppressed(string taskName)
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText(string.Format(
                    LocalizationService.GetString("Snooze.Toast.Suppressed.Title", "Run Suppressed: {0}"), taskName))
                .AddText(LocalizationService.GetString(
                    "Snooze.Toast.Suppressed.Body", "Global snooze is active, so this task was not started."))
                .Show();
        }

        public static void ShowPipelineTriggered(string sourceTask, string targetTask, bool onSuccess)
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText(string.Format(
                    LocalizationService.GetString("Pipeline.Toast.Title", "Pipeline: {0} started"), targetTask))
                .AddText(string.Format(
                    onSuccess
                        ? LocalizationService.GetString("Pipeline.Toast.OnSuccess", "Triggered because '{0}' completed successfully.")
                        : LocalizationService.GetString("Pipeline.Toast.OnFailure", "Triggered because '{0}' failed."),
                    sourceTask))
                .Show();
        }

        private static bool _trayNotificationShown = false;

        public static void ShowMinimizedToTray()
        {
            if (_trayNotificationShown) return;
            _trayNotificationShown = true;

            new ToastContentBuilder()
                .AddArgument("action", "show")
                .AddText("FluentTaskScheduler is still running")
                .AddText("The app has been minimized to the system tray. Click to restore, or double-click the tray icon.")
                .Show();
        }
    }
}
