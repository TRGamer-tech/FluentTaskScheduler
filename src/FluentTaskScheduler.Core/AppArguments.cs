namespace FluentTaskScheduler;

/// <summary>Switches the app starts itself with. They are meant for the window, not for the command line.</summary>
public static class AppArguments
{
    /// <summary>Given to a restarted instance, so it waits for the one that started it to end.</summary>
    public const string AfterRestart = "--after-restart";
}
