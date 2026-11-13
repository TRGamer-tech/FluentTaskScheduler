using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Snoozing all tasks and one task, checked against what Task Scheduler reports. Every test has a
/// data folder of its own, so no snooze outlives it; the snooze of all tasks keeps the default of
/// not suspending triggers, so it disables none of the machine's tasks.
/// </summary>
public sealed class SnoozeTests : IDisposable
{
    private readonly WindowsTasks _windows = new();

    public void Dispose() => _windows.Dispose();

    [Fact]
    public void SnoozingAllTasksFromTheToolbar_ShowsTheBanner_RefusesToStartATask_AndResumeEndsIt()
    {
        _windows.CreateDaily("Held back");
        using var app = AppSession.Launch("snooze-all");
        app.Require("SearchBox").AsTextBox().Enter("Held back");
        AutomationElement row = app.Find(RowNamed("Held back"), 20000) ?? throw new InvalidOperationException("The task is not in the list.");

        app.ToolbarButton("SnoozeButton").Press();
        app.Require("SnoozeHalfAnHourItem").Press();

        Assert.True(app.Find("SnoozeBannerResume", 10000) is { IsOffscreen: false }, "No banner says that all tasks are snoozed.");
        app.Snapshot("snoozed");

        row.RightClick();
        AutomationElement run = app.Find(element => element.ControlType == ControlType.MenuItem && element.Name == "Run Now", 5000)
                                ?? throw new InvalidOperationException("The task's menu has no Run Now entry.");
        run.Press();
        string? said = Retry.WhileNull(app.OpenDialogText, TimeSpan.FromSeconds(10)).Result;
        app.Snapshot("run-refused");
        Assert.True(said?.Contains("Tasks are snoozed") == true, $"Starting the task was not refused. The app says: {said ?? "nothing"}");
        (app.FindByName("OK", ControlType.Button) ?? throw new InvalidOperationException("The message has no OK button.")).Press();

        app.Require("SnoozeBannerResume").Press();
        Assert.True(AppSession.Eventually(() => app.Find("SnoozeBannerResume", 300) is null), "The banner stayed after Resume.");
    }

    [Fact]
    public void SnoozingATaskFromItsDetails_DisablesIt_AndResumeEnablesItAgain()
    {
        _windows.CreateDaily("Paused one");
        using var app = AppSession.Launch("snooze-task");
        app.Require("SearchBox").AsTextBox().Enter("Paused one");
        AutomationElement row = app.Find(RowNamed("Paused one"), 20000) ?? throw new InvalidOperationException("The task is not in the list.");

        row.Click();
        app.Require("DetailsSnoozeButton", 20000).Press();
        app.Require("DetailsSnoozeHalfAnHourItem").Press();

        Assert.True(AppSession.Eventually(() => !_windows.IsEnabled("Paused one")), "The snoozed task is still enabled in Task Scheduler. " + ProblemShown(app, "DetailsSnoozeProblem"));
        Assert.True(app.Find("DetailsSnoozeStatus", 10000) is { IsOffscreen: false }, "The details do not say until when the task is snoozed.");
        app.Snapshot("task-snoozed");

        app.Require("DetailsResumeButton").Press();

        Assert.True(AppSession.Eventually(() => _windows.IsEnabled("Paused one")), "Resume did not enable the task again.");
        Assert.True(AppSession.Eventually(() => app.Find("DetailsSnoozeButton", 300) is not null), "The details do not offer to snooze the task again.");
    }

    /// <summary>
    /// The dialog for a moment of the user's choosing, which the icon's menu opens as well. Its
    /// pickers start an hour from now, which is all the test needs of them.
    /// </summary>
    [Fact]
    public void SnoozingAllTasksUntilAPickedTime_ShowsTheBanner_AndResumeEndsIt()
    {
        using var app = AppSession.Launch("snooze-all-until");
        app.Require("SearchBox", 20000);

        app.ToolbarButton("SnoozeButton").Press();
        app.Require("SnoozeCustomItem").Press();
        app.Require("SnoozeDate", 20000);
        app.Require("SnoozeTime");
        Assert.False(app.Require("SnoozeSuspendTriggers").AsCheckBox().IsChecked == true, "The snooze would disable the tasks of this machine.");
        app.Snapshot("dialog");
        app.Require("PrimaryButton").AsButton().Invoke();

        Assert.True(app.Find("SnoozeBannerResume", 10000) is { IsOffscreen: false }, "No banner says that all tasks are snoozed. " + ProblemShown(app, "SnoozeProblem"));
        app.Snapshot("snoozed");

        app.Require("SnoozeBannerResume").Press();
        Assert.True(AppSession.Eventually(() => app.Find("SnoozeBannerResume", 300) is null), "The banner stayed after Resume.");
    }

    [Fact]
    public void SnoozingATaskUntilAPickedTime_DisablesIt_AndResumeEnablesItAgain()
    {
        _windows.CreateDaily("Paused until");
        using var app = AppSession.Launch("snooze-task-until");
        app.Require("SearchBox").AsTextBox().Enter("Paused until");
        AutomationElement row = app.Find(RowNamed("Paused until"), 20000) ?? throw new InvalidOperationException("The task is not in the list.");

        row.Click();
        app.Require("DetailsSnoozeButton", 20000).Press();
        app.Require("DetailsSnoozeCustomItem").Press();

        // The details close to make way for the dialog.
        app.Require("SnoozeTime", 20000);
        app.Snapshot("dialog");
        app.Require("PrimaryButton").AsButton().Invoke();

        Assert.True(AppSession.Eventually(() => !_windows.IsEnabled("Paused until")), "The snoozed task is still enabled in Task Scheduler. " + ProblemShown(app, "SnoozeProblem"));

        row = app.Find(RowNamed("Paused until"), 20000) ?? throw new InvalidOperationException("The task is no longer in the list.");
        row.Click();
        Assert.True(app.Find("DetailsSnoozeStatus", 20000) is { IsOffscreen: false }, "The details do not say until when the task is snoozed.");
        app.Snapshot("task-snoozed");

        app.Require("DetailsResumeButton").Press();

        Assert.True(AppSession.Eventually(() => _windows.IsEnabled("Paused until")), "Resume did not enable the task again.");
    }

    private static Func<AutomationElement, bool> RowNamed(string name) =>
        element => element.ControlType == ControlType.ListItem && element.Name == name;

    /// <summary>What the bar with this automation id says, for the message of a failed assertion.</summary>
    private static string ProblemShown(AppSession app, string barId) =>
        app.Find(barId, 500) is { } bar ? $"The app says: {AppSession.TextIn(bar)}" : "";
}
