using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The dialogs of the app, checked against the accessibility rules of Axe.Windows the way
/// <see cref="PageAccessibilityTests"/> checks the pages. Every dialog is left through the button
/// that does nothing: no task is changed, deleted, imported or snoozed.
/// </summary>
[Collection(AccessibilityApp.Collection)]
public sealed class DialogAccessibilityTests(AccessibilityApp shared) : IDisposable
{
    // Also after a test that failed before it got that far: its dialog would be in the way of every test after it.
    public void Dispose() => shared.CloseDialogs();

    [Fact]
    public void TheDetailsOfATask_WithItsHistory_BreakNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        app.OpenDetails(AccessibilityApp.RanOnce);

        // The run is in the history a moment after the fact. An event that is selected shows in full below the list.
        AutomationElement? run = null;
        Assert.True(AppSession.Eventually(() =>
        {
            run = app.Find(element => element.Is(ControlType.ListItem) && element.Parent?.AutomationId == "DetailsHistory", 1500);
            if (run is null) app.Require("DetailsReloadButton").Press();
            return run is not null;
        }, seconds: 40), "The history does not show the run of the task.");
        run!.Select();

        AccessibilityScan.AssertNothingFound("The details of a task", app.ScanPageByPage(WindowPart.Dialog, "details", "DetailsScroller"));
    }

    [Fact]
    public void TheTaskEditor_WithEverySectionOpen_BreaksNoAccessibilityRule()
    {
        AppSession app = OpenEditor();

        // One action of each kind. The first runs a program, and its fields show while it is selected.
        foreach (string kind in new[] { "EditorAddEmailAction", "EditorAddMessageAction" })
        {
            app.Require("EditorAddAction").Press();
            app.Require(kind).Press();
        }
        AutomationElement program = app.Find(element => element.Is(ControlType.ListItem) && element.Parent?.AutomationId == "EditorActions")
                                    ?? throw new InvalidOperationException("The editor lists no action.");
        program.Select();

        foreach (string section in new[] { "EditorConditions", "EditorSettings", "EditorAccount", "EditorCompletionActions" }) app.Expand(section);
        // The second account is that of another user, which asks for that user's name.
        AppSession.Walk(app.Require("EditorAccountOptions")).Where(element => element.Is(ControlType.RadioButton)).ElementAt(1).Select();
        app.Require("EditorUserName");

        AccessibilityScan.AssertNothingFound("The task editor", app.ScanPageByPage(WindowPart.Dialog, "editor", "EditorScroller"));
    }

    /// <summary>
    /// The task has a daily trigger, which shows with the whole editor in the test above. This one
    /// looks at what the other kinds of trigger have of their own. Triggers at log on, at startup,
    /// on idle and at one time have no field that a daily one does not have.
    /// </summary>
    [Fact]
    public void TheTaskEditor_ShowingTheOtherKindsOfTrigger_BreaksNoAccessibilityRule()
    {
        AppSession app = OpenEditor();
        var found = new Dictionary<string, AccessibilityViolation>();

        // The numbers are places in the list of kinds, which begins with one time, daily, weekly, monthly.
        Look(2, "TriggerWeeksInterval", "weekly");
        Look(3, "TriggerMonthlyMode", "monthly");
        Look(7, "TriggerEventLog", "event");
        app.Require("TriggerCustomQuery").Patterns.Toggle.Pattern.Toggle();
        app.Require("TriggerEventQuery");
        Scan("event-query");
        Look(8, "TriggerSessionChange", "session-change");

        AccessibilityScan.AssertNothingFound("The task editor's fields for a trigger", found.Values);

        void Look(int kind, string fieldOfItsOwn, string name)
        {
            ComboBox kinds = app.Require("TriggerTypeBox").AsComboBox();
            kinds.Select(kind);
            kinds.Collapse();
            app.Require(fieldOfItsOwn);
            Scan(name);
        }

        void Scan(string name)
        {
            // The focus brings the last of a trigger's fields into view, and with it those above it.
            app.Require("TriggerExpires").Focus();
            foreach (AccessibilityViolation violation in app.Scan(WindowPart.Dialog, "editor-trigger-" + name)) found.TryAdd(violation.Key, violation);
        }
    }

    [Fact]
    public void TheImportDialog_BreaksNoAccessibilityRule()
    {
        string file = Path.Combine(Path.GetTempPath(), $"FtsUiTests_Import_{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, TaskOfAnotherMachine, System.Text.Encoding.Unicode);
        try
        {
            AppSession app = shared.ShowTasks();
            app.ToolbarButton("ImportButton").PressWithKeyboard();
            app.ChooseFile(file);
            app.Require("ImportName", 20000);
            // The first choice keeps the account the task was exported with, which asks for its password.
            AppSession.Walk(app.Require("ImportAccount")).First(element => element.Is(ControlType.RadioButton)).Select();
            app.Require("ImportPassword");

            AccessibilityScan.AssertNothingFound("The import dialog", app.ScanPageByPage(WindowPart.Dialog, "import", "ImportScroller"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TheSnoozeDialog_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        app.ToolbarButton("SnoozeButton").Press();
        app.Require("SnoozeCustomItem").Press();
        app.Require("SnoozeDate", 20000);

        AccessibilityScan.AssertNothingFound("The snooze dialog", app.ScanPageByPage(WindowPart.Dialog, "snooze", MessageScroller));
    }

    [Fact]
    public void TheOverviewOfTheShortcuts_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        app.ToolbarButton("ShortcutsButton").Press();
        Assert.True(app.Find(element => element.Name == "Ctrl+N", 10000) != null, "The overview of the shortcuts did not open.");

        AccessibilityScan.AssertNothingFound("The overview of the shortcuts", app.ScanPageByPage(WindowPart.Dialog, "shortcuts", MessageScroller));
    }

    [Fact]
    public void TheWelcomeTour_BreaksNoAccessibilityRule()
    {
        AppSession app = OpenTour();

        AccessibilityScan.AssertNothingFound("The welcome tour", app.ScanPageByPage(WindowPart.Dialog, "tour", MessageScroller));
    }

    /// <summary>
    /// The focus stays on the button that moves on, which reads the same on every slide but the
    /// last. A screen reader is told what the new slide says, or it would say nothing.
    /// </summary>
    [Fact]
    public void MovingOnInTheWelcomeTour_TellsAScreenReaderWhatTheNewSlideSays()
    {
        AppSession app = OpenTour();
        string? said = null;
        using var listening = app.Require("TourPager").RegisterNotificationEvent(TreeScope.Element, (_, _, _, text, _) => Volatile.Write(ref said, text));

        app.Require("SecondaryButton").Press();

        Assert.True(AppSession.Eventually(() => Volatile.Read(ref said) is not null, seconds: 10), "The slide changed without a screen reader being told.");
        // The other slides are there too, but hidden from automation.
        string shown = app.Find(element => element.AutomationId == "TourSlideBody" && element.Properties.IsContentElement.ValueOrDefault)?.Name ?? "";
        Assert.True(shown.Length > 0, "The tour shows no slide.");
        Assert.Contains(shown, Volatile.Read(ref said));
    }

    /// <summary>The notes come from GitHub. Without them the app says so in a message, and there is nothing to look at.</summary>
    [SkippableFact]
    public void TheReleaseNotes_BreakNoAccessibilityRule_AndCanBeScrolledWithTheKeyboard()
    {
        AppSession app = shared.Show("NavSettings", "Settings");
        app.Require("ReleaseNotesLink", 30000).Press();
        AutomationElement dialog = app.Find(MessageScroller, 30000)?.Parent
                                   ?? throw new InvalidOperationException("Neither the release notes nor a message about them came up.");
        AutomationElement? notes = app.Find("NotesScroller", 1500);
        Skip.If(notes is null, $"There are no release notes to look at. The app says: {AppSession.TextIn(dialog)}");

        Assert.True(notes!.Properties.IsKeyboardFocusable.ValueOrDefault, "The notes cannot take the focus, so they cannot be scrolled with the keyboard.");
        AccessibilityScan.AssertNothingFound("The release notes", app.ScanPageByPage(WindowPart.Dialog, "release-notes", "NotesScroller"));
    }

    [Fact]
    public void TheQuestionBeforeATaskIsDeleted_BreaksNoAccessibilityRule()
    {
        AppSession app = shared.ShowTasks();
        app.OpenDetails(AccessibilityApp.RanOnce);
        app.Require("DetailsDeleteButton").Press();
        // The details make way for the question, which has a button that says yes.
        app.Require("PrimaryButton", 20000);

        AccessibilityScan.AssertNothingFound("The question before a task is deleted", app.ScanPageByPage(WindowPart.Dialog, "confirmation", MessageScroller));
    }

    [Fact]
    public void TheQuestionForTheNameOfAFolder_BreaksNoAccessibilityRule()
    {
        AppSession app = AskForAFolderName();

        AccessibilityScan.AssertNothingFound("The question for the name of a folder", app.ScanPageByPage(WindowPart.Dialog, "question", MessageScroller));
    }

    [Fact]
    public void AnErrorMessage_BreaksNoAccessibilityRule()
    {
        AppSession app = AskForAFolderName();
        // Task Scheduler allows no question mark in a name. The app says so and creates nothing.
        app.Require("PromptInput").AsTextBox().Text = "what?";
        app.Require("PrimaryButton").AsButton().Invoke();
        Assert.True(AppSession.Eventually(() => app.Find("PromptInput", 0) is null && app.Find(MessageScroller, 0) is not null),
            "The app did not say that the name is not allowed.");

        AccessibilityScan.AssertNothingFound("An error message", app.ScanPageByPage(WindowPart.Dialog, "error-message", MessageScroller));
    }

    /// <summary>What a dialog scrolls its text with when the window is too small for it: a part of every dialog.</summary>
    private const string MessageScroller = "ContentScrollViewer";

    /// <summary>
    /// Exported for an account this machine does not know, with its password stored: the dialog
    /// says so, asks which account to use, and for the password if it is to be that one.
    /// </summary>
    private const string TaskOfAnotherMachine = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Author>OTHERPC\someone</Author></RegistrationInfo>
          <Triggers><TimeTrigger><StartBoundary>2031-05-06T07:08:00</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>S-1-5-21-1111111111-2222222222-3333333333-1001</UserId>
              <LogonType>Password</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings><Enabled>true</Enabled><ExecutionTimeLimit>PT1H</ExecutionTimeLimit></Settings>
          <Actions Context="Author"><Exec><Command>ping</Command><Arguments>-n 1 127.0.0.1</Arguments></Exec></Actions>
        </Task>
        """;

    private AppSession OpenEditor()
    {
        AppSession app = shared.ShowTasks();
        app.OpenDetails(AccessibilityApp.FillsTheEditor);
        app.Require("DetailsEditButton").Press();
        app.Require("EditorName", 20000);
        return app;
    }

    private AppSession OpenTour()
    {
        AppSession app = shared.Show("NavSettings", "Settings");
        app.Require("WelcomeTourLink", 30000).Press();
        app.Require("TourPager", 10000);
        return app;
    }

    private AppSession AskForAFolderName()
    {
        AppSession app = shared.ShowTasks();
        app.Require("NewFolderButton").Press();
        app.Require("PromptInput", 20000);
        return app;
    }
}
