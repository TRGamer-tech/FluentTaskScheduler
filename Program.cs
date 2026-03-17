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

        [STAThread]
        static void Main(string[] args)
        {
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
                // Using 1.5 (0x00010005) with an empty version tag to match MddBootstrapInitialize2 requirements.
                Bootstrap.Initialize(0x00010005, "");
            }
            catch (Exception ex)
            {
                // In self-contained scenarios, this might fail but the app can still run if libraries are local.
                System.Diagnostics.Debug.WriteLine($"Bootstrap initialization failed: {ex.Message}");
            }

            try
            {
                // Safely check requirements. In some single-file scenarios,
                // this might still fail if the native DLL is not yet extracted,
                // but the bootstrapper above usually helps resolution.
                XamlCheckProcessRequirements();
            }
            catch (DllNotFoundException)
            {
                // Fallback or log if needed, but continuing might still work
                // if the bootstrapper did its job for other dependencies.
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();

            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });

            Bootstrap.Shutdown();
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
