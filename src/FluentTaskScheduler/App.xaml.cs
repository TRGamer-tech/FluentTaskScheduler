using System.ComponentModel;
using System.Diagnostics;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Shell;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentTaskScheduler;

/// <summary>The composition root: decides which services exist, starts them, and opens the first window.</summary>
public partial class App : Application, IAppLifetime, IDisposable
{
    private readonly SingleInstance _instance;
    private ServiceProvider? _services;

    internal App(SingleInstance instance)
    {
        _instance = instance;
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

    public bool IsElevated { get; } = Helpers.ElevationHelper.IsElevated();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services = ConfigureServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        StartLegacyServices();

        _services.GetRequiredService<AppAppearance>().Start();

        var windows = _services.GetRequiredService<WindowManager>();
        windows.OpenWindow();

        // Starting the app again and clicking one of its notifications both ask for the window.
        var ui = _services.GetRequiredService<IUiThread>();
        _instance.OnShowRequested(() => ui.Post(windows.ShowMainWindow));
        var notifier = _services.GetRequiredService<ToastNotifier>();
        notifier.Activated += () => ui.Post(windows.ShowMainWindow);
        notifier.Start();

        _services.GetRequiredService<Tray>().Start();
    }

    private ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        AddBridges(services);

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
        services.AddSingleton<INetworkProfiles, RegistryNetworkProfiles>();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<INotifier>(app => app.GetRequiredService<ToastNotifier>());
        services.AddSingleton<WindowManager>();
        services.AddSingleton<AppAppearance>();
        services.AddSingleton<Tray>();

        // One per window
        services.AddScoped<WindowContext>();
        services.AddScoped<IDialogService, DialogService>();
        services.AddScoped<IPrompts>(window => window.GetRequiredService<IDialogService>());
        services.AddScoped<IFilePicker, FilePicker>();
        services.AddScoped<ShellPage>();
        services.AddScoped<IShellNavigator>(window => window.GetRequiredService<ShellPage>());
        services.AddScoped<TaskEditorFactory>();
        services.AddScoped<TasksViewModel>();
        services.AddScoped<FoldersViewModel>();
        services.AddScoped<TasksPage>();

        return services;
    }

    // Ends the message loop; Program then disposes the app, which saves what is pending.
    void IAppLifetime.Exit() => Exit();

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

    private void ReportCrash(Exception exception)
    {
        Serilog.Log.Fatal(exception, "Unhandled exception in the UI");
        if (_services?.GetService<WindowManager>()?.MainWindow?.Content is not FrameworkElement { XamlRoot: { } root }) return;

        string details = exception.ToString();
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get("Dialog.Crash.Title"),
            PrimaryButtonText = Loc.Get("Dialog.Crash.Copy"),
            CloseButtonText = Loc.Get("Dialog.Common.Close"),
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock
                {
                    Text = details,
                    FontFamily = new FontFamily("Consolas"),
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        dialog.PrimaryButtonClick += (_, _) => _services.GetRequiredService<IClipboard>().SetText(details);

        // Another dialog may be open; then the log entry above has to do.
        _ = dialog.ShowAsync();
    }
}
