using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Settings;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// Shows the app's notifications as Windows toasts. This goes through the Windows Community
/// Toolkit rather than the Windows App SDK, whose notifications do not work in an elevated
/// process, which is how this app usually runs.
/// </summary>
internal sealed class ToastNotifier(ISettingsStore settings) : INotifier
{
    /// <summary>The user clicked one of the notifications. Raised on a thread of the notification platform.</summary>
    public event Action? Activated;

    public void Start() => ToastNotificationManagerCompat.OnActivated += _ => Activated?.Invoke();

    /// <summary>Removes what showing notifications registered in Windows for this app. For the uninstaller.</summary>
    public static void RemoveRegistration() => ToastNotificationManagerCompat.Uninstall();

    public void TaskStarted(string taskName) =>
        Show(Loc.Format("Toast.TaskStarted.Title", taskName), Loc.Get("Toast.TaskStarted.Body"));

    public void TaskCouldNotStart(string taskName, string reason) =>
        Show(Loc.Format("Toast.TaskNotStarted.Title", taskName), reason);

    public void RunSuppressed(string taskName) =>
        Show(Loc.Format("Snooze.Toast.Suppressed.Title", taskName), Loc.Get("Snooze.Toast.Suppressed.Body"));

    public void SnoozeStarted(string status) => Show(Loc.Get("Snooze.Toast.Started.Title"), status);

    public void SnoozeEnded() => Show(Loc.Get("Snooze.Toast.Ended.Title"), Loc.Get("Snooze.Toast.Ended.Body"));

    public void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess) =>
        Show(Loc.Format("Pipeline.Toast.Title", startedTask),
             Loc.Format(afterSuccess ? "Pipeline.Toast.OnSuccess" : "Pipeline.Toast.OnFailure", finishedTask));

    public void TaskRunsSoon(string taskName, int minutes) =>
        Show(Loc.Format("Toast.Upcoming.Title", taskName),
             minutes <= 1 ? Loc.Get("Toast.Upcoming.Soon") : Loc.Format("Toast.Upcoming.Minutes", minutes));

    // Shown even with notifications switched off: it is told once, and it explains where the app went.
    public void HiddenToTray() => Show(Loc.Get("Toast.HiddenToTray.Title"), Loc.Get("Toast.HiddenToTray.Body"), evenIfSwitchedOff: true);

    private void Show(string title, string text, bool evenIfSwitchedOff = false)
    {
        if (!settings.Current.ShowNotifications && !evenIfSwitchedOff) return;
        try
        {
            new ToastContentBuilder().AddText(title).AddText(text).Show();
        }
        catch (Exception ex)
        {
            // Whether a notification can be shown depends on Windows settings and services this
            // app does not control, and what it reports has happened either way.
            Serilog.Log.Warning(ex, "The notification '{Title}' could not be shown", title);
        }
    }
}
