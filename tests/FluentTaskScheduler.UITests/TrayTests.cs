using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The app's life in the notification area of the taskbar.</summary>
public class TrayTests
{
    private const string Settings = """{"Language":"en-US","HasCompletedOnboarding":true,"LastSeenVersion":"999.0.0",""";

    [Fact]
    public void ClosingTheWindow_OnlyHidesIt_AndStartingTheAppAgainBringsItBack()
    {
        using var app = AppSession.Launch("tray-close");

        app.Window.Close();

        Assert.True(Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");
        Assert.False(app.HasExited, "The app ended although it has its icon in the notification area.");
        Assert.True(Eventually(() => app.SavedSetting("HasSeenTrayHint") == "True"), "The user was not told where the window went.");

        app.StartAgain();

        Assert.True(Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
    }

    [Fact]
    public void WithoutTheIcon_ClosingTheWindowEndsTheApp()
    {
        using var app = AppSession.Launch("tray-off", Settings + """ "EnableTrayIcon":false}""");

        app.Window.Close();

        Assert.True(Eventually(() => app.HasExited), "The app kept running with neither a window nor an icon.");
    }

    [Fact]
    public void MinimizingHidesTheWindowToo_WhenThatIsSwitchedOn()
    {
        using var app = AppSession.Launch("tray-minimize", Settings + """ "MinimizeToTray":true}""");

        app.Window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);

        Assert.True(Eventually(() => !app.IsWindowVisible), "Minimizing did not hide the window.");
        app.StartAgain();
        Assert.True(Eventually(() => app.IsWindowVisible), "Starting the app again did not bring the window back.");
        Assert.True(Eventually(() => app.Window.Patterns.Window.Pattern.WindowVisualState.Value == WindowVisualState.Normal), "The window came back minimized.");
    }

    [Fact]
    public void TheIconsMenu_EndsTheApp()
    {
        using var app = AppSession.Launch("tray-exit");
        app.Window.Close();
        Assert.True(Eventually(() => !app.IsWindowVisible), "Closing did not hide the window.");

        AutomationElement icon = FindIcon(app) ?? throw new InvalidOperationException("The app has no icon in the notification area. Found there: " + NotificationAreaNames(app));
        AutomationElement exit = MenuItemOf(icon, app, "Exit") ?? throw new InvalidOperationException("The icon's menu has no Exit entry.");
        exit.Press();

        Assert.True(Eventually(() => app.HasExited), "Exit in the icon's menu did not end the app.");
    }

    // The icon is a button named after its tooltip. Windows puts a new icon behind the arrow
    // that shows the hidden ones.
    private static AutomationElement? FindIcon(AppSession app)
    {
        // Windows puts more than the tooltip into the name, at least a leading blank.
        AutomationElement? Search() => ShellElements(app).FirstOrDefault(element => element.ControlType == ControlType.Button && NameOf(element).Trim().StartsWith("FluentTaskScheduler"));

        if (Search() is { } visible) return visible;
        AutomationElement? arrow = ShellElements(app).FirstOrDefault(element =>
            element.ControlType == ControlType.Button && (NameOf(element).Contains("Hidden Icons", StringComparison.OrdinalIgnoreCase) || NameOf(element).Contains("Chevron")));
        if (arrow is null) return null;
        arrow.Click();
        return Retry.WhileNull(Search, TimeSpan.FromSeconds(10)).Result;
    }

    // Now and then the ARM64 build machine loses a right-click on the icon, so it is tried again.
    private static AutomationElement? MenuItemOf(AutomationElement icon, AppSession app, string name)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            icon.RightClick();
            if (Retry.WhileNull(() => MenuItem(app, name), TimeSpan.FromSeconds(5)).Result is { } item) return item;
        }
        return null;
    }

    private static AutomationElement? MenuItem(AppSession app, string name) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.ControlType == ControlType.Menu || window.ClassName == "#32768")
            .SelectMany(AppSession.Walk)
            .FirstOrDefault(element => element.ControlType == ControlType.MenuItem && NameOf(element) == name);

    /// <summary>Everything in the taskbar and in the pop-ups of the shell, but not in ordinary app windows.</summary>
    private static IEnumerable<AutomationElement> ShellElements(AppSession app) =>
        app.Automation.GetDesktop().FindAllChildren()
            .Where(window => window.ClassName is "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass")
            .SelectMany(AppSession.Walk);

    private static string NotificationAreaNames(AppSession app) =>
        string.Join(" | ", ShellElements(app).Select(element => $"{element.ControlType} '{NameOf(element)}'").Where(text => !text.EndsWith("''")).Take(80));

    private static string NameOf(AutomationElement element)
    {
        try { return element.Name ?? ""; }
        catch { return ""; } // gone, or it has no name
    }

    private static bool Eventually(Func<bool> condition) =>
        Retry.WhileFalse(condition, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Success;
}
