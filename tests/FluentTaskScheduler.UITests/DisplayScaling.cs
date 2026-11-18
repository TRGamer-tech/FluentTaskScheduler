using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.Tools;
using Microsoft.Win32;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The scaling of the main display, "Scale" in the display settings: read, changed while
/// programs run, and put back on <see cref="Dispose"/>.
/// <para>
/// Windows documents no way to change it. This is the way the display settings take:
/// DisplayConfigGetDeviceInfo and DisplayConfigSetDeviceInfo with two kinds of request that the
/// headers leave out, -3 to read and -4 to set. Both count in steps along the list of scalings,
/// from the one Windows recommends for the display. Unlike the other way known,
/// SystemParametersInfo with SPI_SETLOGICALDPIOVERRIDE, they also tell which scaling the display
/// has and which ones it offers. Found out by https://github.com/lihas/windows-DPI-scaling-sample;
/// Wine's tests run the same calls against Windows (test_DisplayConfigSetDeviceInfo in
/// dlls/user32/tests/monitor.c).
/// </para>
/// </summary>
internal sealed class DisplayScaling : IDisposable
{
    /// <summary>The scalings Windows knows, in percent. A display offers them from 100 % up to the largest its pixels have room for.</summary>
    private static readonly int[] Scalings = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    private const string DesktopKey = @"Control Panel\Desktop";

    /// <summary>
    /// The registry value that keeps Windows from asking, in a notification after a change,
    /// whether it should fix the apps that are blurry now. The notification comes up above the
    /// notification area, where the tests look at the app's icon, and Wine's tests switch it off
    /// because it takes the focus.
    /// </summary>
    private const string NoBlurryAppsQuestion = "IgnorePerProcessSystemDPIToast";

    private readonly Luid _adapter;
    private readonly uint _source;

    /// <summary>The step of 100 %, counted from the recommended scaling: zero or below.</summary>
    private readonly int _lowestStep;

    private readonly object? _noBlurryAppsQuestionBefore;

    private DisplayScaling(Luid adapter, uint source, ScaleSteps steps, Size pixels)
    {
        _adapter = adapter;
        _source = source;
        _lowestStep = steps.Lowest;
        Steps = Scalings[..(steps.Highest - steps.Lowest + 1)];
        Original = Scalings[steps.Current - steps.Lowest];
        Pixels = pixels;

        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(DesktopKey);
        _noBlurryAppsQuestionBefore = desktop.GetValue(NoBlurryAppsQuestion);
        desktop.SetValue(NoBlurryAppsQuestion, 1, RegistryValueKind.DWord);
    }

    /// <summary>The scaling the display had when this was created, in percent.</summary>
    public int Original { get; }

    /// <summary>The scalings the display offers, in percent, beginning with 100.</summary>
    public IReadOnlyList<int> Steps { get; }

    /// <summary>The width and height of the display in its own pixels, which no scaling changes.</summary>
    public Size Pixels { get; }

    /// <summary>The scaling the display has now, in percent.</summary>
    public int Percent
    {
        get
        {
            int error = Read(_adapter, _source, out ScaleSteps steps);
            return error == 0 ? Scalings[steps.Current - _lowestStep] : throw new Win32Exception(error);
        }
    }

    /// <summary>
    /// The scaling of the main display. Null where Windows does not tell it, as it may not for
    /// a virtual display; <paramref name="answer"/> then says what Windows answered instead.
    /// </summary>
    public static DisplayScaling? OfMainDisplay(out string answer)
    {
        int error = GetDisplayConfigBufferSizes(OnlyActivePaths, out uint pathCount, out uint modeCount);
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (error == 0) error = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        if (error != 0)
        {
            answer = $"Windows does not list its displays: QueryDisplayConfig answers {Describe(error)}.";
            return null;
        }

        // The main display is the one the coordinates of the desktop begin at.
        int main = Array.FindIndex(paths, 0, (int)pathCount, candidate => modes[candidate.SourceMode] is { X: 0, Y: 0 });
        if (main < 0)
        {
            answer = $"None of the {pathCount} displays Windows lists begins at the origin of the desktop.";
            return null;
        }

        PathInfo path = paths[main];
        ModeInfo mode = modes[path.SourceMode];
        error = Read(path.Adapter, path.Source, out ScaleSteps steps);
        if (error != 0)
        {
            answer = $"Windows does not tell the scaling of the main display ({mode.Width} x {mode.Height} pixels): DisplayConfigGetDeviceInfo answers {Describe(error)}.";
            return null;
        }
        if (steps.Current < steps.Lowest || steps.Current > steps.Highest || steps.Highest - steps.Lowest >= Scalings.Length)
        {
            answer = $"Windows reports step {steps.Current} for the scaling of the main display ({mode.Width} x {mode.Height} pixels), of the steps {steps.Lowest} to {steps.Highest} around the recommended one, which says nothing about its percentage.";
            return null;
        }

        answer = "";
        return new DisplayScaling(path.Adapter, path.Source, steps, new Size(mode.Width, mode.Height));
    }

    /// <summary>
    /// Gives the display one of the scalings it offers and waits until it has it: until the
    /// setting reads back as asked and the display reports the DPI of that scaling.
    /// </summary>
    /// <returns>False if the display does not have it then; <paramref name="answer"/> says what Windows answered instead.</returns>
    public bool TrySet(int percent, out string answer)
    {
        if (!Steps.Contains(percent)) throw new ArgumentOutOfRangeException(nameof(percent), percent, "The display does not offer this scaling.");

        answer = "";
        if (Percent == percent && MainDisplayDpi() == 96 * percent / 100) return true;

        var request = new SetScaleStep { Header = HeaderOf<SetScaleStep>(SetStep, _adapter, _source), Step = Array.IndexOf(Scalings, percent) + _lowestStep };
        int error = DisplayConfigSetDeviceInfo(ref request);
        if (error != 0)
        {
            answer = $"The main display ({Pixels.Width} x {Pixels.Height} pixels) does not take a scaling of {percent} %: DisplayConfigSetDeviceInfo answers {Describe(error)}.";
            return false;
        }

        int expectedDpi = 96 * percent / 100, now = 0;
        uint dpi = 0;
        bool followed = Retry.WhileFalse(() =>
        {
            now = Percent;
            dpi = MainDisplayDpi();
            return now == percent && dpi == expectedDpi;
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Success;
        if (!followed)
        {
            answer = $"The main display ({Pixels.Width} x {Pixels.Height} pixels) does not take a scaling of {percent} %: Windows accepted it, " +
                     $"but the setting reads {now} % afterwards and the display reports {dpi} DPI, where {percent} % are {expectedDpi} DPI.";
        }
        return followed;
    }

    /// <summary>Gives the display the scaling it had when this was created.</summary>
    /// <returns>False if the display does not have it then; <paramref name="answer"/> says what Windows answered instead.</returns>
    public bool TryRestore(out string answer) => TrySet(Original, out answer);

    /// <summary>Puts the display back as <see cref="TryRestore"/> does, and Windows' question about blurry apps with it.</summary>
    public void Dispose()
    {
        TryRestore(out _);

        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(DesktopKey);
        if (_noBlurryAppsQuestionBefore is null) desktop.DeleteValue(NoBlurryAppsQuestion, throwOnMissingValue: false);
        else desktop.SetValue(NoBlurryAppsQuestion, _noBlurryAppsQuestionBefore);
    }

    private static int Read(Luid adapter, uint source, out ScaleSteps steps)
    {
        steps = new ScaleSteps { Header = HeaderOf<ScaleSteps>(GetSteps, adapter, source) };
        return DisplayConfigGetDeviceInfo(ref steps);
    }

    private static DeviceInfoHeader HeaderOf<TRequest>(int type, Luid adapter, uint source) where TRequest : struct =>
        new() { Type = type, Size = Marshal.SizeOf<TRequest>(), Adapter = adapter, Source = source };

    /// <summary>The DPI the main display reports to a program that works in real pixels: 96 at 100 %, and more in proportion.</summary>
    private static uint MainDisplayDpi() =>
        GetDpiForMonitor(MonitorFromPoint(Point.Empty, PrimaryMonitor), EffectiveDpi, out uint dpi, out _) == 0 ? dpi : 0;

    private static string Describe(int error) => $"{error} ({new Win32Exception(error).Message})";

    private const uint OnlyActivePaths = 2; // QDC_ONLY_ACTIVE_PATHS
    private const int GetSteps = -3;        // not in the headers; DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_DPI_SCALE in Wine's
    private const int SetStep = -4;         // not in the headers; DISPLAYCONFIG_DEVICE_INFO_SET_SOURCE_DPI_SCALE in Wine's
    private const uint PrimaryMonitor = 1;  // MONITOR_DEFAULTTOPRIMARY
    private const int EffectiveDpi = 0;     // MDT_EFFECTIVE_DPI

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    /// <summary>DISPLAYCONFIG_PATH_INFO, as far as it is needed here: the source of a path, which is a part of the desktop.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct PathInfo
    {
        [FieldOffset(0)] public Luid Adapter;
        [FieldOffset(8)] public uint Source;
        [FieldOffset(12)] public uint SourceMode;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO of a source, as far as it is needed here: its size in pixels and where on the desktop it begins.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ModeInfo
    {
        [FieldOffset(16)] public int Width;
        [FieldOffset(20)] public int Height;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
    }

    /// <summary>DISPLAYCONFIG_DEVICE_INFO_HEADER.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public int Size;
        public Luid Adapter;
        public uint Source;
    }

    /// <summary>What a request of type -3 returns: steps along <see cref="Scalings"/>, counted from the recommended scaling.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ScaleSteps
    {
        public DeviceInfoHeader Header;
        public int Lowest;
        public int Current;
        public int Highest;
    }

    /// <summary>A request of type -4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SetScaleStep
    {
        public DeviceInfoHeader Header;
        public int Step;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, nint currentTopology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref ScaleSteps request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref SetScaleStep request);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int kind, out uint dpiX, out uint dpiY);
}
