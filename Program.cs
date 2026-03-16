using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

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
            // Initialize the Windows App SDK bootstrapper for unpackaged apps
            Bootstrap.Initialize(0x00010005, "1.5");

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
    }
}
