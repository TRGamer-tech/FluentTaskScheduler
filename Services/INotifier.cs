namespace FluentTaskScheduler.Services
{
    /// <summary>
    /// Desktop notifications. Implementations respect the user's notification settings and never
    /// throw: a notification that cannot be shown must not change the outcome of what it reports.
    /// </summary>
    public interface INotifier
    {
        void TaskStarted(string taskName);
        void TaskCouldNotStart(string taskName, string reason);

        /// <summary>A run was refused because tasks are snoozed.</summary>
        void RunSuppressed(string taskName);

        /// <param name="status">What is snoozed and until when, in the user's language.</param>
        void SnoozeStarted(string status);
        void SnoozeEnded();

        /// <summary>A completion action started <paramref name="startedTask"/> because <paramref name="finishedTask"/> succeeded or failed.</summary>
        void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess);

        void TaskRunsSoon(string taskName, int minutes);

        /// <summary>The window was hidden to the notification area and the app keeps running.</summary>
        void HiddenToTray();
    }
}
