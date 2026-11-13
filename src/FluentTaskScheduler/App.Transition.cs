using FluentTaskScheduler.Services;
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

        LocalizationService.Initialize();
    }
}
