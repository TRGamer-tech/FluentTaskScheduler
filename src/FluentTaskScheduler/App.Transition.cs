using FluentTaskScheduler.Services;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Shell;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler;

// Bridges for the pages and services that have not been moved to constructor injection yet.
// This file goes away with the last of them.
public partial class App
{
    public static IServiceProvider Container { get; private set; } = null!;

    public static Window? m_window => (Current as App)?._services?.GetRequiredService<WindowManager>().MainWindow;

    /// <summary>The navigator of the first window, for pages that cannot have it injected yet.</summary>
    public static IShellNavigator? Navigator =>
        (m_window?.Content as ShellPage);

    public void ApplyTheme(ElementTheme theme)
    {
        var old = Container.GetRequiredService<ISettingsService>();
        _services!.GetRequiredService<ISettingsStore>().Update(settings => settings with
        {
            Theme = theme switch { ElementTheme.Light => AppTheme.Light, ElementTheme.Dark => AppTheme.Dark, _ => AppTheme.Default },
            IsOledMode = old.IsOledMode,
            IsMicaEnabled = old.IsMicaEnabled
        });
    }

    public void ApplySmoothScrolling(bool enable) =>
        _services!.GetRequiredService<ISettingsStore>().Update(settings => settings with { SmoothScrolling = enable });

    // Stand-ins until the reworked snooze service and the notifier are in.
    private static void AddBridges(IServiceCollection services)
    {
        services.AddSingleton<Scheduling.ISnoozeGate, NoSnooze>();
        services.AddSingleton<Notifications.INotifier, LegacyNotifier>();
    }

    private sealed class NoSnooze : Scheduling.ISnoozeGate
    {
        public bool BlocksRun(string taskPath) => false;
        public void RecordSuppressedRun(string taskPath, Scheduling.RunOrigin origin) { }
    }

    private sealed class LegacyNotifier : Notifications.INotifier
    {
        public void TaskStarted(string taskName) => Try(() => NotificationService.ShowTaskStarted(taskName));
        public void TaskCouldNotStart(string taskName, string reason) => Try(() => NotificationService.ShowTaskError(taskName, reason));
        public void RunSuppressed(string taskName) { }
        public void SnoozeStarted(string status) { }
        public void SnoozeEnded() { }
        public void CompletionActionStarted(string finishedTask, string startedTask, bool afterSuccess) { }
        public void TaskRunsSoon(string taskName, int minutes) { }
        public void HiddenToTray() { }

        private static void Try(Action show)
        {
            try { show(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "A notification could not be shown"); }
        }
    }

    private static void StartLegacyServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<Services.TaskSchedulerConnection>();
        services.AddSingleton<ITaskEventLogService, TaskEventLogService>();
        services.AddSingleton<ITaskCrudService, TaskCrudService>();
        services.AddSingleton<IFolderService, FolderService>();
        services.AddSingleton<ITaskXmlImportExportService, TaskXmlImportExportService>();
        services.AddSingleton<ITaskService, TaskServiceWrapper>();
        Container = services.BuildServiceProvider();

        LoggingConfig.Initialize();
        LocalizationService.Initialize();
    }
}
