using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The top-level windows of the desktop as Windows reports them: which one is in the foreground,
/// whose it is and who owns it, and how much of it can be seen, which UI Automation does not tell.
/// </summary>
internal static class TopLevelWindows
{
    /// <summary>The window the keys typed now go to, or 0 while there is none.</summary>
    public static nint Foreground => GetForegroundWindow();

    public static int ProcessOf(nint window)
    {
        GetWindowThreadProcessId(window, out uint process);
        return (int)process;
    }

    public static string ClassOf(nint window)
    {
        var name = new StringBuilder(64);
        GetClassNameW(window, name, name.Capacity);
        return name.ToString();
    }

    public static string TitleOf(nint window)
    {
        var title = new StringBuilder(128);
        GetWindowTextW(window, title, title.Capacity);
        return title.ToString();
    }

    /// <summary>The window that owns <paramref name="window"/>, as a window owns the dialogs it opens; 0 if none does.</summary>
    public static nint OwnerOf(nint window) => GetWindow(window, OwnerRelation);

    /// <summary>The window at the end of the chain of parents and owners of <paramref name="window"/>.</summary>
    public static nint RootOwnerOf(nint window) => GetAncestor(window, RootOwnerRelation);

    public static bool IsVisible(nint window) => IsWindowVisible(window);

    public static bool IsMinimized(nint window) => IsIconic(window);

    /// <summary>The visible top-level windows, the topmost first.</summary>
    public static List<nint> Visible()
    {
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window)) windows.Add(window);
            return true;
        }, 0);
        return windows;
    }

    /// <summary>The top-level window that is on top at <paramref name="point"/>, which a click there goes to; 0 if none is.</summary>
    public static nint RootAt(Point point) => GetAncestor(WindowFromPoint(point), RootRelation);

    /// <summary>Asks <paramref name="window"/> to close, as its close button does.</summary>
    public static void Close(nint window) => PostMessageW(window, CloseMessage, 0, 0);

    /// <summary>Minimizes <paramref name="window"/>; Windows then gives the foreground to the window under it.</summary>
    public static void Minimize(nint window) => ShowWindow(window, MinimizeCommand);

    /// <summary>The dialog <paramref name="window"/> has open, where the foreground belongs while it is; otherwise the window itself.</summary>
    public static nint LastActivePopupOf(nint window) => GetLastActivePopup(window);

    /// <summary>
    /// Brings <paramref name="window"/> to the foreground. Windows lets only the program in the
    /// foreground hand it on, and sharing that program's input for the moment counts as being it.
    /// That does not work with every program, a console for one; for those, Alt is pressed first,
    /// after which Windows lets any program take the foreground, as it does for a user who is
    /// about to switch windows. The key goes to the window of the other program.
    /// </summary>
    /// <returns>Whether a window of the program of <paramref name="window"/> is in the foreground now.</returns>
    public static bool BringToFront(nint window)
    {
        if (TryBringToFront(window) || ProcessOf(GetForegroundWindow()) == ProcessOf(window)) return true;
        keybd_event(AltKey, 0, 0, 0);
        keybd_event(AltKey, 0, KeyUp, 0);
        Thread.Sleep(100); // the key has to reach that window before it loses the foreground
        return TryBringToFront(window) || ProcessOf(GetForegroundWindow()) == ProcessOf(window);
    }

    private static bool TryBringToFront(nint window)
    {
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
        GetWindowThreadProcessId(window, out uint process);
        uint thisThread = GetCurrentThreadId();
        // Sharing the input of the window's own program would reset the keys held down in it.
        bool shared = foregroundThread != 0 && foregroundThread != thisThread && foregroundProcess != process
                      && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (shared) AttachThreadInput(thisThread, foregroundThread, false);
        }
        return GetForegroundWindow() == window;
    }

    /// <summary>
    /// What can be seen of <paramref name="window"/>, in pixels of the screen. The bounds Windows
    /// and UI Automation give for a window reach further: to the left, to the right and at the
    /// bottom they take in a border that cannot be seen, for the mouse to take hold of.
    /// </summary>
    public static Rectangle FrameOf(nint window)
    {
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window, ExtendedFrameBounds, out Edges frame, Marshal.SizeOf<Edges>()));
        return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    /// <summary>The dots per inch <paramref name="window"/> is drawn with: 96 at a display scale of 100 %.</summary>
    public static int DpiOf(nint window) => (int)GetDpiForWindow(window);

    /// <summary>
    /// Puts a plain rectangle, white or dark grey, right behind <paramref name="window"/>, in front
    /// of everything else that is behind it: what the window lets through then is that rectangle.
    /// The rectangle is a window of its own, to be taken away again with <see cref="Destroy"/> on
    /// the thread that asked for it.
    /// </summary>
    public static nint ShowRectangleBehind(nint window, Rectangle area, bool white)
    {
        nint rectangle = CreateWindowExW(ToolWindow | NeverActive, "Static", null, Popup | (white ? WhiteRectangle : DarkRectangle),
            area.X, area.Y, area.Width, area.Height, 0, 0, 0, 0);
        if (rectangle == 0) throw new Win32Exception();

        SetWindowPos(rectangle, window, 0, 0, 0, 0, KeepSize | KeepPosition | KeepInactive | Show);
        UpdateWindow(rectangle);
        // Returns once the screen has been put together anew, with the rectangle on it.
        DwmFlush();
        return rectangle;
    }

    /// <summary>Takes a window of this process off the screen for good.</summary>
    public static void Destroy(nint window) => DestroyWindow(window);

    private const uint OwnerRelation = 4;     // GW_OWNER
    private const uint RootRelation = 2;      // GA_ROOT
    private const uint RootOwnerRelation = 3; // GA_ROOTOWNER
    private const int MinimizeCommand = 6;    // SW_MINIMIZE
    private const uint CloseMessage = 0x0010; // WM_CLOSE
    private const byte AltKey = 0x12;         // VK_MENU
    private const uint KeyUp = 0x0002;        // KEYEVENTF_KEYUP

    private const uint ExtendedFrameBounds = 9;  // DWMWA_EXTENDED_FRAME_BOUNDS
    private const uint Popup = 0x80000000;       // WS_POPUP
    private const uint ToolWindow = 0x00000080;  // WS_EX_TOOLWINDOW: no button in the taskbar
    private const uint NeverActive = 0x08000000; // WS_EX_NOACTIVATE
    private const uint DarkRectangle = 0x4;      // SS_BLACKRECT: black by name, a dark grey in the colours of Windows 10 and 11
    private const uint WhiteRectangle = 0x6;     // SS_WHITERECT
    private const uint KeepSize = 0x0001;        // SWP_NOSIZE
    private const uint KeepPosition = 0x0002;    // SWP_NOMOVE
    private const uint KeepInactive = 0x0010;    // SWP_NOACTIVATE
    private const uint Show = 0x0040;            // SWP_SHOWWINDOW

    /// <summary>The edges of a rectangle the way Windows hands them over.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Edges
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool WindowVisitor(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visitor, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint relation);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint relation);

    [DllImport("user32.dll")]
    private static extern nint GetLastActivePopup(nint window);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scanCode, uint flags, nuint extraInfo);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out Edges value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string? title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint module, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
}
