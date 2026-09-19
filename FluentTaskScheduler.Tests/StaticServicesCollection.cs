namespace FluentTaskScheduler.Tests
{
    // SettingsService and SnoozeService are static (process-wide) singletons. Any test class that
    // touches them must be in this collection so xunit runs them sequentially instead of in
    // parallel, where they'd stomp on each other's state.
    [CollectionDefinition("StaticServices", DisableParallelization = true)]
    public class StaticServicesCollection
    {
    }
}
