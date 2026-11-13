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

        // NOTE: do not reintroduce SetDefaultDllDirectories/AddDllDirectory here. Narrowing the
        // process-wide DLL search order breaks the LoadLibraryEx-by-relative-name calls that XAML
        // uses to pull in its theme resource DLLs, which fails app startup with
        // "Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'".
        // The application directory is already first in the default search order, so for a
        // self-contained build (runtime DLLs sit next to the exe) these calls bought us nothing.
        //
        // Likewise, do not call Bootstrap.Initialize: that is for framework-dependent unpackaged
        // apps and adds the MSIX-installed runtime to the process package graph. This app is built
        // WindowsAppSDKSelfContained, so the runtime next to the exe is the one to use.

        [STAThread]
        static void Main(string[] args)
        {
            // Initialize ComWrappers as early as possible for WinRT support.
            // This MUST be done before any WinRT types are accessed.
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
