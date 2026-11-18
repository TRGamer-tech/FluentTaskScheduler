using FlaUI.Core.AutomationElements;
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
        tasks.Create("Running", NotificationAreaIcon.RunningTaskXml);
        tasks.Start("Running");
        Assert.True(AppSession.Eventually(() => tasks.IsRunning("Running")), "The task that is to be counted did not start.");
        using var app = AppSession.Launch("tray-snooze", AppSession.FirstRunDone(("ShowNotifications", false), ("HasSeenTrayHint", true)));
        app.Window.Close();
        Assert.True(AppSession.Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");
        NotificationAreaIcon.SavePicture(app, "running");

        ChooseFromIconsMenu(app, "Snooze All Tasks...", "30 minutes");
        Assert.True(AppSession.Eventually(() => IconName(app).Contains("All tasks snoozed")),
            $"The icon's tooltip does not say that all tasks are snoozed: '{IconName(app)}'");
        NotificationAreaIcon.SavePicture(app, "snoozed");

        ChooseFromIconsMenu(app, "Resume All Tasks");
        Assert.True(AppSession.Eventually(() => IconName(app) is { Length: > 0 } name && !name.Contains("All tasks snoozed")),
            $"The icon's tooltip still says that all tasks are snoozed: '{IconName(app)}'");

        app.StartAgain();
        Assert.True(AppSession.Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
        app.Require("SearchBox", 20000);
        Assert.True(app.Find("SnoozeBannerResume", 2000) is null, "The window still says that all tasks are snoozed.");
    }

    /// <summary>Opens the icon's menu and chooses the entry at the end of <paramref name="path"/>, opening the submenus named before it.</summary>
    private static void ChooseFromIconsMenu(AppSession app, params string[] path)
    {
        AutomationElement icon = NotificationAreaIcon.Require(app);
        AutomationElement item = OpenIconsMenuAt(icon, app, path[0]) ?? throw NoEntry(app, path[0]);
        foreach (string name in path.Skip(1))
        {
            item = OpenSubmenu(app, item, name) ?? throw NoEntry(app, name);
        }
        Choose(app, item);
    }

    private static InvalidOperationException NoEntry(AppSession app, string name)
    {
        app.SaveScreen("no-menu-entry");
        return new InvalidOperationException($"The icon's menu has no '{name}' entry.");
    }

    // The icon's menu is one of Windows' own. On Windows 11 its entries do what UI Automation asks
    // of them. On Windows Server 2022, which is built on Windows 10, they answer that the
    // operation is not valid; what the menu shows afterwards tells whether they did it all the same,
    // and where they did not, the mouse does it.

    /// <summary>Opens the submenu of <paramref name="entry"/> and returns the entry of that name in it.</summary>
    private static AutomationElement? OpenSubmenu(AppSession app, AutomationElement entry, string name)
    {
        try
        {
            entry.Patterns.ExpandCollapse.Pattern.Expand();
        }
        catch (InvalidOperationException)
        {
            if (Retry.WhileNull(() => MenuItem(app, name), TimeSpan.FromSeconds(2)).Result is { } opened) return opened;

            // A submenu also opens for a mouse pointer that rests on its entry.
            Mouse.MoveTo(entry.BoundingRectangle.Center());
        }
        return Retry.WhileNull(() => MenuItem(app, name), TimeSpan.FromSeconds(10)).Result;
    }

    /// <summary>Chooses an entry that stands for a command. The menu closes when one is chosen.</summary>
    private static void Choose(AppSession app, AutomationElement entry)
    {
        string name = entry.NameOrEmpty();
        try
        {
            entry.Press();
        }
        catch (InvalidOperationException)
        {
            bool closed = Retry.WhileTrue(() => MenuItem(app, name) is not null, TimeSpan.FromSeconds(1)).Success;
            if (!closed && MenuItem(app, name) is { } stillOffered) Mouse.Click(stillOffered.BoundingRectangle.Center());
        }
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
    private static string IconName(AppSession app) => NotificationAreaIcon.Find(app) is { } icon ? icon.NameOrEmpty() : "";

    private static AutomationElement? MenuItem(AppSession app, string name) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.Is(ControlType.Menu) || window.ClassNameOrEmpty() == "#32768")
            .SelectMany(AppSession.Walk)
            .FirstOrDefault(element => element.Is(ControlType.MenuItem) && element.NameOrEmpty() == name);
}
