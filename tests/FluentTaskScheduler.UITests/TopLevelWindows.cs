using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The top-level windows of the desktop as Windows reports them: which one is in the foreground,
/// whose it is and who owns it, which UI Automation does not tell.
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

    private const uint OwnerRelation = 4;     // GW_OWNER
    private const uint RootRelation = 2;      // GA_ROOT
    private const uint RootOwnerRelation = 3; // GA_ROOTOWNER
    private const int MinimizeCommand = 6;    // SW_MINIMIZE
    private const uint CloseMessage = 0x0010; // WM_CLOSE
    private const byte AltKey = 0x12;         // VK_MENU
    private const uint KeyUp = 0x0002;        // KEYEVENTF_KEYUP

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
}
