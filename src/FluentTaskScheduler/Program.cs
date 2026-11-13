using System.Runtime.InteropServices;
using FluentTaskScheduler.Cli;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace FluentTaskScheduler;

public static class Program
{
    // Do not narrow the process's DLL search path here (SetDefaultDllDirectories, AddDllDirectory):
    // XAML loads its theme resource DLLs by relative name and fails to start without the default
    // order. Do not call Bootstrap.Initialize either: that is for apps that use the installed
    // Windows App SDK runtime, while this one ships its own next to the exe.

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Before anything else, and in this very method (Velopack's packaging tool checks for that): the
        // updater's install, update and uninstall steps arrive as command line arguments and end
        // the process when they apply. In a per-machine install the app folder is read-only for a
        // standard user; the updater would fail on its packages folder and could do nothing anyway.
        if (CanWriteToAppFolder())
        {
            try
            {
                VelopackApp.Build()
                    .OnBeforeUninstallFastCallback(_ =>
                    {
                        // What the app left in Windows outside its own folders: its start at sign-in
                        // and what showing notifications registered.
                        new Settings.RunKeyStartup().TrySetEnabled(false);
                        Shell.ToastNotifier.RemoveRegistration();
                    })
                    .Run();
            }
            catch (Exception ex)
            {
                // Logging is not set up this early. The app is fully usable without the updater.
                System.Diagnostics.Debug.WriteLine($"Updater hooks failed: {ex}");
            }
        }

        // A command is answered and done, whether or not the app is already running.
        if (CommandLine.Handles(args)) return RunCommandLine(args);

        // A restarted instance (e.g. as administrator) starts while the old one is still closing.
        TimeSpan waitForPrevious = args.Contains(AppArguments.AfterRestart) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
        using var instance = new SingleInstance("FluentTaskScheduler", waitForPrevious);
        if (!instance.IsFirst)
        {
            instance.AskFirstToShow();
            return 0;
        }

        XamlCheckProcessRequirements();

        App? app = null;
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            app = new App(instance);
        });

        // Start returns when the last window has closed.
        app?.Dispose();
        return 0;
    }

    private static int RunCommandLine(string[] args)
    {
        // A windowed program has no console of its own, so it borrows the one it was started
        // from. Output that is redirected to a file or a pipe goes there either way.
        AttachConsole(ParentProcess);

        // An exported history names its events. The names come from the app's resources, in English
        // like everything else the command line says, and are only opened when they are asked for.
        var strings = new Lazy<ResourceStrings>(() => new ResourceStrings(AppLanguages.Fallback));
        Loc.Use(key => strings.Value.Find(key));

        var store = new TaskStore();
        // The snooze is the app's to manage; a command only reads whether one is on.
        var runner = new TaskRunner(store, new ReadOnlySnoozeGate(new AppPaths(), new WindowsSession()), new SilentNotifier());
        int exitCode = new CommandLine(store, runner, new TaskHistory(new WindowsEventLog()), Console.Out).Run(args);
        Console.Out.Flush();
        return exitCode;
    }

    private static bool CanWriteToAppFolder()
    {
        string probe = Path.Combine(AppContext.BaseDirectory, ".write-test");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private const int ParentProcess = -1;

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AttachConsole(int processId);

    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();
}
