using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SS = global::FluentTaskScheduler.Services.SettingsService;

namespace FluentTaskScheduler
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window? m_window;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, IntPtr lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint IMAGE_ICON = 1;
        private const uint LR_DEFAULTSIZE = 0x00000040;
        private const uint LR_SHARED = 0x00008000;
        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
        private static readonly IntPtr ICON_BIG = new IntPtr(1);

        public App()
        {
            // Force English language
            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
            }
            catch { }
            
            
            this.InitializeComponent();
            
            // Global handlers
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#pragma warning restore CS8622
            this.UnhandledException += App_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception, "Xaml.UnhandledException");
            e.Handled = true; 
        }

        private void LogCrash(Exception? ex, string source)
        {
            try 
            {
                // Write to local folder to avoid permission issues
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                string logContent = $"[{DateTime.Now}] [{source}] Error: {ex?.Message}\r\nStack Trace: {ex?.StackTrace ?? "No stack"}\r\n\r\n";
                System.IO.File.AppendAllText(logPath, logContent);
            }
            catch { }
            System.Diagnostics.Debug.WriteLine($"[{source}] Error: {ex?.Message}");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            m_window = new Window();
            m_window.Title = "FluentTaskScheduler";
            
            // Try to set icon from file first (works when icon is copied to output)
            try 
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    m_window.AppWindow.SetIcon(iconPath);
                }
                else
                {
                    // Fallback: Try Win32 API to load from embedded resources
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);
                    IntPtr hModule = GetModuleHandle(null);
                    IntPtr hIcon = LoadImage(hModule, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_SHARED);
                    
                    if (hIcon != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
                        SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
                    }
                }
            } 
            catch { }
            
            // Set default window size
            var appWindow = m_window.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1200, Height = 700 });
            
            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            m_window.Content = rootFrame;
            
            // Force Dark Theme always
            ApplyTheme(ElementTheme.Dark);

            rootFrame.Navigate(typeof(MainPage), e.Arguments);
            
            m_window.Activate();
        }

        public void ApplyTheme(ElementTheme theme)
        {
            if (m_window?.Content is Control root)
            {
                root.RequestedTheme = theme;
                
                // Handle OLED Mode (Pure Black Background)
                // We access the frame's background if possible, or just the window content
                if (theme == ElementTheme.Dark && SS.IsOledMode)
                {
                   root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
                }
                else
                {
                    // Reset to default (null or system brush)
                    if (App.Current.Resources.ContainsKey("ApplicationPageBackgroundThemeBrush"))
                    {
                        root.Background = (Microsoft.UI.Xaml.Media.Brush)App.Current.Resources["ApplicationPageBackgroundThemeBrush"];
                    }
                }
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
