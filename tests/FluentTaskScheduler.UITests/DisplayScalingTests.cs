using System.Drawing;
using FlaUI.Core.Capturing;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The app while the scaling of the main display changes under it, and at a scaling other than
/// 100 %. The tests change that scaling for everything on the machine and put it back. They run
/// only where <see cref="ChangeScalingVariable"/> lets them, and skip, with what Windows
/// answered, on a display that takes no other scaling.
/// </summary>
public sealed class DisplayScalingTests : IDisposable
{
    /// <summary>
    /// Set to 1 where the tests may change the scaling of the main display. Every window on it
    /// is moved and resized by that: nothing to a build machine, an imposition on a desk.
    /// </summary>
    private const string ChangeScalingVariable = "FTS_UI_CHANGE_DISPLAY_SCALING";

    /// <summary>The size the tests give the window, in device-independent pixels: those of a scaling of 100 %.</summary>
    private static readonly Size WindowSize = new(900, 600);

    /// <summary>What the window is to have around it on the screen, in the same pixels: the taskbar, and some distance to the edges.</summary>
    private const int RoomAround = 100;

    /// <summary>The page of the task list is whole while these are: the last button of its toolbar, and the list.</summary>
    private static readonly string[] PageElements = ["MoreButton", "TaskList"];

    private DisplayScaling? _scaling;

    /// <summary>
    /// The scaling goes back to what it was after every test, also after one that failed, and
    /// the test fails if it did not: the tests that follow count on the display they began with.
    /// </summary>
    public void Dispose()
    {
        if (_scaling is not null) PutBack(_scaling);
    }

    /// <summary>
    /// Ten tasks run meanwhile, so that the icon shows "9+" whatever else runs on the machine and
    /// the two pictures differ in nothing but the scaling. The window is hidden, as it is for
    /// most of the life of an app in the notification area; notifications are off, since one
    /// could lie over the icon.
    /// <para>
    /// That the app drew the icon anew is told from two things: its log says that it did, for
    /// the DPI of the new scaling, and the badge in the notification area has grown with the
    /// scaling. How clean the badge looks there is written down and not judged: at 150 % the
    /// taskbar of the build machine shows a 24 pixel icon half a pixel off, across 25 pixels,
    /// which softens every edge whoever drew the icon. The pictures are kept for the eye.
    /// </para>
    /// </summary>
    [SkippableFact]
    public void TheNotificationAreaIcon_IsDrawnAnew_ForTheSizeOfANewScaling()
    {
        DisplayScaling scaling = ScalingToChange(out int larger);
        SetOrSkip(scaling, 100);
        using var tasks = new WindowsTasks();
        for (int number = 1; number <= 10; number++)
        {
            tasks.Create($"Running{number}", NotificationAreaIcon.RunningTaskXml);
            tasks.Start($"Running{number}");
        }
        using var app = AppSession.Launch("scaling-icon", AppSession.FirstRunDone(("ShowNotifications", false), ("HasSeenTrayHint", true)));
        app.Window.Close();
        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");
        NotificationAreaIcon.Require(app);

        // The app counts the running tasks every ten seconds.
        var before = new BadgePicture(Rectangle.Empty, 0);
        bool counted = AppSession.Eventually(() =>
        {
            before = BadgePicture.In(NotificationAreaIcon.SavePicture(app, "100"));
            return before.Bounds.Width > before.Bounds.Height;
        }, 40);
        Assert.True(counted, $"The icon does not show the badge of more than nine running tasks, which is wider than high. Its badge is {Describe(before)}.");

        SetOrSkip(scaling, larger);

        // The taskbar and the app take a moment to follow. What the last picture shows is judged below.
        var after = new BadgePicture(Rectangle.Empty, 0);
        bool HasGrown() => Math.Abs(after.Bounds.Height - before.Bounds.Height * larger / 100.0) <= 1.5;
        AppSession.Eventually(() =>
        {
            after = BadgePicture.In(NotificationAreaIcon.SavePicture(app, $"{larger}"));
            return HasGrown();
        }, 30);

        // Nobody has seen the taskbar of a build machine at this scaling: what is around the icon may explain its picture.
        using (CaptureImage screen = Capture.Screen()) screen.ToFile(Path.Combine(app.ArtifactsDir, $"screen-{larger}.png"));
        app.Log($"The badge at 100 %: {Describe(before)}. At {larger} %: {Describe(after)}.");

        string log = AppLog(app);
        Assert.True(log.Contains($"drawn anew for a display of {96 * larger / 100} DPI"),
            $"The app did not draw its icon anew for a scaling of {larger} %. There the badge is {Describe(after)}; at 100 % it was {Describe(before)}. The app's log:{Environment.NewLine}{log}");
        Assert.True(HasGrown(),
            $"The notification area does not show the icon at the size of {larger} %: there the badge is {Describe(after)}; at 100 % it was {Describe(before)}.");
    }

    [SkippableFact]
    public void TheWindow_KeepsItsSizeAndShowsItsPageWhole_WhenTheScalingChanges()
    {
        DisplayScaling scaling = ScalingToChange(out int larger);
        SkipUnlessTheWindowHasRoom(scaling, larger);
        SetOrSkip(scaling, 100);
        using var app = AppSession.Launch("scaling-window");
        app.Require("TaskList", 30000);
        app.Resize(WindowSize.Width, WindowSize.Height);
        CheckWindow(app, 100);
        app.Snapshot("100");

        SetOrSkip(scaling, larger);
        CheckWindow(app, larger);
        app.Snapshot($"{larger}");

        Assert.True(scaling.TrySet(100, out string answer), answer);
        CheckWindow(app, 100);
        app.Snapshot("100-again");
    }

    [SkippableFact]
    public void TheWindow_OpensAtTheSizeItRemembers_AtALargerScaling()
    {
        DisplayScaling scaling = ScalingToChange(out int larger);
        SkipUnlessTheWindowHasRoom(scaling, larger);
        SetOrSkip(scaling, larger);

        using var app = AppSession.Launch("scaling-start", AppSession.FirstRunDone(("WindowWidth", WindowSize.Width), ("WindowHeight", WindowSize.Height)));
        app.Require("TaskList", 30000);

        CheckWindow(app, larger);
        app.Snapshot($"{larger}");
    }

    /// <summary>
    /// The scaling of the main display, and the larger scaling a test changes it to. Skips the
    /// test where it may not change the scaling, or where the display has no other.
    /// </summary>
    private DisplayScaling ScalingToChange(out int larger)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(ChangeScalingVariable) == "1",
            $"This test changes the scaling of the main display, which moves and resizes every window on it. Set {ChangeScalingVariable}=1 to let it.");
        Assert.True(RealPixels.InForce,
            "The test process does not work in real pixels, which it has to once the scaling is not 100 %: whatever runs the tests declared another way of scaling before they were loaded.");

        _scaling = DisplayScaling.OfMainDisplay(out string answer);
        Skip.If(_scaling is null, answer);
        DisplayScaling scaling = _scaling!;

        // 150 %, or the 125 % that are all a small display offers.
        larger = scaling.Steps.Where(step => step <= 150).Max();
        Skip.If(larger == 100, $"The main display ({scaling.Pixels.Width} x {scaling.Pixels.Height} pixels) offers no scaling above 100 %.");
        return scaling;
    }

    private static void SkipUnlessTheWindowHasRoom(DisplayScaling scaling, int larger)
    {
        var needed = new Size((WindowSize.Width + RoomAround) * larger / 100, (WindowSize.Height + RoomAround) * larger / 100);
        Skip.If(needed.Width > scaling.Pixels.Width || needed.Height > scaling.Pixels.Height,
            $"At {larger} %, a window of {WindowSize.Width} x {WindowSize.Height} device-independent pixels and what is around it take {needed.Width} x {needed.Height} pixels. " +
            $"The main display has {scaling.Pixels.Width} x {scaling.Pixels.Height}.");
    }

    /// <summary>
    /// Gives the display a scaling, or skips the test with what Windows answered where the
    /// display does not take it. The display is put back before that skip, not only after it: a
    /// test that skipped is reported as skipped whatever its <see cref="Dispose"/> found, and a
    /// display left changed must not go unnoticed.
    /// </summary>
    private static void SetOrSkip(DisplayScaling scaling, int percent)
    {
        if (scaling.TrySet(percent, out string answer)) return;
        PutBack(scaling);
        throw new SkipException(answer);
    }

    private static void PutBack(DisplayScaling scaling)
    {
        scaling.Dispose();
        Assert.True(scaling.Percent == scaling.Original,
            $"The main display is left at a scaling of {scaling.Percent} %. It had {scaling.Original} % before this test, and the tests after it count on that.");
    }

    /// <summary>
    /// At a scaling of <paramref name="percent"/>, the window has the size the test gave it, in
    /// the pixels of that scaling, lies inside the screen and shows the page of the task list
    /// whole, and the app remembers the size for its next start as what it is.
    /// </summary>
    private static void CheckWindow(AppSession app, int percent)
    {
        var expected = new Size(WindowSize.Width * percent / 100, WindowSize.Height * percent / 100);
        int tolerance = 4 * percent / 100;
        Rectangle window = default;

        // The window follows the display a moment later.
        bool sized = AppSession.Eventually(() =>
        {
            window = app.Window.BoundingRectangle;
            return Math.Abs(window.Width - expected.Width) <= tolerance && Math.Abs(window.Height - expected.Height) <= tolerance;
        }, 10);
        app.Log($"At {percent} %: window {window}, screen {app.Screen}");
        Assert.True(sized, $"At {percent} %, the window is {window.Width} x {window.Height} pixels. " +
                           $"Its {WindowSize.Width} x {WindowSize.Height} device-independent pixels are {expected.Width} x {expected.Height} there.");
        Assert.True(app.Screen.Contains(window), $"At {percent} %, the window at {window} reaches beyond the screen {app.Screen}.");

        foreach (string id in PageElements)
        {
            Rectangle element = default;
            bool whole = AppSession.Eventually(() =>
            {
                var found = app.Require(id);
                element = found.BoundingRectangle;
                return !found.IsOffscreen && element is { Width: > 0, Height: > 0 } && app.Window.BoundingRectangle.Contains(element);
            }, 10);
            Assert.True(whole, $"At {percent} %, {id} at {element} is not whole inside the window at {app.Window.BoundingRectangle}.");
        }

        // The app saves a size half a second after the window last changed it. A wrong one would be there by now.
        Thread.Sleep(1500);
        Size remembered = default;
        bool right = AppSession.Eventually(() =>
        {
            remembered = new Size(int.Parse(app.SavedSetting("WindowWidth")!), int.Parse(app.SavedSetting("WindowHeight")!));
            return Math.Abs(remembered.Width - WindowSize.Width) <= 4 && Math.Abs(remembered.Height - WindowSize.Height) <= 4;
        }, 5);
        Assert.True(right, $"At {percent} %, the app remembers a window of {remembered.Width} x {remembered.Height} device-independent pixels for its next start. " +
                           $"The window has {WindowSize.Width} x {WindowSize.Height}.");
    }

    private static string Describe(BadgePicture badge) =>
        $"{badge.Bounds.Width} x {badge.Bounds.Height} pixels, {badge.PureShare:P0} of them purely red or white";

    /// <summary>What the app has logged so far. The app keeps the file open, so it is read without claiming it.</summary>
    private static string AppLog(AppSession app)
    {
        using var file = new FileStream(Path.Combine(app.DataDir, "App_Log.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }
}
