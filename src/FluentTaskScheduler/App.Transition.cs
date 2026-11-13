using System.ComponentModel;
using System.Diagnostics;
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

    // Completion actions and the execution history log read their switch from the new store.
    public void ApplyTaskPipelines(bool enable) =>
        _services!.GetRequiredService<ISettingsStore>().Update(settings => settings with { EnableTaskPipelines = enable });

    public void ApplyExecutionHistoryLog(bool enable) =>
        _services!.GetRequiredService<ISettingsStore>().Update(settings => settings with { EnableExecutionHistoryLog = enable });

    public void OpenExecutionHistoryFolder()
    {
        string folder = _services!.GetRequiredService<AppPaths>().ExecutionHistoryFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Serilog.Log.Warning(ex, "The execution history folder {Folder} could not be opened", folder);
        }
    }

    // Stand-in until the reworked snooze service is in.
    private static void AddBridges(IServiceCollection services) =>
        services.AddSingleton<Scheduling.ISnoozeGate, NoSnooze>();

    internal sealed class NoSnooze : Scheduling.ISnoozeGate
    {
        public bool BlocksRun(string taskPath) => false;
        public void RecordSuppressedRun(string taskPath, Scheduling.RunOrigin origin) { }
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
