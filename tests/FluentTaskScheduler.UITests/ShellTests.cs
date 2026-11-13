using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The window frame: navigation between pages, the pane, the app language, the window's dialogs.</summary>
public class ShellTests
{
    private static readonly (string Item, string Heading)[] Pages =
    [
        ("NavAllTasks", "Scheduled Tasks"),
        ("NavDashboard", "Dashboard"),
        ("NavQuickActions", "Quick Actions"),
        ("NavScriptLibrary", "Library"),
        ("NavScriptEditor", "Script Editor"),
        ("NavSettings", "Settings")
    ];

    [Fact]
    public void EveryPageOpens_UnderItsOwnHeading()
    {
        using var app = AppSession.Launch("pages");
        app.Snapshot("start");

        foreach (var (item, heading) in Pages)
        {
            app.Require(item).Press();

            // A page may take a while to load its data on a slow machine; the heading follows the page.
            Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == heading, timeoutMs: 30000) != null, $"The heading is not '{heading}' after choosing {item}.");
            app.Snapshot(item);
            Assert.False(app.HasExited, $"The app exited after choosing {item}.");
        }
    }

    /// <summary>
    /// The app opens its window at the size it last had, maybe on a larger screen. The size here
    /// is larger than any screen the tests run on, so the window has to shrink and move to fit.
    /// </summary>
    [Fact]
    public void TheWindow_OpensInsideTheScreen_EvenAtASizeRememberedFromALargerOne()
    {
        using var app = AppSession.Launch("placement", AppSession.FirstRunDone(("WindowWidth", 4000), ("WindowHeight", 3000)));

        var window = app.Window.BoundingRectangle;
        var screen = app.Screen;
        app.Log($"Window {window}, screen {screen}");
        Assert.True(screen.Contains(window), $"The window {window} reaches beyond the screen {screen}.");
    }

    [Fact]
    public void StartingTheAppAgain_BringsBackTheWindowThatIsAlreadyOpen()
    {
        using var app = AppSession.Launch("second-start");
        var window = app.Window.Patterns.Window.Pattern;
        window.SetWindowVisualState(WindowVisualState.Minimized);
        Assert.True(Retry.WhileFalse(() => window.WindowVisualState.Value == WindowVisualState.Minimized, TimeSpan.FromSeconds(10)).Success,
            "The window could not be minimized.");

        app.StartAgain();

        Assert.True(Retry.WhileFalse(() => window.WindowVisualState.Value == WindowVisualState.Normal, TimeSpan.FromSeconds(10)).Success,
            "The first instance's window stayed minimized.");
    }

    [Fact]
    public void TheNavigationPane_ShrinksToIcons_ThenToAMenuButton_AsTheWindowNarrows()
    {
        using var app = AppSession.Launch("pane");

        app.Resize(1700, 900);
        app.Snapshot("wide");
        int wide = WidthOf(app, "NavDashboard");

        app.Resize(900, 800);
        app.Snapshot("medium");
        int medium = WidthOf(app, "NavDashboard");

        app.Resize(560, 800);
        app.Snapshot("narrow");
        var narrowItem = app.Find("NavDashboard", 1500);
        bool hiddenWhenNarrow = narrowItem == null || narrowItem.IsOffscreen;

        app.Log($"NavDashboard width: wide {wide}, medium {medium}; hidden when narrow: {hiddenWhenNarrow}");
        Assert.True(wide > 200, $"At 1700 px the pane should show labels, but its items are {wide} px wide.");
        Assert.True(medium is > 0 and < 80, $"At 900 px the pane should show icons only, but its items are {medium} px wide.");
        Assert.True(hiddenWhenNarrow, "At 560 px the pane should be closed.");

        app.Resize(1700, 900);
        Assert.True(WidthOf(app, "NavDashboard") > 200, "The pane should open again when the window is wide again.");
    }

    [Fact]
    public void TheChosenLanguage_IsUsed_WhateverWindowsIsSetTo()
    {
        using var app = AppSession.Launch("german", AppSession.FirstRunDone(("Language", "de-DE")));
        app.Snapshot("start");

        Assert.Equal("Einstellungen", app.Require("NavSettings").Name);
        Assert.Equal("Geplante Aufgaben", app.Require("PageTitle").Name);
    }

    /// <summary>
    /// Imported settings in another language rebuild the window under the message that says they
    /// were imported. The rebuilt window asks at once for the welcome tour, which the imported
    /// settings have not seen yet, and the tour has to wait for that message.
    /// </summary>
    [Fact]
    public void ADialogOfARebuiltWindow_WaitsForTheMessageThatWasOpenBeforeTheRebuild()
    {
        using var app = AppSession.Launch("import-rebuild");
        string file = Path.Combine(app.DataDir, "imported-settings.json");
        File.WriteAllText(file, AppSession.FirstRunDone(("Language", "de-DE"), ("HasCompletedOnboarding", false)));

        app.Require("NavSettings").Press();
        app.Require("ImportSettingsButton", 30000).PressWithKeyboard();
        app.ChooseFile(file);

        // The message is worded before the language changes; the page under it already is in German.
        Assert.True(app.FindByName("Import Successful", timeoutMs: 20000) != null, "The message that the settings were imported did not come up.");
        Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == "Einstellungen", 10000) != null, "The window was not rebuilt in German.");
        app.Snapshot("message");
        Assert.True(app.Find("TourSlideBody", 1000) == null, "The tour came up while the message was still open.");

        app.Require("CloseButton").Press();

        Assert.True(app.Find("TourSlideBody", 20000) != null, "The tour did not come up once the message was closed.");
        app.Snapshot("tour");
        Assert.False(app.HasExited);
    }

    private static int WidthOf(AppSession app, string automationId) =>
        (int)app.Require(automationId).BoundingRectangle.Width;
}
