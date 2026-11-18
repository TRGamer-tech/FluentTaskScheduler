using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using FluentTaskScheduler.Dialogs;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Logging;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Processes;
using FluentTaskScheduler.Reminders;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Shell;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Updates;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler;

/// <summary>The composition root: decides which services exist, starts them, and opens the first window.</summary>
public partial class App : Application, IAppLifetime, IDisposable
{
    private readonly SingleInstance _instance;
    private readonly Exception? _updaterFailure;
    private readonly DataFolderMoveReport _movedFiles;
    private ServiceProvider? _services;
    private bool _crashReportPending;

    /// <param name="updaterFailure">What went wrong in Velopack's start-up code, which runs before there is a log; null if nothing did.</param>
    /// <param name="movedFiles">What became of the files of versions before 2.0, which also come over before there is a log.</param>
    internal App(SingleInstance instance, Exception? updaterFailure, DataFolderMoveReport movedFiles)
    {
        _instance = instance;
        _updaterFailure = updaterFailure;
        _movedFiles = movedFiles;
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            // An exception in an event handler should not take the whole app down with it.
            e.Handled = true;
            ReportCrash(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Serilog.Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; the process is ending");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Serilog.Log.Error(e.Exception, "An exception in a background task was never observed");
            e.SetObserved();
        };
    }

    public bool IsElevated { get; } = RunsAsAdministrator();

    private static bool RunsAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services = ConfigureServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // The logger comes first and the settings it follows second: what goes wrong while the
        // settings are read is then on record.
        var logging = _services.GetRequiredService<AppLogging>();
        Serilog.Log.Logger = logging.Logger;
        logging.Follow(_services.GetRequiredService<ISettingsStore>());
        Serilog.Log.Information("FluentTaskScheduler {Version} started", _services.GetRequiredService<AppInfo>().VersionText);
        if (_updaterFailure is not null) Serilog.Log.Warning(_updaterFailure, "Velopack's start-up code failed; this copy may not be able to update itself");
        _movedFiles.WriteTo(Serilog.Log.Logger);

        // Before anything saves the settings: the snooze state of version 1.9 lived in the settings
        // file, and the first save in the new format drops what the snooze service has not taken over.
        var snooze = _services.GetRequiredService<SnoozeService>();

        _services.GetRequiredService<AppAppearance>().Start();

        // What acts on finished runs exists before the stream starts to report them.
        _services.GetRequiredService<CompletionActionService>();
        _services.GetRequiredService<ExecutionHistoryLog>();
        _services.GetRequiredService<TaskEventStream>().Start();

        var windows = _services.GetRequiredService<WindowManager>();
        windows.OpenWindow();

        // Starting the app again and clicking one of its notifications both ask for the window.
        var ui = _services.GetRequiredService<IUiThread>();
        _instance.OnShowRequested(() => ui.Post(windows.ShowMainWindow));

        // The uninstaller asks a running app to end before it deletes the app's files.
        _instance.OnExitRequested(() => ui.Post(Exit));
        var notifier = _services.GetRequiredService<ToastNotifier>();
        notifier.Activated += () => ui.Post(windows.ShowMainWindow);
        notifier.Start();

        _services.GetRequiredService<Tray>().Start();
        _services.GetRequiredService<ReminderService>().Start();
        _ = FinishSnoozesAsync(snooze);
    }

    // Re-enables what a snooze that ran out while the app was closed still holds disabled.
    private static async Task FinishSnoozesAsync(SnoozeService snooze)
    {
        try
        {
            await snooze.StartAsync();
        }
        catch (Exception ex)
        {
            // Nothing awaits this at startup; what could not be re-enabled stays on record and is tried again.
            Serilog.Log.Error(ex, "Finishing the snoozes of the last run failed");
        }
    }

    private ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // One per app
        services.AddSingleton<IAppLifetime>(this);
        services.AddSingleton<AppPaths>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IUiThread, UiThread>();
        services.AddSingleton<IClipboard, ClipboardService>();
        services.AddSingleton<ITaskStore, TaskStore>();
        services.AddSingleton<ITaskFolders, TaskFolders>();
        services.AddSingleton<ITaskRunner, TaskRunner>();
        services.AddSingleton<IWindowsSession, WindowsSession>();
        services.AddSingleton<SnoozeService>();
        services.AddSingleton<ISnoozeService>(app => app.GetRequiredService<SnoozeService>());
        services.AddSingleton<ISnoozeGate>(app => app.GetRequiredService<SnoozeService>());
        services.AddSingleton<ReminderService>();
        services.AddSingleton<INetworkProfiles, RegistryNetworkProfiles>();
        services.AddSingleton<IEventLog, WindowsEventLog>();
        services.AddSingleton<ITaskHistory, TaskHistory>();
        services.AddSingleton<TaskEventStream>();
        services.AddSingleton<ITaskEventStream>(app => app.GetRequiredService<TaskEventStream>());
        services.AddSingleton<CompletionActionService>();
        services.AddSingleton<ExecutionHistoryLog>();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<INotifier>(app => app.GetRequiredService<ToastNotifier>());
        services.AddSingleton<WindowManager>();
        services.AddSingleton<AppAppearance>();
        services.AddSingleton<Tray>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton(provider => new ScriptStore(
            provider.GetRequiredService<AppPaths>(), Path.Combine(AppContext.BaseDirectory, "Assets", "Scripts.json")));
        services.AddSingleton(new AppInfo(typeof(App).Assembly.GetName().Version!));
        services.AddSingleton<AppLogging>();
        services.AddSingleton<IStartWithWindows, RunKeyStartup>();
        services.AddSingleton<ILauncher, ShellLauncher>();
        services.AddSingleton<IReleaseNotesSource, GitHubReleaseNotes>();
        services.AddSingleton<IUpdater, VelopackUpdater>();
        services.AddSingleton<UpdateService>();

        // One per window
        services.AddScoped<WindowContext>();
        services.AddScoped<IDialogService, DialogService>();
        services.AddScoped<IPrompts>(window => window.GetRequiredService<IDialogService>());
        services.AddScoped<IFilePicker, FilePicker>();
        services.AddScoped<ShellPage>();
        services.AddScoped<IShellNavigator>(window => window.GetRequiredService<ShellPage>());
        services.AddScoped<TaskEditorFactory>();
        services.AddScoped<TaskDetailsFactory>();
        services.AddScoped<TasksViewModel>();
        services.AddScoped<SnoozeViewModel>();
        services.AddScoped<FoldersViewModel>();
        services.AddScoped<TasksPage>();
        services.AddScoped<DashboardViewModel>();
        services.AddScoped<DashboardPage>();
        services.AddScoped<QuickActionsViewModel>();
        services.AddScoped<QuickActionsPage>();
        services.AddScoped<ScriptEditorViewModel>();
        services.AddScoped<ScriptEditorPage>();
        services.AddScoped<LibraryViewModel>();
        services.AddScoped<LibraryPage>();
        services.AddScoped<IWelcomeDialogs, WelcomeDialogs>();
        services.AddScoped<StartupFlow>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<SettingsPage>();

        return services;
    }

    // Ends the message loop; Program then disposes the app, which saves what is pending.
    void IAppLifetime.Exit() => Exit();

    /// <summary>Whether Program is to delete the app's data once this app is disposed.</summary>
    internal bool DeletesDataOnExit { get; private set; }

    void IAppLifetime.ExitAndDeleteData()
    {
        DeletesDataOnExit = true;
        Exit();
    }

    public bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, AppArguments.AfterRestart) { UseShellExecute = true, Verb = "runas" })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the user said no at the consent prompt
        {
            return false;
        }
        Exit();
        return true;
    }

    /// <summary>Saves what is pending and stops everything that runs in the background.</summary>
    public void Dispose()
    {
        _services?.Dispose();
        Serilog.Log.CloseAndFlush();
    }

    // Must not throw: WinUI would report that as unhandled as well, and call this again.
    private void ReportCrash(Exception exception)
    {
        Serilog.Log.Fatal(exception, "Unhandled exception in the UI");

        // Errors tend to come in bursts. While one report is waiting or open, the log has to do for the others.
        if (_crashReportPending || _services?.GetService<WindowManager>()?.MainShell is not { } shell) return;
        _ = ShowCrashReportAsync(shell, exception.ToString());
    }

    private async Task ShowCrashReportAsync(ShellPage shell, string details)
    {
        _crashReportPending = true;
        try
        {
            await shell.ShowCrashReportAsync(details);
        }
        catch (Exception ex)
        {
            // Nothing awaits the report. The exception it was to show is in the log either way.
            Serilog.Log.Error(ex, "The report of an unhandled exception could not be shown");
        }
        finally
        {
            _crashReportPending = false;
        }
    }
}
