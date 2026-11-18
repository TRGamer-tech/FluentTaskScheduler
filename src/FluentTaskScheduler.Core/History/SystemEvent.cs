using System.Globalization;

namespace FluentTaskScheduler.History;

/// <summary>Something that happened to the computer or its users and can explain why a task ran oddly or not at all.</summary>
public enum SystemEventKind
{
    /// <summary>Windows started.</summary>
    Startup,

    /// <summary>Windows shut down in an orderly way.</summary>
    Shutdown,

    /// <summary>A user or a program asked for a shutdown or restart.</summary>
    ShutdownRequested,

    /// <summary>Windows noticed, on starting, that it had not been shut down properly: power loss, crash or reset.</summary>
    UnexpectedShutdown,

    /// <summary>The computer went to sleep or into standby.</summary>
    Sleep,

    /// <summary>The computer woke up again.</summary>
    Resume,

    /// <summary>A user logged on.</summary>
    Logon,

    /// <summary>A user logged off.</summary>
    Logoff
}

/// <summary>An event of the Windows System log that the app shows next to a task's history.</summary>
public sealed record SystemEvent(SystemEventKind Kind, DateTimeOffset Time)
{
    /// <summary>The Windows log these events are read from.</summary>
    internal const string Log = "System";

    // An event ID only identifies an event together with its source: 7001 from Winlogon is a
    // logon, 7001 from the Service Control Manager is a service that failed to start.
    private static readonly (string Provider, int Id, SystemEventKind Kind)[] Known =
    [
        ("EventLog", 6005, SystemEventKind.Startup),
        ("EventLog", 6006, SystemEventKind.Shutdown),
        ("EventLog", 6008, SystemEventKind.UnexpectedShutdown),
        ("User32", 1074, SystemEventKind.ShutdownRequested),
        ("Microsoft-Windows-Kernel-Power", 41, SystemEventKind.UnexpectedShutdown),
        ("Microsoft-Windows-Kernel-Power", 42, SystemEventKind.Sleep),
        ("Microsoft-Windows-Kernel-Power", 107, SystemEventKind.Resume),
        ("Microsoft-Windows-Kernel-Power", 506, SystemEventKind.Sleep),
        ("Microsoft-Windows-Kernel-Power", 507, SystemEventKind.Resume),
        ("Microsoft-Windows-Winlogon", 7001, SystemEventKind.Logon),
        ("Microsoft-Windows-Winlogon", 7002, SystemEventKind.Logoff)
    ];

    /// <summary>Reads the event out of a record of the System log. Null for a record the app has no use for.</summary>
    public static SystemEvent? From(LogRecord record)
    {
        foreach (var (provider, id, kind) in Known)
        {
            if (record.Id == id && record.Provider == provider) return new SystemEvent(kind, record.Time);
        }
        return null;
    }

    /// <summary>
    /// Selects the candidates within a period. The query names sources and IDs separately, which
    /// also lets through pairs that mean something else; <see cref="From"/> sorts those out.
    /// </summary>
    internal static string Query(DateTimeOffset from, DateTimeOffset to)
    {
        string providers = string.Join(" or ", Known.Select(known => known.Provider).Distinct().Select(provider => $"@Name='{provider}'"));
        string ids = string.Join(" or ", Known.Select(known => known.Id).Distinct().Order().Select(id => $"EventID={id.ToString(CultureInfo.InvariantCulture)}"));
        return $"*[System[Provider[{providers}] and ({ids}) and TimeCreated[@SystemTime>='{TaskSchedulerLog.Format(from)}' and @SystemTime<='{TaskSchedulerLog.Format(to)}']]]";
    }
}
