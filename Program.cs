using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Velopack;

namespace FluentTaskScheduler
{
    public static class Program
    {
        [DllImport("Microsoft.ui.xaml.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern void XamlCheckProcessRequirements();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [STAThread]
        static void Main(string[] args)
        {
            // Initialize ComWrappers as early as possible for WinRT support.
            // This MUST be done before any WinRT types are accessed or the bootstrapper runs.
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Ensure the application directory is in the DLL search path.
            // This is critical for single-file extraction scenarios where native DLLs
            // might be in a temp folder.
            SetDefaultDllDirectories(0x00001000); // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS

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

            // Initialize the Windows App SDK bootstrapper for unpackaged apps
            try
            {
                // In a self-contained environment, we check if we should even call the bootstrapper.
                // If WindowsAppSDKSelfContained is true, the runtime is next to the EXE.
                // However, for custom Main methods, calling Bootstrap.Initialize with the right version
                // helps the WinRT subsystem find the metadata even if the manifest merging is tricky.
                
                // We use Windows App SDK 1.5 (0x00010005). We use an empty tag to avoid issues with 
                // specific servicing versions that might not be present on the target machine.
                Bootstrap.Initialize(0x00010005, "");
            }
            catch (Exception ex)
            {
                // If this fails, it's often because the Framework Package isn't installed.
                // In self-contained scenarios, this is expected to fail on machines without the SDK,
                // but we carry on and hope the local DLLs and manifest are enough.
                System.Diagnostics.Debug.WriteLine($"Bootstrap initialization failed: {ex.Message}");
            }

            try
            {
                // XamlCheckProcessRequirements is a native call in Microsoft.ui.xaml.dll.
                // Calling it here ensures the DLL is loaded and dependencies are checked.
                // This is especially important for ARM64 and Single-File extraction.
                XamlCheckProcessRequirements();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XamlCheckProcessRequirements failed: {ex.Message}");
            }

            try
            {
                Application.Start((p) =>
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                });
            }
            finally
            {
                Bootstrap.Shutdown();
            }
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
