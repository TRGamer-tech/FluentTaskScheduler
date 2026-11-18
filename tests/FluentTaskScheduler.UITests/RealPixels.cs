using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Makes the test process work in the real pixels of the screen at every display scaling. UI
/// Automation says where an element is in real pixels, while Windows moves the pointer and
/// measures the displays in scaled ones for a program that has not declared that it does its
/// own scaling: at 150 %, a click aimed at an element would land half as far again from the
/// corner of the screen. The program that runs the tests declares nothing, so the tests do.
/// </summary>
internal static class RealPixels
{
    private const nint PerMonitorV2 = -4; // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    // When the tests are loaded: Windows takes the declaration once, and wants it before the
    // process asks anything that depends on it.
    [ModuleInitializer]
    internal static void Declare() => SetProcessDpiAwarenessContext(PerMonitorV2);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint context);
}
