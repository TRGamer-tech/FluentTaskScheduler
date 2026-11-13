using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The dashboard, following what happens to tasks while it is open. It learns of that from the
/// history Windows records, which the tests switch on for their duration.
/// </summary>
public sealed class DashboardTests : IDisposable
{
    private readonly WindowsTasks _windows = new();

    public void Dispose() => _windows.Dispose();

    [Fact]
    public void TheDashboard_OpensUnderItsHeading_AndCountsATaskCreatedWhileItIsOpen()
    {
        using var history = new HistoryLog();
        using var app = AppSession.Launch("dashboard-counts");

        app.Require("NavDashboard").Press();

        Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == "Dashboard", 30000) != null, "The dashboard did not open under its heading.");
        int before = TotalTasks(app, 30000);
        app.Snapshot("opened");
        Assert.True(before > 0, "The dashboard counts no tasks.");

        _windows.CreateDaily("Counted");

        bool counted = Retry.WhileFalse(() => TotalTasks(app) > before, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), ignoreException: true).Success;
        app.Snapshot("counted");
        Assert.True(counted, $"The dashboard still counts {TotalTasks(app)} tasks after one was added to {before}.");
    }

    [Fact]
    public void ATaskThatRan_IsInTheLog_AndItsEntryLeadsToTheTaskInTheList()
    {
        using var history = new HistoryLog();
        _windows.CreateDaily("Logged");
        using var app = AppSession.Launch("dashboard-log");
        app.Require("NavDashboard").Press();
        app.Require("DashboardLog", 30000);

        _windows.Start("Logged");

        // An entry is named after its task, how the run went and when it ended. The log is a
        // moment behind what happens, and the end of a run is reported once it has settled.
        AutomationElement? entry = app.Find(e => e.ControlType == ControlType.ListItem && e.Name.StartsWith("Logged, Succeeded, ", StringComparison.Ordinal), 60000);
        app.Snapshot("logged");
        Assert.True(entry != null, "The run of the task did not appear in the dashboard's log.");

        // The log is the last part of the page; focus brings the entry into view for the click.
        entry.Focus();
        Assert.True(Retry.WhileFalse(() => !entry.IsOffscreen, TimeSpan.FromSeconds(10), ignoreException: true).Success, "The entry could not be brought into view.");
        entry.Click();

        Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == "Scheduled Tasks", 20000) != null, "The entry did not lead to the task list.");
        AutomationElement? row = app.Find(e => e.ControlType == ControlType.ListItem && e.Name == "Logged" && e.Parent?.AutomationId == "TaskList", 20000);
        app.Snapshot("task-list");
        Assert.True(row != null && row.Patterns.SelectionItem.Pattern.IsSelected.Value, "The task list does not show the task as selected.");
    }

    private static int TotalTasks(AppSession app, int timeoutMs = 5000) => int.Parse(app.Require("TotalTasksFigure", timeoutMs).Name);
}
