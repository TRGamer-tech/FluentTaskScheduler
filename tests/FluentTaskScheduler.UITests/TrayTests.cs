using System.Drawing;
using System.Drawing.Drawing2D;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The app's life in the notification area of the taskbar.</summary>
public class TrayTests
{
    [Fact]
    public void ClosingTheWindow_OnlyHidesIt_AndStartingTheAppAgainBringsItBack()
    {
        using var app = AppSession.Launch("tray-close");

        app.Window.Close();

        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");
        Assert.False(app.HasExited, "The app ended although it has its icon in the notification area.");
        Assert.True(AppSession.Eventually(() => app.SavedSetting("HasSeenTrayHint") == "True"), "The user was not told where the window went.");

        app.StartAgain();

        Assert.True(AppSession.Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
    }

    [Fact]
    public void WithoutTheIcon_ClosingTheWindowEndsTheApp()
    {
        using var app = AppSession.Launch("tray-off", AppSession.FirstRunDone(("EnableTrayIcon", false)));

        app.Window.Close();

        Assert.True(AppSession.Eventually(() => app.HasExited), "The app kept running with neither a window nor an icon.");
    }

    [Fact]
    public void MinimizingHidesTheWindowToo_WhenThatIsSwitchedOn()
    {
        using var app = AppSession.Launch("tray-minimize", AppSession.FirstRunDone(("MinimizeToTray", true)));

        app.Window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);

        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Minimizing did not hide the window.");
        app.StartAgain();
        Assert.True(AppSession.Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
        Assert.True(AppSession.Eventually(() => app.Window.Patterns.Window.Pattern.WindowVisualState.Value == WindowVisualState.Normal), "The window came back minimized.");
    }

    [Fact]
    public void TheIconsMenu_EndsTheApp()
    {
        using var app = AppSession.Launch("tray-exit");
        app.Window.Close();
        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");

        ChooseFromIconsMenu(app, "Exit");

        Assert.True(AppSession.Eventually(() => app.HasExited), "Exit in the icon's menu did not end the app.");
    }

    /// <summary>
    /// The snooze keeps the default of not suspending triggers, so it disables none of the
    /// machine's tasks. Notifications are off: one could lie over the icon or its menu. A task
    /// runs meanwhile, so that the pictures of the icon the test leaves show a count as well.
    /// </summary>
    [Fact]
    public void SnoozingAllTasksFromTheIconsMenu_ShowsInItsTooltip_AndResumingThereEndsIt()
    {
        using var tasks = new WindowsTasks();
        tasks.Create("Running", RunningTaskXml);
        tasks.Start("Running");
        Assert.True(AppSession.Eventually(() => tasks.IsRunning("Running")), "The task that is to be counted did not start.");
        using var app = AppSession.Launch("tray-snooze", AppSession.FirstRunDone(("ShowNotifications", false), ("HasSeenTrayHint", true)));
        app.Window.Close();
        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");
        SaveIconPicture(app, "running");

        ChooseFromIconsMenu(app, "Snooze All Tasks...", "30 minutes");
        Assert.True(AppSession.Eventually(() => IconName(app).Contains("All tasks snoozed")),
            $"The icon's tooltip does not say that all tasks are snoozed: '{IconName(app)}'");
        SaveIconPicture(app, "snoozed");

        ChooseFromIconsMenu(app, "Resume All Tasks");
        Assert.True(AppSession.Eventually(() => IconName(app) is { Length: > 0 } name && !name.Contains("All tasks snoozed")),
            $"The icon's tooltip still says that all tasks are snoozed: '{IconName(app)}'");

        app.StartAgain();
        Assert.True(AppSession.Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
        app.Require("SearchBox", 20000);
        Assert.True(app.Find("SnoozeBannerResume", 2000) is null, "The window still says that all tasks are snoozed.");
    }

    /// <summary>Runs for two minutes as SYSTEM, which has no desktop: a console window of its own would come up over the taskbar.</summary>
    private const string RunningTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Principals><Principal id="Author"><UserId>S-1-5-18</UserId></Principal></Principals>
          <Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries></Settings>
          <Actions Context="Author"><Exec><Command>ping</Command><Arguments>-n 120 127.0.0.1</Arguments></Exec></Actions>
        </Task>
        """;

    /// <summary>
    /// Leaves a picture of the icon as the notification area shows it, and one eight times as
    /// large, for whoever checks by eye that its marks can be read at the size Windows shows it at.
    /// </summary>
    private static void SaveIconPicture(AppSession app, string name)
    {
        AutomationElement icon = FindIcon(app) ?? throw new InvalidOperationException("The app has no icon in the notification area. Found there: " + NotificationAreaNames(app));
        // A pointer over the icon would highlight it and bring up its tooltip.
        Mouse.MoveTo(app.Screen.Center());
        Thread.Sleep(600); // the hidden icons may just have opened, the icon just been drawn anew

        using CaptureImage picture = Capture.Element(icon);
        picture.ToFile(Path.Combine(app.ArtifactsDir, $"icon-{name}.png"));
        using var enlarged = new Bitmap(picture.Bitmap.Width * 8, picture.Bitmap.Height * 8);
        using (Graphics graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(picture.Bitmap, 0, 0, enlarged.Width, enlarged.Height);
        }
        enlarged.Save(Path.Combine(app.ArtifactsDir, $"icon-{name}-x8.png"));
    }

    /// <summary>Opens the icon's menu and chooses the entry at the end of <paramref name="path"/>, opening the submenus named before it.</summary>
    private static void ChooseFromIconsMenu(AppSession app, params string[] path)
    {
        AutomationElement icon = FindIcon(app) ?? throw new InvalidOperationException("The app has no icon in the notification area. Found there: " + NotificationAreaNames(app));
        AutomationElement item = OpenIconsMenuAt(icon, app, path[0]) ?? throw new InvalidOperationException($"The icon's menu has no '{path[0]}' entry.");
        foreach (string name in path.Skip(1))
        {
            item.Patterns.ExpandCollapse.Pattern.Expand();
            item = Retry.WhileNull(() => MenuItem(app, name), TimeSpan.FromSeconds(10)).Result
                   ?? throw new InvalidOperationException($"The icon's menu has no '{name}' entry.");
        }
        item.Press();
    }

    // Now and then the ARM64 build machine loses a right-click on the icon, so it is tried again.
    private static AutomationElement? OpenIconsMenuAt(AutomationElement icon, AppSession app, string name)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            icon.RightClick();
            if (Retry.WhileNull(() => MenuItem(app, name), TimeSpan.FromSeconds(5)).Result is { } item) return item;
        }
        return null;
    }

    /// <summary>What the icon is called, which is its tooltip; empty while it cannot be found.</summary>
    private static string IconName(AppSession app) => FindIcon(app) is { } icon ? icon.NameOrEmpty() : "";

    // The icon is a button named after its tooltip, in the taskbar or behind the arrow that shows
    // the hidden ones, where Windows puts a new icon. The arrow is found by its English name: on a
    // Windows in another language, the icon has to be shown in the taskbar itself.
    private static AutomationElement? FindIcon(AppSession app)
    {
        // Windows puts more than the tooltip into the name, at least a leading blank. The shell also
        // keeps elements for icons it does not show, the hidden ones while their flyout is closed,
        // which have no size.
        AutomationElement? Search() => ShellElements(app).FirstOrDefault(element =>
            element.Is(ControlType.Button) && element.NameOrEmpty().Trim().StartsWith("FluentTaskScheduler") && HasSize(element));

        if (Search() is { } visible) return visible;
        AutomationElement? arrow = ShellElements(app).FirstOrDefault(element =>
            element.Is(ControlType.Button) && HasSize(element)
            && (element.NameOrEmpty().Contains("Hidden Icons", StringComparison.OrdinalIgnoreCase) || element.NameOrEmpty().Contains("Chevron")));
        if (arrow is null) return null;
        arrow.Click();
        return Retry.WhileNull(Search, TimeSpan.FromSeconds(10)).Result;
    }

    private static bool HasSize(AutomationElement element)
    {
        try { return element.BoundingRectangle is { Width: > 1, Height: > 1 }; }
        catch { return false; }
    }

    private static AutomationElement? MenuItem(AppSession app, string name) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.Is(ControlType.Menu) || window.ClassNameOrEmpty() == "#32768")
            .SelectMany(AppSession.Walk)
            .FirstOrDefault(element => element.Is(ControlType.MenuItem) && element.NameOrEmpty() == name);

    /// <summary>Everything in the taskbar and in the pop-ups of the shell, but not in ordinary app windows.</summary>
    private static IEnumerable<AutomationElement> ShellElements(AppSession app) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.ClassNameOrEmpty() is "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass")
            .SelectMany(AppSession.Walk);

    private static string NotificationAreaNames(AppSession app) =>
        string.Join(" | ", ShellElements(app).Select(element => element.NameOrEmpty()).Where(name => name.Length > 0).Take(80));
}
