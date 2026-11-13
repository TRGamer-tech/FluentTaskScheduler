namespace FluentTaskScheduler.Notifications;

/// <summary>Shows nothing. For the command line, which answers in text on the console instead.</summary>
public sealed class SilentNotifier : INotifier
{
    public void TaskStarted(string taskName) { }
    public void TaskCouldNotStart(string taskName, string reason) { }
    public void RunSuppressed(string taskName) { }
    public void SnoozeStarted(string status) { }
    public void SnoozeEnded() { }
    public void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess) { }
    public void TaskRunsSoon(string taskName, int minutes) { }
    public void HiddenToTray() { }
}
