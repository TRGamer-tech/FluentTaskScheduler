using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The pictures of the README's "Screenshots" section: the app as it is, in English and in its
/// dark theme, in a window of 1280 by 800 pixels, with tasks that could be somebody's own (see
/// <see cref="DemoTasks"/>). They are only taken when asked for, by setting FTS_README_SCREENSHOTS
/// to 1. They go to the folder "readme" of the tests' artifacts, under the names they have in
/// Assets/, for someone to look at and to copy there.
/// </summary>
/// <remarks>
/// Every picture has an app of its own, so one that fails costs none of the others. What the
/// user in a picture would have done to get there is done with the mouse: an element that UI
/// Automation activates draws the frame of the keyboard focus around itself and brings up its
/// tooltip, which is not what someone who clicks gets to see.
/// </remarks>
public sealed class ReadmeScreenshotTests(DemoTasks demo) : IClassFixture<DemoTasks>
{
    private const string Request = "FTS_README_SCREENSHOTS";

    private static readonly Size PictureSize = new(1280, 800);

    [SkippableFact]
    public void TheTaskList_WithTheTasksOfOneFolder()
    {
        using AppSession app = Open("overview", onTheFolder: true);

        ClickFolder(app);
        Assert.True(AppSession.Eventually(() => FolderInTree(app).Patterns.SelectionItem.Pattern.IsSelected.Value), "The tree does not highlight the folder.");
        WaitForTheTasks(app);
        Assert.True(AppSession.Eventually(() => StateOf(Row(app, DemoTasks.VerifyBackup)) == "Running"), "The list does not show the running task as running.");

        Save(app, "1-Overview");
    }

    /// <summary>
    /// The dashboard of the backups alone. With every task of the machine on it, the tasks of
    /// Windows would fill its lists, and the runs other tests left behind its log.
    /// </summary>
    [SkippableFact]
    public void TheDashboard_NarrowedToTheBackups()
    {
        using AppSession app = Open("dashboard");
        Go(app, "NavDashboard", "Dashboard");
        app.Require("TotalTasksFigure", 60000);

        AutomationElement? Backups() =>
            app.Find(element => element.ControlType == ControlType.ListItem && element.Name == DemoTasks.BackupCategory && !element.IsOffscreen, 3000);
        Click(() => app.Require("CategoryChoice"), () => Backups() is not null, "The list of categories did not open, or does not offer the backups.");
        Thread.Sleep(600); // it animates open
        string backups = DemoTasks.NamesIn(DemoTasks.BackupCategory).Count.ToString();
        Click(() => Backups() ?? throw new InvalidOperationException("The list of categories has closed."),
            () => AppSession.Eventually(() => app.Require("TotalTasksFigure").Name == backups, seconds: 3),
            $"The dashboard does not count the {backups} backups after their category was chosen.");

        Assert.True(app.Find(element => element.ControlType == ControlType.ListItem && element.Name == DemoTasks.VerifyBackup
                                        && element.Parent?.AutomationId == "RunningList", 30000) != null,
            "The dashboard does not list the running task.");
        Assert.True(LogEntry(app, DemoTasks.SyncPhotos, "Failed") != null, "The log does not show the run that failed.");
        AutomationElement newest = LogEntry(app, DemoTasks.VerifyBackup, "Running")
                                   ?? throw new InvalidOperationException("The log does not show the run that is going on.");
        Rectangle frame = TopLevelWindows.FrameOf(app.Window.Properties.NativeWindowHandle.Value);
        Assert.True(frame.Contains(newest.BoundingRectangle), $"The newest entry of the log at {newest.BoundingRectangle} is not in the picture, which shows {frame}.");

        Save(app, "2-Dashboard");
    }

    [SkippableFact]
    public void TheQuickActions_AfterOneOfThemRan()
    {
        using AppSession app = Open("quick-actions");
        Go(app, "NavQuickActions", "Quick Actions");

        Click(() => app.Require("QuickAction.FlushDNS.Run"),
            () => app.Find(element => element.AutomationId == "QuickAction.FlushDNS.Status" && element.Name.Length > 0, 3000) != null,
            "A click on Run did not start the quick action.");
        Assert.True(app.Find(element => element.AutomationId == "QuickAction.FlushDNS.Status" && element.Name == "Success", 60000) != null,
            "Flushing the DNS cache did not report success.");

        Save(app, "3-QuickActions");
    }

    [SkippableFact]
    public void TheLibrary_WithItsTaskTemplates()
    {
        using AppSession app = Open("library");
        Go(app, "NavScriptLibrary", "Library");
        app.Require("Template.TempCleanup", 20000);

        Save(app, "4-ScriptLibrary");
    }

    [SkippableFact]
    public void TheScriptEditor_AfterAScriptRan()
    {
        using AppSession app = Open("script-editor");
        Go(app, "NavScriptEditor", "Script Editor");
        app.Require("ScriptEditorName").AsTextBox().Text = "Disk space report";
        app.Require("ScriptEditorDescription").AsTextBox().Text = "Lists how much room is left on every drive.";
        app.Require("ScriptEditorText").AsTextBox().Text = string.Join('\r',
            "# How much room is left on every drive",
            "Get-CimInstance Win32_LogicalDisk -Filter 'DriveType = 3' | ForEach-Object {",
            "    '{0}  {1,6:N1} GB free of {2,6:N1} GB' -f $_.DeviceID, ($_.FreeSpace / 1GB), ($_.Size / 1GB)",
            "}");

        Click(() => app.Find(element => element.AutomationId == "ScriptEditorRun" && element.IsEnabled, 10000)
                    ?? throw new InvalidOperationException("Run is not available."),
            () => app.Find(element => element.Parent?.AutomationId == "ScriptEditorOutput", 3000) != null,
            "A click on Run did not start the script.");
        Assert.True(app.Find(element => element.Name == "[Exited with code 0]", 60000) != null, "The output does not say that the script ran to its end.");

        Save(app, "5-ScriptEditor");
    }

    [SkippableFact]
    public void TheSettings_FromTheTop()
    {
        using AppSession app = Open("settings");
        Go(app, "NavSettings", "Settings");
        Assert.Equal("Dark", app.Require("ThemeChoice", 30000).AsComboBox().SelectedItem?.Name);

        Save(app, "6-Settings");
    }

    [SkippableFact]
    public void TheTaskEditor_OpenOnATask()
    {
        using AppSession app = Open("task-editor", onTheFolder: true);

        // A click on a row shows the task's details, in a dialog over the list; the editor is one
        // button further. The dialog is given time to open: a second click would go to whatever
        // it has where the row was.
        Click(() => Row(app, DemoTasks.SyncPhotos), () => app.Find("DetailsEditButton", 10000) is not null, "A click on the task did not show its details.");
        app.Require("DetailsEditButton").Press();
        app.Require("EditorName", 20000);
        Assert.True(app.Find(element => element.ControlType == ControlType.ListItem && element.Parent?.AutomationId == "EditorTriggers"
                                        && element.Name.StartsWith("Weekly (Sat, Sun)", StringComparison.Ordinal), 20000) != null,
            "The editor does not show the trigger of the task.");

        Save(app, "7-Add-And-Edit");
    }

    /// <summary>
    /// The dialog that snoozes all tasks until a moment of the user's choosing, as it opens. It is
    /// never confirmed, so nothing is snoozed.
    /// </summary>
    [SkippableFact]
    public void TheSnoozeDialog_ForAllTasks()
    {
        using AppSession app = Open("snooze", onTheFolder: true);
        ClickFolder(app);
        WaitForTheTasks(app);

        // Through UI Automation: a click on an entry of the toolbar's menu does not always arrive.
        app.ToolbarButton("SnoozeButton").Press();
        app.Require("SnoozeCustomItem").Press();
        app.Require("SnoozeDate", 20000);

        Save(app, "8-Snooze-All");
    }

    /// <summary>Starts the app the way the pictures show it, with the demo tasks in place.</summary>
    /// <param name="picture">Names the folder for what the test saw, which every test leaves behind.</param>
    /// <param name="onTheFolder">The task list shows the folder of the demo tasks instead of the tasks of all folders.</param>
    private AppSession Open(string picture, bool onTheFolder = false)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(Request) == "1", $"The pictures of the README are only taken when {Request} is 1.");
        demo.Prepare();

        var app = AppSession.Launch("readme-" + picture, AppSession.FirstRunDone(
            ("Theme", "Dark"),
            ("WindowWidth", PictureSize.Width),
            ("WindowHeight", PictureSize.Height),
            ("LastFolderPath", onTheFolder ? @"\" + DemoTasks.Folder : @"\")));
        try
        {
            GiveThePictureSize(app);
            return app;
        }
        catch
        {
            // Keeps the app's logs and last screen, as the test's own using would have.
            app.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Makes what can be seen of the window as large as the pictures are to be. A window is given
    /// its size with the border that cannot be seen, which the pictures leave out.
    /// </summary>
    private static void GiveThePictureSize(AppSession app)
    {
        nint window = app.Window.Properties.NativeWindowHandle.Value;
        int dpi = TopLevelWindows.DpiOf(window);
        Assert.True(dpi == 96, $"The pictures of the README are taken at a display scale of 100 %; this display is at {dpi * 100 / 96} %.");

        Size border = app.Window.BoundingRectangle.Size - TopLevelWindows.FrameOf(window).Size;
        app.Resize(PictureSize.Width + border.Width, PictureSize.Height + border.Height);

        Size seen = TopLevelWindows.FrameOf(window).Size;
        Assert.True(seen == PictureSize,
            $"The window is {seen.Width} by {seen.Height} pixels instead of {PictureSize.Width} by {PictureSize.Height}; the screen has {app.Screen.Width} by {app.Screen.Height}.");
    }

    /// <summary>
    /// Clicks with the mouse, again if need be, until the click has done what it is for. On the
    /// build machines a click now and then does not arrive.
    /// </summary>
    /// <param name="target">Finds what to click, anew for every click.</param>
    /// <param name="hasWorked">Whether the click has had its effect. It is asked right after a click, so it waits a moment for the effect itself.</param>
    private static void Click(Func<AutomationElement> target, Func<bool> hasWorked, string otherwise)
    {
        target().Click();
        Assert.True(AppSession.Eventually(() =>
        {
            if (hasWorked()) return true;
            target().Click();
            return false;
        }, seconds: 60), otherwise);
    }

    /// <summary>Goes to a page the way a user with a mouse does, and waits for it to open under its heading.</summary>
    private static void Go(AppSession app, string paneEntry, string heading) =>
        Click(() => app.Require(paneEntry, 30000),
            () => app.Find(element => element.AutomationId == "PageTitle" && element.Name == heading, 5000) != null,
            $"The heading is not '{heading}' after a click on {paneEntry}.");

    /// <summary>
    /// Clicks the folder of the demo tasks in the tree. The list has opened on that folder already;
    /// the click is what would have brought the user there, and it takes the keyboard focus, with
    /// its frame, off the button of the pane, where a window starts out with it.
    /// </summary>
    private static void ClickFolder(AppSession app) =>
        Click(() => FolderInTree(app),
            () => !app.Require("TogglePaneButton").Properties.HasKeyboardFocus.ValueOrDefault,
            "A click on the folder in the tree did not take the keyboard focus off the button of the pane.");

    private static AutomationElement FolderInTree(AppSession app) =>
        app.Find(element => element.ControlType == ControlType.TreeItem && element.Name == DemoTasks.Folder, 30000)
        ?? throw new InvalidOperationException("The folder of the demo tasks is not in the tree.");

    private static AutomationElement Row(AppSession app, string task) =>
        app.Find(element => element.ControlType == ControlType.ListItem && element.Name == task && element.Parent?.AutomationId == "TaskList", 30000)
        ?? throw new InvalidOperationException($"The task '{task}' is not in the list.");

    private static void WaitForTheTasks(AppSession app)
    {
        foreach (string task in DemoTasks.Names) Row(app, task);
    }

    /// <summary>What the icon in a task's row says about the task, e.g. "Ready".</summary>
    private static string StateOf(AutomationElement row) =>
        AppSession.Walk(row).FirstOrDefault(element => element.Is(ControlType.Image))?.NameOrEmpty() ?? "";

    /// <summary>The entry of the dashboard's log for a run of a task that ended, or stands, the given way.</summary>
    private static AutomationElement? LogEntry(AppSession app, string task, string status) =>
        app.Find(element => element.ControlType == ControlType.ListItem && element.Parent?.AutomationId == "DashboardLog"
                            && element.Name.StartsWith($"{task}, {status}, ", StringComparison.Ordinal), 30000);

    /// <summary>
    /// Takes the picture of the window as it is now, once the pointer is out of the way and what
    /// it left behind has gone.
    /// </summary>
    private static void Save(AppSession app, string name)
    {
        nint window = app.Window.Properties.NativeWindowHandle.Value;
        Rectangle frame = TopLevelWindows.FrameOf(window);

        // On the title bar the pointer is over nothing that lights up under it or has a tooltip to show.
        Mouse.Position = new Point(frame.Left + frame.Width / 2, frame.Top + 10);
        Assert.True(AppSession.Eventually(() => !AppSession.Walk(app.Window).Any(element => element.Is(ControlType.ToolTip))),
            "A tooltip stays open over the window.");
        Thread.Sleep(1000); // what the pointer was over fades back, and a dialog that has just opened comes to rest

        // Behind a window of another program, the app's would have a faded title bar and a plain colour in place of Mica.
        app.Window.WaitForTheForeground();

        string folder = Path.Combine(Path.GetDirectoryName(app.ArtifactsDir)!, "readme");
        Directory.CreateDirectory(folder);
        using Bitmap picture = WindowPicture.Take(window);
        picture.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }
}
