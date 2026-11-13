namespace FluentTaskScheduler.Scheduling;

/// <summary>What the editor starts from when the user creates a task from nothing.</summary>
public static class NewTask
{
    /// <summary>A task that runs one program once a day; the user fills in the rest.</summary>
    public static TaskDraft Draft(DateTime now) => new()
    {
        Author = Environment.UserName,
        Triggers = [Trigger(now)],
        Actions = [new ActionDraft()]
    };

    /// <summary>A daily trigger starting at the next full hour, so the time is not an accident of when the editor was opened.</summary>
    public static TriggerDraft Trigger(DateTime now) => new()
    {
        Type = TriggerType.Daily,
        Start = now.Date.AddHours(now.Hour + 1)
    };

    /// <summary>An action that sends an e-mail through PowerShell; the addresses and the server are placeholders to replace.</summary>
    public static ActionDraft SendEmail { get; } = new()
    {
        Path = "powershell.exe",
        Arguments = "-ExecutionPolicy Bypass -Command \"Send-MailMessage -To 'recipient@example.com' -From 'scheduler@example.com' -Subject 'Task started' -Body 'The task has started.' -SmtpServer 'smtp.example.com'\""
    };

    /// <summary>An action that shows a message box; the text is a placeholder to replace.</summary>
    public static ActionDraft ShowMessage { get; } = new()
    {
        Path = "powershell.exe",
        Arguments = "-WindowStyle Hidden -Command \"& {Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Task notification', 'FluentTaskScheduler')}\""
    };
}
