using System.Drawing;
using System.Drawing.Drawing2D;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The app's icon in the notification area of the taskbar, as the shell shows it.</summary>
internal static class NotificationAreaIcon
{
    /// <summary>
    /// A task for the icon to count. It runs for two minutes as SYSTEM, which has no desktop: a
    /// console window of its own would come up over the taskbar.
    /// </summary>
    public const string RunningTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Principals><Principal id="Author"><UserId>S-1-5-18</UserId></Principal></Principals>
          <Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries></Settings>
          <Actions Context="Author"><Exec><Command>ping</Command><Arguments>-n 120 127.0.0.1</Arguments></Exec></Actions>
        </Task>
        """;

    // The icon is a button named after its tooltip, in the taskbar or behind the arrow that shows
    // the hidden ones, where Windows puts a new icon. The arrow is found by its English name: on a
    // Windows in another language, the icon has to be shown in the taskbar itself.
    //
    // The arrow is tried more than once. On the ARM64 build machine the icon was not found behind
    // it whenever a console window of the machine's own setup had come up in front of everything
    // shortly before, so such a window is minimized first; and the build machines lose a click
    // now and then. A click on the arrow also closes the hidden icons again, which is why each
    // look takes its time before the next click.
    public static AutomationElement? Find(AppSession app)
    {
        if (Shown(app) is { } visible) return visible;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            AppSession.StepOverAConsoleInFront();
            AutomationElement? arrow = ShellElements(app).FirstOrDefault(element =>
                element.Is(ControlType.Button) && HasSize(element)
                && (element.NameOrEmpty().Contains("Hidden Icons", StringComparison.OrdinalIgnoreCase) || element.NameOrEmpty().Contains("Chevron")));
            if (arrow is null) return null;
            arrow.Click();
            if (Retry.WhileNull(() => Shown(app), TimeSpan.FromSeconds(5)).Result is { } icon) return icon;
        }
        return null;
    }

    /// <summary>The icon, or a failure that names what the notification area holds instead and leaves a picture of the whole screen.</summary>
    public static AutomationElement Require(AppSession app)
    {
        if (Find(app) is { } icon) return icon;

        string names = Names(app);
        app.SaveScreen("no-icon");
        throw new InvalidOperationException("The app has no icon in the notification area. Found there: " + names);
    }

    /// <summary>
    /// Leaves a picture of the icon as the notification area shows it, and one eight times as
    /// large, for whoever checks by eye that its marks can be read at the size Windows shows it at.
    /// </summary>
    /// <returns>The file of the picture at its own size.</returns>
    public static string SavePicture(AppSession app, string name)
    {
        Require(app);

        // On the build machine, the element found while the hidden icons opened had no place on
        // the screen any more a moment later, and the picture of no place is a single black
        // pixel. So the picture is taken of the place where a second look finds the icon, a look
        // that opens nothing; until one does, the hidden icons are opened again.
        Rectangle place = default;
        AppSession.Eventually(() =>
        {
            Find(app);
            // A pointer over the icon would highlight it and bring up its tooltip.
            Mouse.MoveTo(app.Screen.Center());
            Thread.Sleep(600); // the hidden icons may just have opened, the icon just been drawn anew
            place = Shown(app)?.BoundingRectangle ?? default;
            return place is { Width: > 1, Height: > 1 };
        });

        string file = Path.Combine(app.ArtifactsDir, $"icon-{name}.png");
        using CaptureImage picture = Capture.Rectangle(place);
        picture.ToFile(file);
        using var enlarged = new Bitmap(picture.Bitmap.Width * 8, picture.Bitmap.Height * 8);
        using (Graphics graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(picture.Bitmap, 0, 0, enlarged.Width, enlarged.Height);
        }
        enlarged.Save(Path.Combine(app.ArtifactsDir, $"icon-{name}-x8.png"));
        return file;
    }

    // Windows puts more than the tooltip into the name, at least a leading blank. The shell also
    // keeps elements for icons it does not show, the hidden ones while their flyout is closed,
    // which have no size.
    private static AutomationElement? Shown(AppSession app) => ShellElements(app).FirstOrDefault(element =>
        element.Is(ControlType.Button) && element.NameOrEmpty().Trim().StartsWith("FluentTaskScheduler") && HasSize(element));

    private static bool HasSize(AutomationElement element)
    {
        try { return element.BoundingRectangle is { Width: > 1, Height: > 1 }; }
        catch { return false; }
    }

    /// <summary>Everything in the taskbar and in the pop-ups of the shell, but not in ordinary app windows.</summary>
    private static IEnumerable<AutomationElement> ShellElements(AppSession app) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.ClassNameOrEmpty() is "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass")
            .SelectMany(AppSession.Walk);

    private static string Names(AppSession app) =>
        string.Join(" | ", ShellElements(app).Select(element => element.NameOrEmpty()).Where(name => name.Length > 0).Take(80));
}
