using System.Runtime.InteropServices;
using FluentTaskScheduler.Cli;
using FluentTaskScheduler.History;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
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
        Exception? updaterFailure = null;
        if (CanWriteToAppFolder())
        {
            try
            {
                VelopackApp.Build()
                    // Velopack would install a downloaded update at the next start, whatever that start
                    // is for: a command a script is waiting for, or a second start that should only bring
                    // the window back. An update the user put off is offered again instead.
                    .SetAutoApplyOnStartup(false)
                    .OnAfterInstallFastCallback(_ => OnInstalled())
                    .OnBeforeUninstallFastCallback(_ => OnUninstalling())
                    .Run();
            }
            catch (Exception ex)
            {
                // The app is fully usable without the updater. It logs why once it has its logger.
                updaterFailure = ex;
            }
        }

        // Velopack has answered the installer's starts and ended the process. Where it was left out
        // or failed, they are answered here: such a start has seconds and must never open the window.
        switch (InstallerSteps.Of(args))
        {
            case InstallerStep.None:
                break;
            case InstallerStep.Installed:
                OnInstalled();
                return 0;
            case InstallerStep.Uninstalling:
                OnUninstalling();
                return 0;
            default:
                return 0;
        }

        // Versions before 2.0 kept the app's files where an installation for one user also has the
        // app itself. They come over to the data folder before anything reads them, whatever this
        // start is for: a command looks at them as well.
        var paths = new AppPaths();
        DataFolderMoveReport movedFiles = DataFolderMove.Run(paths);

        // A command is answered and done, whether or not the app is already running.
        if (CommandLine.Handles(args)) return RunCommandLine(args);

        // A restarted instance (e.g. as administrator) starts while the old one is still closing.
        TimeSpan waitForPrevious = args.Contains(AppArguments.AfterRestart) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
        using var instance = new SingleInstance(InstanceName, waitForPrevious);
        if (!instance.IsFirst)
        {
            instance.AskFirstToShow();
            return 0;
        }

        // An installation that told the app nothing, a portable copy for one, finds the note here.
        new StartWithWindowsNote(new RunKeyStartup(), paths).PutEntryBack();

        App? app = null;
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            app = new App(instance, updaterFailure, movedFiles);
        });

        // Start returns when the last window has closed.
        app?.Dispose();

        // The user asked for it in the settings. Nothing of the app writes to its files any more.
        if (app is { DeletesDataOnExit: true })
        {
            ForgetWhatWindowsKnowsOfTheApp(new RunKeyStartup());
            DataFolderDeletion.Run(paths);
        }
        return 0;
    }

    private const string InstanceName = "FluentTaskScheduler";

    // The app's start with Windows comes back if it was only taken away by the uninstall that a
    // newer Setup MSI runs before it installs itself.
    private static void OnInstalled() => new StartWithWindowsNote(new RunKeyStartup(), new AppPaths()).PutEntryBack();

    // The app's files in the data folder stay: they are the user's, and this is also what runs
    // when a newer Setup MSI replaces this version.
    private static void OnUninstalling()
    {
        // A copy of the app that is running, in the notification area for one, would hold on to
        // the files the uninstaller is about to delete.
        using (var instance = new SingleInstance(InstanceName, TimeSpan.Zero))
        {
            if (!instance.IsFirst) instance.AskFirstToExit(TimeSpan.FromSeconds(15));
        }

        var startup = new RunKeyStartup();
        new StartWithWindowsNote(startup, new AppPaths()).TakeEntryAway();
        ForgetWhatWindowsKnowsOfTheApp(startup);
    }

    /// <summary>What the app left in Windows outside its own folders: its start at sign-in and what showing notifications registered.</summary>
    private static void ForgetWhatWindowsKnowsOfTheApp(RunKeyStartup startup)
    {
        startup.TrySetEnabled(false);
        Shell.ToastNotifier.RemoveRegistration();
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
}
