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
                .AddText($"Task Started: {taskName}")
                .AddText("The task has been triggered manually.")
                .Show();
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!Settings.ShowNotifications) return;

            new ToastContentBuilder()
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
