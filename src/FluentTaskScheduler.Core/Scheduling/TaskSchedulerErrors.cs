namespace FluentTaskScheduler.Scheduling;

internal static class TaskSchedulerErrors
{
    /// <summary>Win32 E_ACCESSDENIED (0x80070005), which is what Task Scheduler's COM layer
    /// raises for a protected/system task regardless of the OS display language.</summary>
    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);

    /// <summary>
    /// Checks the HRESULT, inner exceptions included, rather than the message, which is written
    /// in the display language of Windows.
    /// </summary>
    internal static bool IsAccessDenied(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.HResult == E_ACCESSDENIED) return true;
        }
        return false;
    }
}
