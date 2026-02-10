using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FluentTaskScheduler.Services
{
    public static class NotificationService
    {
        public static void ShowTaskStarted(string taskName)
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddText($"Task Started: {taskName}")
                .AddText("The task has been triggered manually.")
                .Show();
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!SettingsService.ShowNotifications) return;

            new ToastContentBuilder()
                .AddText($"Task Failed: {taskName}")
                .AddText(error)
                .Show();
        }
    }
}
