using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Snoozing;

/// <summary>
/// What every snooze test starts from: an empty data folder, no tasks, default settings, and a
/// clock that stands at <see cref="Start"/> until the test moves it.
/// </summary>
public abstract class SnoozeTestBase : IDisposable
{
    /// <summary>A Tuesday morning. Local time is UTC unless a test sets a time zone.</summary>
    protected static readonly DateTimeOffset Start = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly List<IDisposable> _created = [];
    private SettingsStore? _settings;

    protected TempFolder Folder { get; } = new();
    protected AdjustableClock Time { get; } = new(Start);
    protected FakeTaskStore Store { get; } = new();
    protected RecordingNotifier Notifier { get; } = new();
    protected FakeWindowsSession Session { get; } = new();

    /// <summary>Created on first use, so that a test can put a settings file in place before it is read.</summary>
    protected SettingsStore Settings => _settings ??= new SettingsStore(Folder.Paths, Time);

    protected string StateFile => Folder.File(SnoozeService.StateFileName);

    public void Dispose()
    {
        foreach (IDisposable created in _created) created.Dispose();
        _settings?.Dispose();
        Folder.Dispose();
    }

    /// <summary>
    /// The service as a start of the app creates it, not yet started. To restart the app, dispose
    /// the service that is running and create a new one.
    /// </summary>
    protected SnoozeService NewService() => NewService(Folder, Store);

    protected SnoozeService NewService(TempFolder folder, FakeTaskStore store) =>
        DisposedWithTheTest(new SnoozeService(store, Settings, Notifier, Session, folder.Paths, Time));

    protected T DisposedWithTheTest<T>(T created) where T : IDisposable
    {
        _created.Add(created);
        return created;
    }

    /// <summary>A service that has done its startup recovery.</summary>
    protected async Task<SnoozeService> StartedService()
    {
        SnoozeService service = NewService();
        await service.StartAsync();
        return service;
    }

    protected void SuspendTriggers(bool includeMicrosoftTasks = false) => Settings.Update(settings => settings with
    {
        SnoozeSuspendsScheduledTasks = true,
        SnoozeIncludeMicrosoftTasks = includeMicrosoftTasks
    });

    protected static SnoozeEnd After(double minutes) => SnoozeEnd.After(TimeSpan.FromMinutes(minutes));

    protected void AdvanceMinutes(double minutes) => Time.Advance(TimeSpan.FromMinutes(minutes));

    /// <summary>
    /// What is left when the app dies: the data folder and the tasks as they were at the moment
    /// Task Scheduler was asked for its <paramref name="number"/>-th change from now on. A
    /// service started on that shows what the next start of the app would do.
    /// </summary>
    protected Aftermath DieAtChange(int number)
    {
        var aftermath = new Aftermath();
        DisposedWithTheTest(aftermath.Folder);

        int changes = 0;
        Store.Before = (_, _) =>
        {
            if (++changes != number) return;
            foreach (string file in Directory.GetFiles(Folder.Path)) File.Copy(file, aftermath.Folder.File(Path.GetFileName(file)));
            foreach (string path in Store.Paths) aftermath.Store.Add(path, Store.IsEnabled(path));
        };
        return aftermath;
    }

    /// <summary>Waits for a change that a timer started in the background. The time limit only keeps a failing test from hanging.</summary>
    protected static async Task Eventually(ISnoozeService snooze, Func<SnoozeState, bool> condition)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (condition(snooze.State)) reached.TrySetResult();
        }

        snooze.Changed += Check;
        try
        {
            Check(null, EventArgs.Empty);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            snooze.Changed -= Check;
        }
    }

    protected sealed class Aftermath
    {
        public TempFolder Folder { get; } = new();
        public FakeTaskStore Store { get; } = new();
    }
}
