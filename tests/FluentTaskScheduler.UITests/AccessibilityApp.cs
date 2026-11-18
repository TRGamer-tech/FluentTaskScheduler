using System.Text.Json.Nodes;
using System.Xml.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The app as the accessibility tests look at it, started once for all of them: they change
/// nothing, and starting the app for every page and dialog would take longer than looking at
/// them does. Task Scheduler has two tasks of theirs: one that has run, so that there is a
/// history to show, and one with something in every part of the task editor.
/// </summary>
public sealed class AccessibilityApp : IDisposable
{
    public const string Collection = "Accessibility";

    /// <summary>A daily task that was started once when the tests began.</summary>
    public const string RanOnce = "Ran once";

    /// <summary>A task that uses every part of the editor. It is disabled, so it never runs.</summary>
    public const string FillsTheEditor = "Fills the editor";

    private readonly HistoryLog _history = new();
    private readonly WindowsTasks _tasks = new();

    public AccessibilityApp()
    {
        try
        {
            _tasks.CreateDaily(RanOnce);
            _tasks.Create(FillsTheEditor, TaskThatFillsTheEditor(startsOnSuccess: _tasks.PathOf(RanOnce)));
            _tasks.Start(RanOnce);

            App = AppSession.Launch("accessibility", AppSession.FirstRunDone(
                ("LastFolderPath", _tasks.Folder), ("SavedCategories", new JsonArray("Backups")), ("SavedTags", new JsonArray("nightly"))));

            // A scan judges what shows, so a taller window means fewer scans of a long page. On a
            // screen the window already fills, it stays as it is.
            int tallest = App.Screen.Height - 80;
            if (tallest > App.Window.BoundingRectangle.Height) App.Resize(App.Window.BoundingRectangle.Width, tallest);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AppSession App { get; }

    /// <summary>Shows a page through its entry in the navigation pane, unless it is showing already, and waits for its heading.</summary>
    public AppSession Show(string navigationItem, string heading)
    {
        if (App.HasExited) throw new InvalidOperationException("The app has ended, in this test or in one before it.");

        if (App.Find("PageTitle", 2000)?.NameOrEmpty() != heading) App.Require(navigationItem).Press();
        if (App.Find(element => element.AutomationId == "PageTitle" && element.Name == heading, 30000) is null)
            throw new InvalidOperationException($"The page '{heading}' did not open.");
        return App;
    }

    /// <summary>
    /// Shows the task list with the two tasks of these tests and none of the machine's own. The
    /// entry of the navigation pane lists the tasks of all folders; searching for the tests'
    /// folder leaves the two.
    /// </summary>
    public AppSession ShowTasks()
    {
        Show("NavAllTasks", "Scheduled Tasks");
        if (!ListsTheTwoTasks()) App.Require("SearchBox").TypeText(_tasks.Folder.TrimStart('\\'));
        if (!AppSession.Eventually(ListsTheTwoTasks)) throw new InvalidOperationException("The task list does not show the two tasks of the accessibility tests.");
        return App;
    }

    /// <summary>
    /// Closes the dialog a test has open, through the button every dialog of the app has for
    /// leaving it without doing anything. A dialog left open would fail every test after it.
    /// </summary>
    public void CloseDialogs()
    {
        for (int closed = 0; closed < 3 && !App.HasExited && App.Find("ContentScrollViewer", 0)?.Parent is { } dialog; closed++)
        {
            dialog.FindAllChildren().FirstOrDefault(child => child.Properties.AutomationId.ValueOrDefault == "CloseButton")?.Press();
            AppSession.Eventually(() => App.Find("ContentScrollViewer", 0) is null, seconds: 5);
        }
    }

    public void Dispose()
    {
        try
        {
            App?.Dispose();
        }
        finally
        {
            _tasks.Dispose();
            _history.Dispose();
        }
    }

    private bool ListsTheTwoTasks() =>
        AppSession.Walk(App.Require("TaskList"))
            .Where(element => element.Is(ControlType.ListItem))
            .Select(element => element.NameOrEmpty())
            .Order(StringComparer.Ordinal)
            .SequenceEqual([FillsTheEditor, RanOnce]);

    /// <summary>
    /// A task as an export of Task Scheduler describes it, with a daily trigger that repeats and
    /// expires, two actions, every condition and a restart on failure. The app keeps a task's
    /// category, tags and completion actions at the end of its description.
    /// </summary>
    private static string TaskThatFillsTheEditor(string startsOnSuccess)
    {
        var kept = new JsonObject
        {
            ["Category"] = "Backups",
            ["Tags"] = new JsonArray("nightly"),
            ["Pipeline"] = new JsonObject { ["IsEnabled"] = true, ["OnSuccessTasks"] = new JsonArray(startsOnSuccess), ["OnFailureTasks"] = new JsonArray() }
        };
        var description = new XText($"Copies the day's work\n\n<!-- FTS_META:{kept.ToJsonString()} -->");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>CORP\admin</Author><Description>{description}</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <Repetition><Interval>PT45M</Interval><Duration>PT6H</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
                  <StartBoundary>2030-01-02T03:04:05</StartBoundary>
                  <EndBoundary>2040-01-02T03:04:05</EndBoundary>
                  <Enabled>true</Enabled>
                  <RandomDelay>PT10M</RandomDelay>
                  <ScheduleByDay><DaysInterval>2</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <IdleSettings><Duration>PT10M</Duration><WaitTimeout>PT1H</WaitTimeout><StopOnIdleEnd>true</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>false</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>true</RunOnlyIfIdle>
                <WakeToRun>true</WakeToRun>
                <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
                <Priority>4</Priority>
                <RestartOnFailure><Interval>PT5M</Interval><Count>3</Count></RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>robocopy</Command><Arguments>C:\a D:\b /MIR</Arguments><WorkingDirectory>C:\</WorkingDirectory></Exec>
                <Exec><Command>cmd.exe</Command><Arguments>/c exit 0</Arguments></Exec>
              </Actions>
            </Task>
            """;
    }
}

[CollectionDefinition(AccessibilityApp.Collection)]
public sealed class AccessibilityCollection : ICollectionFixture<AccessibilityApp>;
