using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
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
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
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
            try { m_window.AppWindow.SetIcon("Assets/AppIcon.ico"); } catch { }
            
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
