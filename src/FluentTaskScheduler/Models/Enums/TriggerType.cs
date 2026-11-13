namespace FluentTaskScheduler.Models.Enums
{
    /// <summary>
    /// The set of Windows Task Scheduler trigger kinds this app understands.
    /// Members are named to match the existing localization keys
    /// (e.g. "Dialog.Trigger." + TriggerType) exactly.
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
}
