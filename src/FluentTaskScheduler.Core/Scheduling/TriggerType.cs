namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// The set of Windows Task Scheduler trigger kinds this app understands. What each is called
/// for the user comes from <see cref="TriggerText.TypeKey"/>.
/// </summary>
public enum TriggerType
{
    Daily,
    Weekly,
    Monthly,
    AtLogon,
    AtStartup,
    Once,
    Event,
    OnIdle,
    SessionStateChange,
    Unsupported
}
