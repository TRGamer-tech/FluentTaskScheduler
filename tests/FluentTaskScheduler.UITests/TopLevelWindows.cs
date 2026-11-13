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

    /// <summary>Asks <paramref name="window"/> to close, as its close button does.</summary>
    public static void Close(nint window) => PostMessageW(window, CloseMessage, 0, 0);

    /// <summary>
    /// Brings <paramref name="window"/> to the foreground. Windows lets only the program in the
    /// foreground hand it on, and sharing that program's input for the moment counts as being it.
    /// </summary>
    public static void BringToFront(nint window)
    {
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint thisThread = GetCurrentThreadId();
        bool shared = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (shared) AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    private const uint OwnerRelation = 4;     // GW_OWNER
    private const uint RootOwnerRelation = 3; // GA_ROOTOWNER
    private const uint CloseMessage = 0x0010; // WM_CLOSE

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
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
