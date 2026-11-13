using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FluentTaskScheduler.Services
{
    public static class StartupService
    {
        private static ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FluentTaskScheduler";

        public static void Enable()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                key?.SetValue(AppName, $"\"{exePath}\"");
                Serilog.Log.Information("Run on Startup enabled");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to enable Run on Startup");
            }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key?.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
                Serilog.Log.Information("Run on Startup disabled");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to disable Run on Startup");
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void UpdateFromSettings()
        {
            if (Settings.RunOnStartup)
                Enable();
            else
                Disable();
        }
    }
}
