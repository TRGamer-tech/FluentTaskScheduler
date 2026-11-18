using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Every page of the app, and the navigation pane beside them, checked against the accessibility
/// rules of Axe.Windows. A scan judges what is in view, so a page that scrolls is scanned one
/// screenful after the other. A test that fails lists what was found, and every scan leaves a
/// file for Accessibility Insights for Windows in the artifacts.
/// </summary>
[Collection(AccessibilityApp.Collection)]
public sealed class PageAccessibilityTests(AccessibilityApp shared)
{
    [Fact]
    public void TheTaskList_WithTasksInIt_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();

        AccessibilityScan.AssertNothingFound("The task list", app.Scan(WindowPart.Page, "tasks"));
    }

    [Fact]
    public void TheDashboard_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavDashboard", "Dashboard");
        app.Require("TotalTasksFigure", 30000);
        // The run of the task that ran; the log is a moment behind what happens.
        app.Find(element => element.Is(ControlType.ListItem) && element.Parent?.AutomationId == "DashboardLog", 20000);

        AccessibilityScan.AssertNothingFound("The dashboard", app.ScanPageByPage(WindowPart.Page, "dashboard", "DashboardScroller"));
    }

    [Fact]
    public void TheQuickActions_BreakNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavQuickActions", "Quick Actions");

        AccessibilityScan.AssertNothingFound("The quick actions", app.ScanPageByPage(WindowPart.Page, "quick-actions", "QuickActionsScroller"));
    }

    /// <summary>
    /// The focus is on the button that started the action, so nothing would tell a screen reader
    /// how the action ended: the status next to the button says it without being asked.
    /// </summary>
    [Fact]
    public void HowAQuickActionEnded_IsReadOutByAScreenReader()
    {
        AppSession app = shared.Show("NavQuickActions", "Quick Actions");
        AutomationElement status = app.Require("QuickAction.FlushDNS.Status");
        int announcements = 0;
        using var listening = status.RegisterAutomationEvent(app.Automation.EventLibrary.Element.LiveRegionChangedEvent, TreeScope.Element,
            (_, _) => Interlocked.Increment(ref announcements));

        app.Require("QuickAction.FlushDNS.Run").AsButton().Invoke();

        Assert.True(app.Find(element => element.AutomationId == "QuickAction.FlushDNS.Status" && element.Name == "Success", 60000) != null,
            "Flush DNS did not report success.");
        Assert.Equal(LiveSetting.Polite, status.Properties.LiveSetting.ValueOrDefault);
        Assert.True(AppSession.Eventually(() => Volatile.Read(ref announcements) > 0, seconds: 10),
            "The status of the action changed without a screen reader being told.");
    }

    [Fact]
    public void TheLibrary_ShowingItsTemplates_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavScriptLibrary", "Library");
        app.Require("LibraryTemplatesTab").Select();
        app.Require("Template.TempCleanup", 20000);

        AccessibilityScan.AssertNothingFound("The templates of the library", app.ScanPageByPage(WindowPart.Page, "library-templates", "TemplatesView"));
    }

    [Fact]
    public void TheLibrary_ShowingItsScripts_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavScriptLibrary", "Library");
        app.Require("LibraryScriptsTab").Select();
        app.Require("Script.SystemFileChecker", 20000);

        AccessibilityScan.AssertNothingFound("The scripts of the library", app.ScanPageByPage(WindowPart.Page, "library-scripts", "ScriptsView"));
    }

    [Fact]
    public void TheScriptEditor_WithOutput_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavScriptEditor", "Script Editor");
        // What scripts write: lines, a blank line among them, and an error.
        app.Require("ScriptEditorText").AsTextBox().Text = "'fts-line'; ''; Write-Error 'fts-error'; 'fts-end'";
        AutomationElement run = app.Find(element => element.AutomationId == "ScriptEditorRun" && element.IsEnabled, 10000)
                                ?? throw new InvalidOperationException("Run did not become available.");
        run.AsButton().Invoke();
        Assert.True(app.Find(element => element.Name.StartsWith("[Exited with code", StringComparison.Ordinal), 60000) != null, "The script did not come to an end.");

        AccessibilityScan.AssertNothingFound("The script editor", app.Scan(WindowPart.Page, "script-editor"));
    }

    [Fact]
    public void TheSettings_WithEveryExpanderOpen_BreakNoAccessibilityRule()
    {
        AppSession app = shared.Show("NavSettings", "Settings");
        app.Require("ThemeChoice", 30000);
        foreach (string expander in new[] { "LoggingExpander", "ExecutionHistoryExpander", "CategoriesExpander", "TagsExpander" }) app.Expand(expander);

        AccessibilityScan.AssertNothingFound("The settings", app.ScanPageByPage(WindowPart.Page, "settings", "SettingsScroller"));
    }

    [Fact]
    public void TheNavigationPane_Open_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        Assert.True(PaneIsOpen(app), "The navigation pane is not open, in a window wide enough for it.");

        AccessibilityScan.AssertNothingFound("The window around the pages, with the navigation pane open", app.Scan(WindowPart.Frame, "pane-open"));
    }

    [Fact]
    public void TheNavigationPane_FoldedToIcons_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        app.Require("TogglePaneButton").Press();
        try
        {
            Assert.True(AppSession.Eventually(() => !PaneIsOpen(app), seconds: 10), "The navigation pane did not fold to icons.");

            AccessibilityScan.AssertNothingFound("The window around the pages, with the navigation pane folded to icons", app.Scan(WindowPart.Frame, "pane-folded"));
        }
        finally
        {
            app.Require("TogglePaneButton").Press();
        }
    }

    // The folders show in the open pane only.
    private static bool PaneIsOpen(AppSession app) => app.Find("ReloadFoldersButton", 0) is { IsOffscreen: false };
}
