using System.Runtime.InteropServices;
using FluentTaskScheduler.Cli;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
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
        RunUpdaterHooks();

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

        var store = new TaskStore();
        var runner = new TaskRunner(store, new App.NoSnooze(), new SilentNotifier());
        int exitCode = new CommandLine(store, runner, Console.Out).Run(args);
        Console.Out.Flush();
        return exitCode;
    }

    /// <summary>
    /// Lets the updater run its install, update and uninstall steps, which arrive as command line
    /// arguments and end the process when they apply.
    /// </summary>
    private static void RunUpdaterHooks()
    {
        // In a per-machine install the app folder is read-only for a standard user, and the
        // updater fails on its packages folder. There is nothing it could do there anyway.
        if (!CanWriteToAppFolder()) return;

        try
        {
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => Shell.ToastNotifier.RemoveRegistration())
                .Run();
        }
        catch (Exception ex)
        {
            // Logging is not set up this early. The app is fully usable without the updater.
            System.Diagnostics.Debug.WriteLine($"Updater hooks failed: {ex}");
        }
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
