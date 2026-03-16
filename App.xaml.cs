using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
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
        // ── Window registry ──────────────────────────────────────────────────────
        private sealed class WindowRecord
        {
            public string Name { get; }
            public Window Win  { get; }
            public bool IsHidden { get; set; }
            public WindowRecord(string name, Window win) { Name = name; Win = win; }
        }

        private static readonly List<WindowRecord> _windows = new();
        private static int _windowCounter = 0;
        private static System.Threading.Mutex? _instanceMutex;
        private static System.Threading.EventWaitHandle? _showInstanceEvent;

        /// <summary>Backward-compat alias — still valid for file pickers, icon loading, etc.</summary>
        public static Window? m_window => _windows.Count > 0 ? _windows[0].Win : null;
        public Window? MainWindow => m_window;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        public App()
        {
            // Force English language
            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
            }
            catch { }

            // Handle toast notification activation (e.g. clicking the "minimized to tray" notification)
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;

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
            string errorMessage = $"[{DateTime.Now}] [{source}] Error: {ex?.Message}\r\nStack Trace: {ex?.StackTrace ?? "No stack"}\r\n\r\n";
            Services.LogService.WriteCrash(ex, source);

            // Attempt to show dialog if window exists
            if (m_window != null)
            {
                try
                {
                    var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    if (dispatcher != null)
                    {
                        // Fire and forget, we just want to see it
                        dispatcher.TryEnqueue(async () =>
                        {
                            try
                            {
                                var dialog = new ContentDialog
                                {
                                    Title = "Unhandled Exception",
                                    Content = errorMessage,
                                    CloseButtonText = "Close",
                                    XamlRoot = m_window.Content?.XamlRoot
                                };
                                await dialog.ShowAsync();
                            }
                            catch (Exception nestedEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to show crash dialog: {nestedEx.Message}");
                            }
                        });
                        // Keep process alive briefly?
                        System.Threading.Thread.Sleep(5000); 
                    }
                }
                catch { }
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var args = Environment.GetCommandLineArgs();
            
            // GUI Mode: No arguments or just the executable path
            if (args.Length > 1)
            {
                // Attempt to attach to parent console to output text
                AttachConsole(ATTACH_PARENT_PROCESS);

                // CLI Mode
                // usage: FluentTaskScheduler.exe --run "Path"
                string command = args[1].ToLower();
                string? param = args.Length > 2 ? args[2] : null; 
                bool jsonOutput = args.Contains("--json"); // Keep variable for potential future use or just ignore

                var service = new global::FluentTaskScheduler.Services.TaskServiceWrapper();
                
                try 
                {
                    if (command == "--list")
                    {
                        var tasks = service.GetAllTasks();
                        var simpleList = new System.Collections.Generic.List<object>();
                        foreach(var t in tasks)
                        {
                                simpleList.Add(new { 
                                Name = t.Name, 
                                Path = t.Path, 
                                State = t.State, 
                                LastRun = t.LastRunTime, 
                                NextRun = t.NextRunTime 
                                });
                        }
                        string json = System.Text.Json.JsonSerializer.Serialize(simpleList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        Console.WriteLine(json);
                    }
                    else if (command == "--run" && !string.IsNullOrEmpty(param))
                    {
                        Console.WriteLine($"Running task: {param}");
                        service.RunTask(param);
                        Console.WriteLine("Task started.");
                    }
                    else if (command == "--enable" && !string.IsNullOrEmpty(param))
                    {
                            Console.WriteLine($"Enabling task: {param}");
                            service.EnableTask(param);
                            Console.WriteLine("Task enabled.");
                    }
                    else if (command == "--disable" && !string.IsNullOrEmpty(param))
                    {
                            Console.WriteLine($"Disabling task: {param}");
                            service.DisableTask(param);
                            Console.WriteLine("Task disabled.");
                    }
                        else if (command == "--export-history" && !string.IsNullOrEmpty(param))
                    {
                        string output = args.Length > 4 && args[3] == "--output" ? args[4] : "history.csv";
                        Console.WriteLine($"Exporting history for {param} to {output}...");
                        
                        var history = service.GetTaskHistory(param);
                        if (history != null && history.Count > 0)
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine("Time,EventId,Result,User,ExitCode,Message");
                            foreach (var h in history)
                            {
                                sb.AppendLine($"\"{h.Time}\",{h.EventId},\"{h.Result}\",\"{h.User}\",{h.ExitCode},\"{h.Message.Replace("\"", "\"\"")}\"");
                            }
                            System.IO.File.WriteAllText(output, sb.ToString());
                            Console.WriteLine("Export complete.");
                        }
                        else
                        {
                            Console.WriteLine("No history found or task does not exist.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                // Flush and Exit
                Console.Out.Flush();
                Environment.Exit(0);
                return;
            }

            // ── Single-instance enforcement (GUI mode) ──────────────────────────
            _instanceMutex = new System.Threading.Mutex(true, "FluentTaskScheduler_Instance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Another GUI instance is already running — signal it to show itself and exit
                try
                {
                    var ev = System.Threading.EventWaitHandle.OpenExisting("FluentTaskScheduler_Show");
                    ev.Set();
                }
                catch { }
                Environment.Exit(0);
                return;
            }

            // First instance: listen for show-signals from future instances
            _showInstanceEvent = new System.Threading.EventWaitHandle(
                false, System.Threading.EventResetMode.AutoReset, "FluentTaskScheduler_Show");
            System.Threading.Tasks.Task.Run(() =>
            {
                while (true)
                {
                    _showInstanceEvent.WaitOne();
                    var win = _windows.Count > 0 ? _windows[0].Win : null;
                    win?.DispatcherQueue.TryEnqueue(() => { win.AppWindow.Show(); win.Activate(); });
                }
            });

            // GUI Mode: create and register the first window
            CreateAndRegisterWindow();

            // One-time tray init (uses the first window's HWND as the message sink)
            var trayHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_windows[0].Win);
            Services.TrayIconService.Initialize(trayHwnd);

            // Callback: returns all currently hidden windows for the tray menu
            Services.TrayIconService.GetHiddenWindows = () =>
            {
                var list = new List<(string, Action, Action)>();
                foreach (var rec in _windows)
                {
                    if (!rec.IsHidden) continue;
                    var r = rec; // capture
                    list.Add((
                        r.Name,
                        () => r.Win.DispatcherQueue.TryEnqueue(() => { r.Win.AppWindow.Show(); r.Win.Activate(); r.IsHidden = false; }),
                        () => r.Win.DispatcherQueue.TryEnqueue(() => { r.IsHidden = false; r.Win.Close(); })
                    ));
                }
                return list;
            };

            Services.TrayIconService.NewWindowRequested += () =>
                _windows[0].Win.DispatcherQueue.TryEnqueue(CreateAndRegisterWindow);

            Services.TrayIconService.ExitRequested += () => Environment.Exit(0);
            Services.TrayIconService.UpdateVisibility();

            Services.LogService.Info("Application started");
            Services.ReminderService.Start();

            // Check for VeloPack auto-updates in the background
            _ = CheckForVeloPackUpdateAsync();

            // Defer smooth scrolling until visual tree is built
            _windows[0].Win.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrolling(SS.SmoothScrolling);
            });
        }

        private void CreateAndRegisterWindow()
        {
            _windowCounter++;
            string name = _windowCounter == 1 ? "Window 1" : $"Window {_windowCounter}";

            var win = new Window();
            win.Title = _windowCounter == 1 ? "FluentTaskScheduler" : $"FluentTaskScheduler — {name}";

            var rec = new WindowRecord(name, win);
            _windows.Add(rec);

            // Icon
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                    win.AppWindow.SetIcon(iconPath);
                else
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr hModule = GetModuleHandle(null);
                        IntPtr hIcon = LoadImage(hModule, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_SHARED);
                        if (hIcon != IntPtr.Zero) { SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon); SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon); }
                    }
                }
            }
            catch { }

            // Size — first window restores saved size, subsequent windows use a slight offset
            int offset = (_windowCounter - 1) * 30;
            win.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = SS.WindowWidth + offset, Height = SS.WindowHeight + offset });

            // Save size changes for the first window only
            if (_windowCounter == 1)
            {
                win.AppWindow.Changed += (s, e) =>
                {
                    if (e.DidSizeChange && !rec.IsHidden)
                    {
                        SS.WindowWidth  = s.Size.Width;
                        SS.WindowHeight = s.Size.Height;
                    }
                };
            }

            // Frame & navigation
            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            win.Content = rootFrame;
            
            // Extend into title bar
            win.ExtendsContentIntoTitleBar = true;
            
            ApplyThemeToWindow(win);
            rootFrame.Navigate(typeof(MainPage));

            // Close-to-tray handler
            win.AppWindow.Closing += (sender, args) =>
            {
                if (SS.EnableTrayIcon)
                {
                    args.Cancel = true;
                    rec.IsHidden = true;
                    sender.Hide();
                    Services.NotificationService.ShowMinimizedToTray();
                }
                else
                {
                    // Actually closing — remove from registry
                    _windows.Remove(rec);
                }
            };

            win.Activate();
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            // Handled per-window inside CreateAndRegisterWindow
        }

        private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("action", out string action) && action == "show")
            {
                // Restore the most-recently-hidden window, or the first window
                var win = _windows.FindLast(r => r.IsHidden)?.Win ?? m_window;
                win?.DispatcherQueue.TryEnqueue(() => { win.AppWindow.Show(); win.Activate(); });
            }
        }

        public void ApplySmoothScrolling(bool enable)
        {
            foreach (var rec in _windows)
            {
                if (rec.Win?.Content == null) continue;
                foreach (var sv in FindDescendants<ScrollViewer>(rec.Win.Content))
                    sv.IsScrollInertiaEnabled = enable;
            }
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        Microsoft.UI.Xaml.Media.SystemBackdrop? _backdrop;

        private void ApplyThemeToWindow(Window win)
        {
            if (win?.Content is Control root)
            {
                root.RequestedTheme = SS.Theme;
                win.SystemBackdrop = null;

                Application.Current.Resources["TaskCardBackground"] = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                Application.Current.Resources["TaskCardBorder"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                if (SS.IsOledMode && SS.Theme == ElementTheme.Dark)
                {
                    var black = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
                    root.Background = black;
                    SetNavigationViewBackgrounds(black);
                }
                else if (SS.IsMicaEnabled && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                {
                    var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    root.Background = transparent;
                    if (_backdrop == null) _backdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                    win.SystemBackdrop = _backdrop;
                    SetNavigationViewBackgrounds(transparent);
                }
                else
                {
                    _backdrop = null;
                    // Use ActualTheme so the colour matches the requested theme even when the OS theme differs
                    bool isDark = root.ActualTheme == ElementTheme.Dark;
                    var bg = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                               : Windows.UI.Color.FromArgb(255, 243, 243, 243));
                    root.Background = bg;
                    SetNavigationViewBackgrounds(bg);
                }

                // Title bar customization
                UpdateTitleBarTheme(win, root.ActualTheme);
            }
        }

        private void UpdateTitleBarTheme(Window win, ElementTheme theme)
        {
            var appWindow = win.AppWindow;
            if (appWindow == null) return;

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                bool isDark = theme == ElementTheme.Dark;

                // Set colors for the title bar buttons to match theme
                if (isDark)
                {
                    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(100, 255, 255, 255);
                }
                else
                {
                    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 0, 0, 0);
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(100, 0, 0, 0);
                }
            }
        }

        private static void SetNavigationViewBackgrounds(Microsoft.UI.Xaml.Media.Brush brush)
        {
            Application.Current.Resources["NavigationViewContentBackground"] = brush;
            Application.Current.Resources["NavigationViewExpandedPaneBackground"] = brush;
            Application.Current.Resources["NavigationViewTopPaneBackground"] = brush;
        }

        public void ApplyTheme(ElementTheme theme)
        {
            foreach (var rec in _windows)
                ApplyThemeToWindow(rec.Win);
        }

        private async System.Threading.Tasks.Task CheckForVeloPackUpdateAsync()
        {
            try
            {
                var (updateReady, info, newVersion) = await Services.VeloPackUpdateService.CheckAndDownloadAsync();
                if (!updateReady || info == null || m_window == null) return;

                m_window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Update Available",
                            Content = $"Version {newVersion} has been downloaded and is ready to install.\nRestart now to apply the update?",
                            PrimaryButtonText = "Restart Now",
                            CloseButtonText = "Later",
                            XamlRoot = m_window.Content?.XamlRoot,
                            RequestedTheme = SS.Theme
                        };

                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            Services.VeloPackUpdateService.ApplyAndRestart(info);
                        }
                    }
                    catch (Exception ex)
                    {
                        Services.LogService.Info($"[AutoUpdate] Could not show update dialog: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Services.LogService.Info($"[AutoUpdate] Background update check failed: {ex.Message}");
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

    }
}
