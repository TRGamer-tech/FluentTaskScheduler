using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// One entry of the notification area icon's menu: with an action it can be chosen, with
/// children it opens a submenu, with neither it is a greyed caption.
/// </summary>
internal sealed record TrayMenuItem(string Text, Action? Invoke = null, IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new("");
}

/// <summary>
/// An icon in the notification area of the taskbar. Windows offers an unpackaged WinUI app no
/// managed API for one, so this talks to the shell directly. Create and use it on the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "FluentTaskScheduler.TrayIcon";
    private const uint IconMessage = 0x0401; // WM_USER + 1
    private const uint CheckDpiMessage = 0x0402; // WM_USER + 2
    private const int MaxTooltipLength = 127;

    // Explorer announces a new taskbar with this message, e.g. after it crashed and was restarted.
    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private readonly WindowProc _windowProc; // kept in a field so the delegate outlives the native window
    private readonly nint _module = GetModuleHandleW(null);
    private readonly nint _window;
    private nint _icon;
    private string _tooltip = "";

    /// <summary>Between <see cref="Show"/> and <see cref="Hide"/>: the icon is to be in the taskbar.</summary>
    private bool _wanted;

    /// <summary>The taskbar has the icon. A wanted icon may lack it while there is no taskbar, or while Explorer does not answer.</summary>
    private bool _added;

    public TrayIcon()
    {
        _windowProc = OnMessage;
        var windowClass = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = _module,
            lpszClassName = WindowClassName
        };
        if (RegisterClassExW(ref windowClass) == 0) throw new Win32Exception();

        // An ordinary window that is never shown. A message-only window would not be told when
        // the taskbar is created anew.
        _window = CreateWindowExW(0, WindowClassName, "", 0, 0, 0, 0, 0, 0, 0, _module, 0);
        if (_window == 0) throw new Win32Exception();

        // Windows keeps the messages of programs with fewer rights, Explorer's among them, from a
        // program that runs as administrator unless it lets them through. Without this, the icon
        // would not come back after Explorer restarts, and could miss a new display scaling or
        // the taskbar moving to another display.
        ChangeWindowMessageFilterEx(_window, TaskbarCreated, MSGFLT_ALLOW, 0);
        ChangeWindowMessageFilterEx(_window, WM_SETTINGCHANGE, MSGFLT_ALLOW, 0);
        ChangeWindowMessageFilterEx(_window, WM_DISPLAYCHANGE, MSGFLT_ALLOW, 0);
    }

    /// <summary>The user clicked the icon.</summary>
    public event Action? Clicked;

    /// <summary>The user asked for the icon's menu. Answer with <see cref="ShowMenu"/>.</summary>
    public event Action? MenuRequested;

    /// <summary>The DPI of the display whose taskbar has the notification area, which the icon is to be drawn for.</summary>
    public uint Dpi { get; private set; } = NotificationAreaDpi();

    /// <summary><see cref="Dpi"/> changed, as it does with the display scaling: the icon is to be drawn anew.</summary>
    public event Action? DpiChanged;

    /// <summary>Uses <paramref name="icon"/> from now on and takes charge of destroying it.</summary>
    public void SetIcon(nint icon)
    {
        nint previous = _icon;
        _icon = icon;
        Update();
        if (previous != 0) DestroyIcon(previous);
    }

    public void SetTooltip(string text)
    {
        _tooltip = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
        Update();
    }

    public void Show()
    {
        _wanted = true;
        Add();
    }

    public void Hide()
    {
        _wanted = false;
        if (!_added) return;
        NOTIFYICONDATAW data = Describe(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    /// <summary>Opens a menu at the mouse pointer and carries out what the user chooses from it.</summary>
    public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var actions = new List<Action>();
        nint menu = BuildMenu(items, actions);
        try
        {
            GetCursorPos(out POINT pointer);

            // A menu of a window that is not in the foreground would not close when the user
            // clicks somewhere else. The posted message afterwards makes the foreground change stick.
            SetForegroundWindow(_window);
            int chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON, pointer.X, pointer.Y, _window, 0);
            PostMessageW(_window, 0, 0, 0);

            if (chosen > 0) actions[chosen - 1]();
        }
        finally
        {
            DestroyMenu(menu); // takes the submenus with it
        }
    }

    private static nint BuildMenu(IReadOnlyList<TrayMenuItem> items, List<Action> actions)
    {
        nint menu = CreatePopupMenu();
        foreach (TrayMenuItem item in items)
        {
            if (item.Text.Length == 0)
            {
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
            }
            else if (item.Children is { } children)
            {
                AppendMenuW(menu, MF_POPUP, (nuint)BuildMenu(children, actions), item.Text);
            }
            else if (item.Invoke is { } invoke)
            {
                actions.Add(invoke);
                AppendMenuW(menu, MF_STRING, (nuint)actions.Count, item.Text);
            }
            else
            {
                AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, item.Text);
            }
        }
        return menu;
    }

    private void Update()
    {
        if (!_wanted) return;

        // An icon that could not be added is tried again with every change.
        if (!_added)
        {
            Add();
            return;
        }

        NOTIFYICONDATAW data = Describe(NIF_ICON | NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>
    /// Adding fails while there is no taskbar, until Explorer announces one, and can time out
    /// while Explorer is busy, as it is while the user signs in. An add that timed out may have
    /// got through after all; the icon then takes a modification instead.
    /// </summary>
    private void Add()
    {
        if (_added) return;
        NOTIFYICONDATAW data = Describe(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data) || Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATAW Describe(int fields) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = 1,
        uFlags = fields,
        uCallbackMessage = (int)IconMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = ""
    };

    private nint OnMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == IconMessage)
            {
                switch ((uint)lParam & 0xFFFF)
                {
                    case WM_LBUTTONUP: Clicked?.Invoke(); break;
                    case WM_RBUTTONUP or WM_CONTEXTMENU: MenuRequested?.Invoke(); break;
                }
                return 0;
            }

            if (message == TaskbarCreated)
            {
                // The new taskbar starts without the icon, and may show icons at another size.
                _added = false;
                CheckDpi();
                if (_wanted) Add();
                return 0;
            }

            if (message == CheckDpiMessage)
            {
                CheckDpi();
                return 0;
            }

            // Which of these reaches a window that is never shown when the display scaling
            // changes is up to Windows: WM_DPICHANGED is meant for windows on a display,
            // WM_SETTINGCHANGE comes when the taskbar takes another size with the scaling or
            // moves to another display, WM_DISPLAYCHANGE with a new resolution. Each leads to a
            // check once the window has answered: the sender waits for that, and can be
            // Explorer, which a new icon goes to.
            if (message is WM_DPICHANGED or WM_SETTINGCHANGE or WM_DISPLAYCHANGE) PostMessageW(_window, CheckDpiMessage, 0, 0);
        }
        catch (Exception ex)
        {
            // An exception must not travel back into Windows through this callback.
            Serilog.Log.Error(ex, "Handling a message of the notification area icon failed");
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void CheckDpi()
    {
        uint dpi = NotificationAreaDpi();
        if (dpi == Dpi) return;
        Dpi = dpi;
        DpiChanged?.Invoke();
    }

    /// <summary>
    /// The DPI of the display whose taskbar has the notification area. That is the main display
    /// unless the user dragged the taskbar to another one, as Windows 10 allows. It follows the
    /// display scaling at once, where the system DPI of a running program stays as it was when
    /// the program started.
    /// </summary>
    private static uint NotificationAreaDpi()
    {
        // The taskbars on the other displays are of another class. Without a taskbar, as while
        // Explorer restarts, the main display stands in until the new one announces itself.
        nint taskbar = FindWindowW("Shell_TrayWnd", null);
        nint display = taskbar == 0 ? MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY) : MonitorFromWindow(taskbar, MONITOR_DEFAULTTONEAREST);
        // A display can go away in between, while the displays are being changed.
        return GetDpiForMonitor(display, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi : GetDpiForSystem();
    }

    public void Dispose()
    {
        Hide();
        if (_icon != 0) DestroyIcon(_icon);
        DestroyWindow(_window);
        UnregisterClassW(WindowClassName, _module);
    }

    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint WM_CONTEXTMENU = 0x007B, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const uint WM_SETTINGCHANGE = 0x001A, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    private const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;
    private const uint MSGFLT_ALLOW = 1;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(nint window, uint message, uint action, nint changeFilterStruct);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
