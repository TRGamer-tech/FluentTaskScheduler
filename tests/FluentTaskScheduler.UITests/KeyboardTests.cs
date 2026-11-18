namespace FluentTaskScheduler.UITests;

/// <summary>
/// Every page walked through with the Tab key, from its first control until the focus is back
/// there: the controls a page is for have to be on the way, and the focus must not stop at
/// anything a screen reader has no name for.
/// </summary>
[Collection(AccessibilityApp.Collection)]
public sealed class KeyboardTests(AccessibilityApp shared)
{
    [Fact]
    public void OnTheTaskList_TabReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.ShowTasks();

        List<FocusStop> stops = app.TabAround("SearchBox");

        Check(app, "the task list", stops, "SearchBox", "StatusFilterBox", "NewTaskButton|RefreshButton|ImportButton|SortButton|SnoozeButton", "TaskList");
    }

    [Fact]
    public void OnTheDashboard_TabReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.Show("NavDashboard", "Dashboard");
        app.Require("TotalTasksFigure", 30000);

        List<FocusStop> stops = app.TabAround("CategoryChoice");

        Check(app, "the dashboard", stops, "CategoryChoice", "TagChoice", "DashboardRefreshButton", "LogFilterAll|LogFilterSucceeded|LogFilterFailed|LogFilterSnoozed");
    }

    [Fact]
    public void OnTheQuickActions_TabReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.Show("NavQuickActions", "Quick Actions");

        List<FocusStop> stops = app.TabAround("QuickAction.FlushDNS.Run");

        // The actions that run without administrator rights, whoever runs the tests.
        Check(app, "the quick actions", stops, "QuickAction.FlushDNS.Run", "QuickAction.ClearTemp.Run", "QuickAction.RestartExplorer.Run");
    }

    [Fact]
    public void InTheLibrary_TabReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.Show("NavScriptLibrary", "Library");
        app.Require("LibraryTemplatesTab").Select();
        app.Require("Template.TempCleanup", 20000);

        List<FocusStop> stops = app.TabAround("LibraryTemplatesTab");

        Check(app, "the library", stops, "LibraryTemplatesTab|LibraryScriptsTab", "LibrarySearch", "Template.TempCleanup");
    }

    /// <summary>
    /// The box for the script keeps Tab to indent with, and leaves Shift+Tab to the focus. So this
    /// walk goes backwards, the one way a keyboard gets out of the box.
    /// </summary>
    [Fact]
    public void InTheScriptEditor_ShiftTabLeavesTheScript_ReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.Show("NavScriptEditor", "Script Editor");

        List<FocusStop> stops = app.TabAround("ScriptEditorText", backwards: true);

        Check(app, "the script editor", stops, "ScriptEditorText", "ScriptEditorDescription", "ScriptEditorName", "ScriptEditorRun|ScriptEditorStop|ScriptEditorSave|ScriptEditorNew");
    }

    [Fact]
    public void InTheSettings_TabReachesWhatThePageIsFor_AndStopsAtNothingWithoutAName()
    {
        AppSession app = shared.Show("NavSettings", "Settings");
        app.Require("ThemeChoice", 30000);

        List<FocusStop> stops = app.TabAround("ThemeChoice");

        Check(app, "the settings", stops, "ThemeChoice", "LanguageChoice", "SmoothScrollingSwitch", "ConfirmDeleteSwitch", "ExportSettingsButton", "CheckForUpdatesButton", "ReportProblemLink");
    }

    /// <param name="expected">
    /// Automation ids of controls the walk has to pass. An entry with several ids, separated by |,
    /// is a toolbar or a row of tabs: Tab stops at one of them, and the arrow keys go on to the others.
    /// </param>
    private static void Check(AppSession app, string page, List<FocusStop> stops, params string[] expected)
    {
        app.Log($"The focus on its way through {page}: {KeyboardWalk.Listed(stops)}");

        FocusStop[] unnamed = stops.Where(stop => stop.Name.Length == 0).ToArray();
        Assert.True(unnamed.Length == 0,
            $"On its way through {page} the focus stops at {unnamed.Length} element(s) a screen reader has no name for: {KeyboardWalk.Listed(unnamed)}{Environment.NewLine}The whole way: {KeyboardWalk.Listed(stops)}");

        string[] missed = expected.Where(entry => !entry.Split('|').Any(id => stops.Any(stop => stop.IsOrIsIn(id)))).ToArray();
        Assert.True(missed.Length == 0, $"The focus does not get to {string.Join(", ", missed)} on its way through {page}: {KeyboardWalk.Listed(stops)}");
    }
}
