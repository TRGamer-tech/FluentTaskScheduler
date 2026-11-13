using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace FluentTaskScheduler.Services
{
    public static class TrayIconService
    {
        private static ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        // Win32 Constants
        private const int NIM_ADD = 0x00000000;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 1;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int IMAGE_ICON = 1;
        private const int LR_LOADFROMFILE = 0x00000010;
        private const int LR_DEFAULTSIZE = 0x00000040;

        // Context menu CMD IDs
        private const int CMD_NEW_WINDOW = 1;
        private const int CMD_EXIT      = 2;
        private const int CMD_SHOW_BASE  = 10;  // 10..59  → Show window[i]
        private const int CMD_CLOSE_BASE = 60;  // 60..109 → Close window[i]

        // Snooze submenu
        private const int CMD_SNOOZE_30M    = 200;
        private const int CMD_SNOOZE_1H     = 201;
        private const int CMD_SNOOZE_3H     = 202;
        private const int CMD_SNOOZE_REBOOT = 203;
        private const int CMD_SNOOZE_CUSTOM = 204;
        private const int CMD_SNOOZE_CANCEL = 205;

        // Win32 Structs
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        // Win32 Imports
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);
        [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string lpString);

        private const uint MF_STRING    = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_GRAYED    = 0x00000001;
        private const uint MF_POPUP     = 0x00000010;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY  = 0x0080;

        // Subclass imports
        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
        private static SUBCLASSPROC? _subclassProc;
        [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);
        [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);
        [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private const int NIM_MODIFY = 0x00000001;

        // State
        private static NOTIFYICONDATA _nid;
        private static IntPtr _hIcon = IntPtr.Zero;
        private static bool _isCreated = false;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static int _badgeCount = -1;
        private static bool _badgeSnoozed = false;
        private static IntPtr _badgeIcon = IntPtr.Zero;

        // ── Public API ──────────────────────────────────────────────────────────────
        /// <summary>
        /// Provide the list of currently hidden windows.
        /// Each entry: (display name, action to show, action to close/destroy).
        /// </summary>
        public static Func<IReadOnlyList<(string Name, Action Show, Action Close)>>? GetHiddenWindows;

        /// <summary>Fired when the user picks "New Window" from the tray menu.</summary>
        public static event Action? NewWindowRequested;

        /// <summary>Fired when the user picks "Exit All" from the tray menu.</summary>
        public static event Action? ExitRequested;

        /// <summary>Fired when the user picks "Custom Time..." from the snooze submenu.</summary>
        public static event Action? CustomSnoozeRequested;

        // ── Lifecycle ───────────────────────────────────────────────────────────────
        // Explorer.exe registers this message and broadcasts it to every top-level window after it
        // (re)starts — that's the signal to re-add the tray icon, since a restarted Explorer starts
        // with an empty notification area regardless of whether Shell_NotifyIcon(NIM_ADD) was ever
        // called (see 2.8).
        private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        public static void Initialize(IntPtr hwnd)
        {
            _hwnd = hwnd;

            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
                _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

            _subclassProc = SubclassProc;
            SetWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Show()
        {
            if (_isCreated || _hwnd == IntPtr.Zero) return;

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _badgeIcon != IntPtr.Zero ? _badgeIcon : _hIcon,
                szTip = BuildTooltip()
            };

            Shell_NotifyIcon(NIM_ADD, ref _nid);
            _isCreated = true;
        }

        public static void Hide()
        {
            if (!_isCreated) return;
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        public static void Dispose()
        {
            Hide();
            if (_badgeIcon != IntPtr.Zero) { DestroyIcon(_badgeIcon); _badgeIcon = IntPtr.Zero; }
            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero && _subclassProc != null)
                RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero);
        }

        public static void UpdateVisibility()
        {
            if (Settings.EnableTrayIcon) Show();
            else Hide();
        }

        /// <summary>Overlays a running-task count badge on the tray icon. Pass 0 to restore the plain icon.</summary>
        public static void UpdateBadge(int runningCount)
        {
            bool snoozed = SnoozeService.IsActive;
            if (_badgeCount == runningCount && _badgeSnoozed == snoozed) return;
            _badgeCount = runningCount;
            _badgeSnoozed = snoozed;
            RedrawIcon();
        }

        /// <summary>Re-renders the icon and tooltip after the global snooze state changed.</summary>
        public static void RefreshSnoozeState()
        {
            bool snoozed = SnoozeService.IsActive;
            if (_badgeSnoozed == snoozed) { UpdateTooltip(); return; }
            _badgeSnoozed = snoozed;
            RedrawIcon();
        }

        private static void RedrawIcon()
        {
            // Clean up previous badge icon
            if (_badgeIcon != IntPtr.Zero) { DestroyIcon(_badgeIcon); _badgeIcon = IntPtr.Zero; }

            if (_hIcon != IntPtr.Zero)
            {
                try
                {
                    string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
                    using var baseIcon = System.IO.File.Exists(iconPath)
                        ? new System.Drawing.Icon(iconPath, 32, 32)
                        : System.Drawing.SystemIcons.Application;

                    using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = System.Drawing.Graphics.FromImage(bmp);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Draw base icon
                    g.DrawIcon(baseIcon, new System.Drawing.Rectangle(0, 0, 32, 32));

                    if (_badgeSnoozed)
                    {
                        // Pause badge (top-left) so a paused app is recognisable at a glance
                        const int PauseSize = 15;
                        var rect = new System.Drawing.Rectangle(0, 0, PauseSize, PauseSize);
                        using var amber = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 202, 128, 0));
                        g.FillEllipse(amber, rect);
                        using var bar = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                        g.FillRectangle(bar, 4.5f, 3.5f, 2.2f, 8f);
                        g.FillRectangle(bar, 8.3f, 3.5f, 2.2f, 8f);
                    }

                    if (_badgeCount > 0)
                    {
                        // Badge circle in bottom-right corner
                        const int BadgeSize = 14;
                        int bx = 32 - BadgeSize, by = 32 - BadgeSize;
                        g.FillEllipse(System.Drawing.Brushes.OrangeRed,
                            bx, by, BadgeSize, BadgeSize);

                        // Badge number
                        string text = _badgeCount > 9 ? "9+" : _badgeCount.ToString();
                        using var font = new System.Drawing.Font("Segoe UI", _badgeCount > 9 ? 6f : 7.5f,
                            System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
                        var textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, System.Drawing.Brushes.White,
                            bx + (BadgeSize - textSize.Width) / 2,
                            by + (BadgeSize - textSize.Height) / 2);
                    }

                    _badgeIcon = bmp.GetHicon();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "{Message}", "Failed to render the tray icon badge.");
                }
            }

            if (!_isCreated) return;
            _nid.hIcon = _badgeIcon != IntPtr.Zero ? _badgeIcon : _hIcon;
            _nid.szTip = BuildTooltip();
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        private static void UpdateTooltip()
        {
            if (!_isCreated) return;
            _nid.szTip = BuildTooltip();
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        private static string BuildTooltip()
        {
            string tip = "FluentTaskScheduler";
            if (SnoozeService.IsActive) tip += "\n" + SnoozeService.StatusText;
            // NOTIFYICONDATA.szTip is a fixed 128-char buffer — overflowing it corrupts the struct.
            return tip.Length > 127 ? tip.Substring(0, 127) : tip;
        }

        // ── Context Menu ────────────────────────────────────────────────────────────
        private static void ShowContextMenu()
        {
            string L(string key, string fallback) => LocalizationService.GetString(key, fallback);
            var hidden = GetHiddenWindows?.Invoke() ?? Array.Empty<(string, Action, Action)>();

            IntPtr hMenu = CreatePopupMenu();

            if (hidden.Count == 0)
            {
                // Nothing in tray — grey placeholder so the menu isn't empty
                AppendMenu(hMenu, MF_STRING | MF_GRAYED, IntPtr.Zero, L("Tray.NoHiddenWindows", "(No hidden windows)"));
                AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            }
            else
            {
                for (int i = 0; i < hidden.Count; i++)
                {
                    AppendMenu(hMenu, MF_STRING, (IntPtr)(CMD_SHOW_BASE  + i), $"▶  {hidden[i].Name}");
                    AppendMenu(hMenu, MF_STRING, (IntPtr)(CMD_CLOSE_BASE + i), string.Format(L("Tray.CloseWindowFormat", "✕  Close {0}"), hidden[i].Name));
                }
                AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            }

            AppendSnoozeMenu(hMenu);
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_NEW_WINDOW, L("Tray.NewWindow", "New Window"));
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_EXIT, L("Tray.ExitAll", "Exit All"));

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            // DestroyMenu also destroys the submenus attached with MF_POPUP.
            DestroyMenu(hMenu);

            if (cmd >= CMD_SHOW_BASE && cmd < CMD_SHOW_BASE + hidden.Count)
                hidden[cmd - CMD_SHOW_BASE].Show();
            else if (cmd >= CMD_CLOSE_BASE && cmd < CMD_CLOSE_BASE + hidden.Count)
                hidden[cmd - CMD_CLOSE_BASE].Close();
            else if (cmd == CMD_NEW_WINDOW)
                NewWindowRequested?.Invoke();
            else if (cmd == CMD_EXIT)
                ExitRequested?.Invoke();
            else
                HandleSnoozeCommand(cmd);
        }

        private static void AppendSnoozeMenu(IntPtr hMenu)
        {
            string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            if (SnoozeService.IsActive)
            {
                AppendMenu(hMenu, MF_STRING | MF_GRAYED, IntPtr.Zero, SnoozeService.StatusText);
                AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_SNOOZE_CANCEL, L("Snooze.Menu.Resume", "Resume All Tasks"));
                return;
            }

            IntPtr hSub = CreatePopupMenu();
            AppendMenu(hSub, MF_STRING, (IntPtr)CMD_SNOOZE_30M, L("Snooze.Duration.30m", "30 Minutes"));
            AppendMenu(hSub, MF_STRING, (IntPtr)CMD_SNOOZE_1H, L("Snooze.Duration.1h", "1 Hour"));
            AppendMenu(hSub, MF_STRING, (IntPtr)CMD_SNOOZE_3H, L("Snooze.Duration.3h", "3 Hours"));
            AppendMenu(hSub, MF_STRING, (IntPtr)CMD_SNOOZE_REBOOT, L("Snooze.Duration.Reboot", "Until Next Reboot"));
            AppendMenu(hSub, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            AppendMenu(hSub, MF_STRING, (IntPtr)CMD_SNOOZE_CUSTOM, L("Snooze.Duration.Custom", "Custom Time..."));

            AppendMenu(hMenu, MF_STRING | MF_POPUP, hSub, L("Snooze.Menu.SnoozeAll", "Snooze All Tasks..."));
        }

        private static void HandleSnoozeCommand(int cmd)
        {
            try
            {
                switch (cmd)
                {
                    case CMD_SNOOZE_30M: SnoozeService.Snooze(TimeSpan.FromMinutes(30)); break;
                    case CMD_SNOOZE_1H: SnoozeService.Snooze(TimeSpan.FromHours(1)); break;
                    case CMD_SNOOZE_3H: SnoozeService.Snooze(TimeSpan.FromHours(3)); break;
                    case CMD_SNOOZE_REBOOT: SnoozeService.SnoozeUntilReboot(); break;
                    case CMD_SNOOZE_CANCEL: SnoozeService.Cancel(); break;
                    case CMD_SNOOZE_CUSTOM: CustomSnoozeRequested?.Invoke(); break;
                    default: return;
                }
                RefreshSnoozeState();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Tray snooze command {cmd} failed.");
            }
        }

        // ── Win32 message sink ──────────────────────────────────────────────────────
        private static IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0)
            {
                // Explorer restarted (crash, "Restart Explorer" quick action, etc.) — the icon we
                // previously added is gone even though _isCreated still says otherwise.
                _isCreated = false;
                UpdateVisibility();
                return IntPtr.Zero;
            }

            if (uMsg == WM_TRAYICON)
            {
                int eventId = (int)lParam;
                if (eventId == WM_LBUTTONDBLCLK)
                {
                    // Double-click: restore the most recently hidden window
                    var hidden = GetHiddenWindows?.Invoke();
                    if (hidden != null && hidden.Count > 0)
                        hidden[hidden.Count - 1].Show();
                    return IntPtr.Zero;
                }
                else if (eventId == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                    return IntPtr.Zero;
                }
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
    }
}
