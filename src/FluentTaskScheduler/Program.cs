using System.Runtime.InteropServices;
using FluentTaskScheduler.Shell;
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

        // A restarted instance (e.g. as administrator) starts while the old one is still closing.
        TimeSpan waitForPrevious = args.Contains(App.AfterRestartArgument) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
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
            VelopackApp.Build().Run();
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

    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();
}
