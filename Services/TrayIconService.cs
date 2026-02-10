using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Services
{
    public static class TrayIconService
    {
        // Win32 Constants
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
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

        // Win32 Imports
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        // State
        private static NOTIFYICONDATA _nid;
        private static IntPtr _hIcon = IntPtr.Zero;
        private static bool _isCreated = false;
        private static IntPtr _hwnd = IntPtr.Zero;

        // Subclass proc for intercepting tray messages
        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
        private static SUBCLASSPROC? _subclassProc;

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        public static event Action? ShowRequested;
        public static event Action? ExitRequested;

        public static void Initialize(IntPtr hwnd)
        {
            _hwnd = hwnd;

            // Load icon from file
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            }

            // Set up subclass to receive tray messages
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
                hIcon = _hIcon,
                szTip = "FluentTaskScheduler"
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

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            if (_hwnd != IntPtr.Zero && _subclassProc != null)
            {
                RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero);
            }
        }

        public static void UpdateVisibility()
        {
            if (SettingsService.EnableTrayIcon)
                Show();
            else
                Hide();
        }

        // Context Menu via Win32
        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;
        private const int CMD_SHOW = 1;
        private const int CMD_EXIT = 2;
        private const uint WM_COMMAND = 0x0111;

        private static void ShowContextMenu()
        {
            IntPtr hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_SHOW, "Show");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_EXIT, "Exit");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);

            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(hMenu);

            if (cmd == CMD_SHOW)
                ShowRequested?.Invoke();
            else if (cmd == CMD_EXIT)
                ExitRequested?.Invoke();
        }

        private static IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                int eventId = (int)lParam;
                if (eventId == WM_LBUTTONDBLCLK)
                {
                    ShowRequested?.Invoke();
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
