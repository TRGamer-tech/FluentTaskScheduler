using System.Drawing;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace FluentTaskScheduler.UITests;

/// <summary>The task list and the task editor, checked against what Task Scheduler itself reports afterwards.</summary>
public sealed class TaskTests : IDisposable
{
    private readonly WindowsTasks _windows = new();

    public void Dispose() => _windows.Dispose();

    [Fact]
    public void ATaskCreatedOutsideTheApp_IsListed()
    {
        _windows.CreateDaily("Made elsewhere");
        using var app = AppSession.Launch("listed");

        app.Require("SearchBox").AsTextBox().Enter("Made elsewhere");
        app.Snapshot("searched");

        Assert.True(app.Find(RowNamed("Made elsewhere"), 20000) != null, "The task created with schtasks is not in the list.");

        // Every row has the same check box and switch; a screen reader tells them apart by the task's name.
        Assert.True(app.FindByName("Select Made elsewhere") != null, "The row's check box does not say which task it selects.");
        Assert.True(app.FindByName("Enable Made elsewhere") != null, "The row's switch does not say which task it enables.");
    }

    [Fact]
    public void ANewWeeklyTask_IsSavedWithTheChosenDaysAndTime()
    {
        string name = "FtsUiTests_Weekly_" + Guid.NewGuid().ToString("N")[..8];
        using var app = AppSession.Launch("weekly");
        OpenFolderlessList(app);

        app.ToolbarButton("NewTaskButton").Press();
        app.Require("EditorName", 20000).AsTextBox().Text = name;
        app.Snapshot("editor-new");

        app.Require("TriggerTypeBox").AsComboBox().Select(2); // one time, daily, weekly, ...
        Toggle(app, "Wed");
        Toggle(app, "Fri");

        // A new trigger starts on the hour; picking 05 in the minutes makes it five past.
        var timeButton = app.Find(element => element.AutomationId == "FlyoutButton" && element.Parent?.AutomationId == "TriggerStartTime")
                         ?? throw new InvalidOperationException("The time picker has no button.");
        timeButton.Patterns.Invoke.Pattern.Invoke();
        var accept = app.Require("AcceptButton");
        var fivePast = app.Find(element => element.ControlType == ControlType.ListItem && element.Name == "05"
                                           && element.Parent?.AutomationId == "MinuteLoopingSelector")
                       ?? throw new InvalidOperationException("The minutes do not offer 05.");
        fivePast.Select();
        Wait();
        app.Snapshot("time-picker");
        accept.Patterns.Invoke.Pattern.Invoke();
        Wait();

        app.Require("EditorProgram").AsTextBox().Text = "cmd.exe";
        app.Require("EditorArguments").AsTextBox().Text = "/c exit 0";
        app.Snapshot("editor-filled");
        app.Require("PrimaryButton").AsButton().Invoke();

        try
        {
            Assert.True(AppSession.Eventually(() => WindowsTasks.Root.Exists(name)), "The task was not created. " + ProblemsShown(app));
            app.Snapshot("saved");

            string trigger = WindowsTasks.Root.Element(name, "Triggers", "CalendarTrigger")?.ToString() ?? "";
            Assert.Contains("ScheduleByWeek", trigger);
            Assert.Contains("Monday", trigger);
            Assert.Contains("Wednesday", trigger);
            Assert.Contains("Friday", trigger);
            Assert.DoesNotContain("Tuesday", trigger);

            DateTime start = DateTime.Parse(WindowsTasks.Root.Value(name, "Triggers", "CalendarTrigger", "StartBoundary")!);
            Assert.Equal(5, start.Minute);
            Assert.Equal(0, start.Second);
        }
        finally
        {
            WindowsTasks.Root.Delete(name);
        }
    }

    /// <summary>
    /// The tags box suggests the tags kept in the settings. Enter takes the suggestion picked in the
    /// list and, with no list open, saves the task, as Enter does in the editor's other fields.
    /// </summary>
    [Fact]
    public void ATagChosenFromTheSuggestions_CompletesTheOneTyped_AndEnterSavesTheTask()
    {
        string name = "FtsUiTests_Tags_" + Guid.NewGuid().ToString("N")[..8];
        using var app = AppSession.Launch("tags", AppSession.FirstRunDone(("SavedTags", new JsonArray("nightly", "disk"))));
        OpenFolderlessList(app);

        app.ToolbarButton("NewTaskButton").Press();
        app.Require("EditorName", 20000).AsTextBox().Text = name;
        app.Require("EditorProgram").AsTextBox().Text = "cmd.exe";
        var tags = app.Find(element => element.ControlType == ControlType.Edit && element.Parent?.AutomationId == "EditorTags")
                   ?? throw new InvalidOperationException("The tags box has no text box.");
        tags.AsTextBox().Enter("backup, ni");
        Wait(); // the box suggests once the typing pauses
        app.Snapshot("tag-suggested");
        Keyboard.Type(VirtualKeyShort.DOWN);
        Keyboard.Type(VirtualKeyShort.ENTER);
        Wait();
        app.Snapshot("tag-chosen");
        Keyboard.Type(VirtualKeyShort.ENTER);

        try
        {
            Assert.True(AppSession.Eventually(() => WindowsTasks.Root.Exists(name)), "Enter did not save the task. " + ProblemsShown(app));
            Assert.Contains("\"Tags\":[\"backup\",\"nightly\"]", WindowsTasks.Root.Value(name, "RegistrationInfo", "Description"));
        }
        finally
        {
            WindowsTasks.Root.Delete(name);
        }
    }

    [Fact]
    public void SwitchingOffTheRunTimeLimit_LeavesTheAccountAndEverythingElseAlone()
    {
        _windows.Create("Nightly", SystemTaskXml);
        using var app = AppSession.Launch("limit");

        app.Require("SearchBox").AsTextBox().Enter("Nightly");
        var row = app.Find(RowNamed("Nightly"), 20000) ?? throw new InvalidOperationException("The task is not in the list.");
        OpenEditor(app, row);
        app.Expand("EditorSettings");
        app.Snapshot("editor-settings");
        var limit = app.Require("EditorLimitRunTime").AsCheckBox();
        Assert.True(limit.IsChecked, "The editor does not show the task's run time limit.");
        limit.Toggle();
        Assert.Equal("Normal", app.Require("EditorPriority").AsComboBox().SelectedItem?.Name);
        app.Require("PrimaryButton").AsButton().Invoke();

        Assert.True(AppSession.Eventually(() => _windows.Value("Nightly", "Settings", "ExecutionTimeLimit") == "PT0S"),
            "The run time limit was not switched off. " + ProblemsShown(app));
        Assert.Equal("S-1-5-18", _windows.Value("Nightly", "Principals", "Principal", "UserId"));
        Assert.Equal("HighestAvailable", _windows.Value("Nightly", "Principals", "Principal", "RunLevel"));
        Assert.Equal("4", _windows.Value("Nightly", "Settings", "Priority"));
        Assert.Equal("true", _windows.Value("Nightly", "Settings", "WakeToRun"));
        Assert.Equal("PT45M", _windows.Value("Nightly", "Triggers", "CalendarTrigger", "Repetition", "Interval"));
        Assert.Contains("robocopy", _windows.Value("Nightly", "Actions", "Exec", "Command"));

        // Opened again, the limit stays off instead of coming back.
        row = app.Find(RowNamed("Nightly"), 20000)!;
        OpenEditor(app, row);
        app.Expand("EditorSettings");
        Assert.False(app.Require("EditorLimitRunTime").AsCheckBox().IsChecked, "The limit is shown as on again.");
        app.Snapshot("editor-reopened");
    }

    /// <summary>
    /// What other programs do to tasks shows up without pressing Refresh. The app learns of it
    /// from the history Windows records, which the test switches on for its duration.
    /// </summary>
    [Fact]
    public void ATaskCreatedAndDeletedElsewhereWhileTheListIsOpen_AppearsAndDisappearsByItself()
    {
        using var history = new HistoryLog();
        using var app = AppSession.Launch("live");
        OpenFolderlessList(app);
        Assert.True(app.Find(element => element.ControlType == ControlType.ListItem, 30000) != null, "The list stayed empty.");
        app.Require("SearchBox").AsTextBox().Enter("Arrives later");
        Assert.True(AppSession.Eventually(() => app.Find(RowNamed("Arrives later"), 500) == null), "A task of that name is already there.");

        _windows.CreateDaily("Arrives later");

        bool appeared = app.Find(RowNamed("Arrives later"), 30000) != null;
        app.Snapshot("appeared");
        Assert.True(appeared, "The task created with schtasks did not appear by itself.");

        _windows.Delete("Arrives later");

        Assert.True(AppSession.Eventually(() => app.Find(RowNamed("Arrives later"), 500) == null, seconds: 30), "The deleted task stayed in the list.");
    }

    [Fact]
    public void TheShortcutOverview_ListsTheKeysOfTheTaskList()
    {
        using var app = AppSession.Launch("shortcuts");

        app.ToolbarButton("ShortcutsButton").Press();

        Assert.True(app.Find(element => element.Name == "Ctrl+N", 10000) != null, "The overview did not open or does not list Ctrl+N.");
        Assert.NotNull(app.FindByName("Refresh the task list"));
        app.Snapshot("shortcuts");
    }

    [Fact]
    public void ATasksDetails_ShowWhatItRuns_AndAfterARunWhatBecameOfIt()
    {
        using var history = new HistoryLog(switchOn: false);
        _windows.CreateDaily("Detailed");
        using var app = AppSession.Launch("details");
        app.Require("SearchBox").AsTextBox().Enter("Detailed");
        AutomationElement Row() => app.Find(RowNamed("Detailed"), 20000) ?? throw new InvalidOperationException("The task is not in the list.");

        Row().Click();
        app.Require("DetailsRunButton", 20000);
        bool showsAction = app.FindByName("cmd.exe /c exit 0", ControlType.Text, 20000) != null;
        app.Snapshot("details-opened");
        Assert.True(showsAction, "The details do not show the task's action.");

        // Windows ships with task history switched off; the details offer to switch it on.
        if (app.Find("DetailsSwitchLogOnButton", 3000) is { } switchOn)
        {
            switchOn.Press();
            Assert.True(AppSession.Eventually(() => app.Find("DetailsSwitchLogOnButton", 500) is null), "History could not be switched on. " + ProblemsShown(app, "DetailsProblem"));
        }

        app.Require("DetailsRunButton").Press();
        Assert.True(AppSession.Eventually(() => app.Find("DetailsRunButton", 500) is null), "Run did not close the details.");

        // The log is written a moment after the fact. Meanwhile the list reads the task again and can
        // rebuild its row just as it is clicked, which the ARM64 build machine has seen.
        Assert.True(AppSession.Eventually(() =>
        {
            Row().Click();
            return app.Find("DetailsReloadButton", 3000) is not null;
        }), "The details did not open again after the run.");
        Assert.True(AppSession.Eventually(() =>
        {
            app.Require("DetailsReloadButton").Press();
            Thread.Sleep(1500);
            return app.Require("DetailsLastResult").Name == "Succeeded (0)";
        }, seconds: 40), $"The history does not show the run; last result reads '{app.Require("DetailsLastResult").Name}'. " + ProblemsShown(app, "DetailsProblem"));
        app.Snapshot("details-history");
        Assert.NotNull(app.Find(element => element.ControlType == ControlType.ListItem && element.Parent?.AutomationId == "DetailsHistory", 5000));
    }

    [Fact]
    public void InANarrowWindow_TheDetailsGiveWay_InsteadOfBeingCutOff()
    {
        _windows.CreateDaily("Seen narrow");
        // German has the longest texts.
        using var app = AppSession.Launch("details-narrow", AppSession.FirstRunDone(("Language", "de-DE")));
        app.Require("SearchBox").AsTextBox().Enter("Seen narrow");
        (app.Find(RowNamed("Seen narrow"), 20000) ?? throw new InvalidOperationException("The task is not in the list.")).Click();
        app.Require("DetailsRunButton", 20000);

        app.Resize(560, 800);
        app.Snapshot("narrow");

        // The last button of the task's row of buttons, and of the history's.
        Rectangle window = app.Window.BoundingRectangle;
        foreach (string id in new[] { "DetailsDeleteButton", "DetailsCopyButton" })
        {
            var control = app.Require(id);
            Rectangle bounds = control.BoundingRectangle;
            Assert.False(control.IsOffscreen, $"{id} cannot be seen.");
            Assert.True(bounds.Left >= window.Left && bounds.Right <= window.Right, $"{id} at {bounds} is not inside the window at {window}.");
        }
    }

    /// <summary>
    /// The list reuses its rows as it scrolls. A row's switch must never take that for the user
    /// switching a task on or off.
    /// </summary>
    [Fact]
    public void ScrollingThroughTheList_ChangesNoTask()
    {
        var before = WindowsTasks.EnabledStates();
        using var app = AppSession.Launch("scroll");
        OpenFolderlessList(app);
        Assert.True(app.Find(element => element.ControlType == ControlType.ListItem, 30000) != null, "The list stayed empty.");

        var scroll = app.Require("TaskList").Patterns.Scroll.Pattern;
        Assert.True(AppSession.Eventually(() => scroll.VerticallyScrollable.Value), "The list does not scroll, so no row is ever reused.");
        for (int page = 0; page < 300 && scroll.VerticalScrollPercent.Value < 100; page++)
        {
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            Thread.Sleep(40);
        }
        app.Snapshot("bottom");
        for (int page = 0; page < 300 && scroll.VerticalScrollPercent.Value > 0; page++)
        {
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            Thread.Sleep(40);
        }
        Wait();

        Assert.Null(app.OpenDialogText());
        var after = WindowsTasks.EnabledStates();
        string[] changed = before.Where(task => after.TryGetValue(task.Key, out bool enabled) && enabled != task.Value).Select(task => task.Key).ToArray();
        Assert.True(changed.Length == 0, "Scrolling switched these tasks on or off: " + string.Join(", ", changed));
    }

    [Fact]
    public void ATaskExportedForAnAccountUnknownHere_IsImportedForTheCurrentUser()
    {
        // The dialog names the task after the file.
        string name = "FtsUiTests_Foreign_" + Guid.NewGuid().ToString("N")[..8];
        string file = Path.Combine(Path.GetTempPath(), name + ".xml");
        File.WriteAllText(file, ForeignTaskXml, System.Text.Encoding.Unicode);
        using var app = AppSession.Launch("import");
        try
        {
            app.ToolbarButton("ImportButton").PressWithKeyboard();
            app.ChooseFile(file);

            app.Require("ImportName", 20000);
            app.Snapshot("import-dialog");
            Assert.Equal(name, app.Require("ImportName").AsTextBox().Text);
            Assert.True(app.Find("ImportAccountMissing", 2000) is { IsOffscreen: false }, "The dialog does not say that the account is unknown here.");
            app.Require("PrimaryButton").AsButton().Invoke();

            Assert.True(AppSession.Eventually(() => WindowsTasks.Root.Exists(name)), "The task was not imported. " + ProblemsShown(app, "ImportProblem"));
            string? user = WindowsTasks.Root.Value(name, "Principals", "Principal", "UserId");
            Assert.NotNull(user);
            Assert.DoesNotContain("1111111111", user);
            Assert.Contains("ping", WindowsTasks.Root.Value(name, "Actions", "Exec", "Command"));
            app.Snapshot("imported");
            Assert.Null(app.OpenDialogText());
        }
        finally
        {
            WindowsTasks.Root.Delete(name);
            File.Delete(file);
        }
    }

    [Fact]
    public void AFolder_CanBeCreatedAndDeletedFromTheTree()
    {
        // The test's own folder, which is removed afterwards if the test does not get to delete it.
        string name = _windows.Folder.TrimStart('\\');
        using var app = AppSession.Launch("folders");

        app.Require("NewFolderButton").Press();
        app.Require("PromptInput").AsTextBox().Text = name;
        app.Require("PrimaryButton").AsButton().Invoke();

        var folder = app.Find(element => element.ControlType == ControlType.TreeItem && element.Name == name, 20000);
        app.Snapshot("folder-created");
        Assert.True(folder != null, "The new folder is not in the tree.");

        folder.RightClick();
        var delete = app.Find(element => element.ControlType == ControlType.MenuItem && element.Name == "Delete", 5000)
                     ?? throw new InvalidOperationException("The folder's menu has no Delete entry.");
        delete.Patterns.Invoke.Pattern.Invoke();
        app.Require("PrimaryButton").AsButton().Invoke();

        Assert.True(AppSession.Eventually(() => app.Find(element => element.ControlType == ControlType.TreeItem && element.Name == name, 500) == null),
            "The folder is still in the tree after deleting it.");
    }

    [Fact]
    public void TheList_OpensOnTheFolderChosenLast_AndRemembersAChoiceOfAllTasks()
    {
        _windows.CreateDaily("Chosen last time");
        using var app = AppSession.Launch("last-folder", AppSession.FirstRunDone(("LastFolderPath", _windows.Folder)));

        Assert.True(app.Find(RowNamed("Chosen last time"), 20000) != null, "The list does not show the task of the folder chosen last.");
        app.Snapshot("opened");
        int rows = RowsIn(app);
        Assert.True(rows == 1, $"The list shows {rows} tasks instead of the one in the folder.");

        app.Require("NavAllTasks").Press();
        Assert.True(AppSession.Eventually(() => app.SavedSetting("LastFolderPath") == @"\"), "settings.json does not say that all tasks were chosen.");
    }

    [Fact]
    public void TheTree_HighlightsTheFolderTheListOpensOn()
    {
        _windows.CreateDaily("Chosen last time");
        using var app = AppSession.Launch("last-folder-tree", AppSession.FirstRunDone(("LastFolderPath", _windows.Folder)));

        string name = _windows.Folder.TrimStart('\\');
        var folder = app.Find(element => element.ControlType == ControlType.TreeItem && element.Name == name, 20000)
                     ?? throw new InvalidOperationException("The folder is not in the tree.");
        bool highlighted = AppSession.Eventually(() => folder.Patterns.SelectionItem.Pattern.IsSelected.Value);
        app.Snapshot("tree");
        Assert.True(highlighted, "The tree does not highlight the folder the list opened on.");
    }

    [Fact]
    public void NewTaskInThePane_IsForTheFolderShown_WhichStaysTheOneRemembered()
    {
        _windows.CreateDaily("Chosen last time");
        using var app = AppSession.Launch("last-folder-new-task", AppSession.FirstRunDone(("LastFolderPath", _windows.Folder)));
        Assert.True(app.Find(RowNamed("Chosen last time"), 20000) != null, "The list does not show the task of the folder chosen last.");

        app.Require("NavAdd").Press();
        app.Require("EditorName", 20000);
        app.Snapshot("editor");
        app.Require("CloseButton").Press();
        Assert.True(AppSession.Eventually(() => app.Find("EditorName", 500) == null), "The editor did not close.");

        // Had New task switched the list to all tasks, the task would have gone into the root folder.
        int rows = RowsIn(app);
        Assert.True(rows == 1, $"After the editor, the list shows {rows} tasks instead of the one in the folder.");
        Wait(); // settings are written half a second after a change
        Assert.Equal(_windows.Folder, app.SavedSetting("LastFolderPath"));
    }

    [Fact]
    public void ATemplateOpenedAfterPressingTheLibraryAgain_LeavesTheFolderChosenLast()
    {
        _windows.CreateDaily("Chosen last time");
        using var app = AppSession.Launch("last-folder-template", AppSession.FirstRunDone(("LastFolderPath", _windows.Folder)));
        Assert.True(app.Find(RowNamed("Chosen last time"), 20000) != null, "The list does not show the task of the folder chosen last.");

        // Pressing the entry that is selected already leaves an invocation pending in the pane,
        // which Windows App SDK 1.5 hands on to the entry the app selects next: All tasks, for
        // the template's editor.
        app.Require("NavScriptLibrary").Press();
        app.Require("Template.TempCleanup", 20000);
        app.Require("NavScriptLibrary").Press();
        app.Require("Template.TempCleanup").Press();
        app.Require("EditorName", 20000);
        app.Snapshot("editor");
        app.Require("CloseButton").Press();
        Assert.True(AppSession.Eventually(() => app.Find("EditorName", 500) == null), "The editor did not close.");

        Wait(); // settings are written half a second after a change
        Assert.Equal(_windows.Folder, app.SavedSetting("LastFolderPath"));
    }

    private const string ForeignTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Author>OTHERPC\someone</Author></RegistrationInfo>
          <Triggers><TimeTrigger><StartBoundary>2031-05-06T07:08:00</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>S-1-5-21-1111111111-2222222222-3333333333-1001</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings><Enabled>true</Enabled><ExecutionTimeLimit>PT1H</ExecutionTimeLimit></Settings>
          <Actions Context="Author"><Exec><Command>ping</Command><Arguments>-n 1 127.0.0.1</Arguments></Exec></Actions>
        </Task>
        """;

    private const string SystemTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Author>CORP\admin</Author><Description>Copies the day's work</Description></RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <Repetition><Interval>PT45M</Interval><Duration>PT6H</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
              <StartBoundary>2030-01-02T03:04:05</StartBoundary>
              <Enabled>true</Enabled>
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
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>true</WakeToRun>
            <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
            <Priority>4</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>robocopy</Command><Arguments>C:\a D:\b /MIR</Arguments></Exec></Actions>
        </Task>
        """;

    private static Func<AutomationElement, bool> RowNamed(string name) =>
        element => element.ControlType == ControlType.ListItem && element.Name == name;

    private static void OpenFolderlessList(AppSession app) => app.Require("NavAllTasks").Press();

    private static int RowsIn(AppSession app) => AppSession.Walk(app.Require("TaskList")).Count(element => element.ControlType == ControlType.ListItem);

    /// <summary>A click on a row shows the task's details; the editor is one button further.</summary>
    private static void OpenEditor(AppSession app, AutomationElement row)
    {
        row.Click();
        app.Require("DetailsEditButton", 20000).Press();
        app.Require("EditorName", 20000);
    }

    private static void Toggle(AppSession app, string name)
    {
        var button = app.Find(element => element.ControlType == ControlType.Button && element.Name == name && element.Patterns.Toggle.IsSupported)
                     ?? throw new InvalidOperationException($"No toggle button '{name}'.");
        button.Patterns.Toggle.Pattern.Toggle();
    }

    private static string ProblemsShown(AppSession app, string infoBar = "EditorProblems") =>
        app.Find(infoBar, 500) is { } bar ? $"The dialog says: {AppSession.TextIn(bar)}" : "";

    private static void Wait() => Thread.Sleep(600);
}
