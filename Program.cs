using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace FluentTaskScheduler
{
    public static class Program
    {
        [DllImport("Microsoft.ui.xaml.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern void XamlCheckProcessRequirements();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [STAThread]
        static void Main(string[] args)
        {
            // Set the DLL search path to include the application directory.
            // This is critical for ARM64 and self-contained builds where native DLLs
            // might not be found by the default search logic.
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            AddDllDirectory(appDir);
            SetDefaultDllDirectories(0x00001000); // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS

            // Initialize ComWrappers as early as possible for WinRT support.
            // This MUST be done before any WinRT types are accessed or the bootstrapper runs.
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // VeloPack: Handle install/uninstall/update hooks before anything else.
            // In a machine-wide install (C:\Program Files), non-admin users don't have write access,
            // which causes Velopack to crash with UnauthorizedAccessException when it tries to 
            // manage the 'packages' directory. We skip Velopack for non-admins in protected folders.
            if (HasWriteAccessToAppDir())
            {
                try
                {
                    VelopackApp.Build().Run();
                }
                catch (Exception)
                {
                    // Catch-all for any other Velopack initialization issues
                }
            }

            // NOTE: The Windows App SDK Bootstrapper API (Bootstrap.Initialize/Shutdown) is only for
            // framework-dependent unpackaged apps that need to dynamically bind to an installed
            // WinAppSDK framework package at runtime. This project uses WindowsAppSDKSelfContained=true,
            // meaning the WinAppSDK runtime is deployed locally next to the EXE and never needs the
            // bootstrapper. Calling Bootstrap.Initialize here always fails on machines without the
            // WinAppSDK 1.5 framework package installed (writing an Event ID 22 "Windows App Runtime"
            // error to the Event Log on every launch), and can leave WinRT activation in a bad state
            // that later surfaces as COMExceptions from WinRT API calls (e.g. FileSavePicker).

            try
            {
                XamlCheckProcessRequirements();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XamlCheckProcessRequirements failed: {ex.Message}");
            }

            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }

        private static bool HasWriteAccessToAppDir()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string testPath = System.IO.Path.Combine(appDir, ".velopack_write_test");
                System.IO.File.WriteAllText(testPath, "test");
                System.IO.File.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
