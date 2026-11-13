using FlaUI.Core.AutomationElements;

namespace FluentTaskScheduler.UITests;

/// <summary>The script editor: running a script for real, stopping it, and saving it to the library.</summary>
public class ScriptEditorTests
{
    [Fact]
    public void RunningAScript_ShowsWhatItWrites_AlsoInOtherLanguages_AndHowItEnded()
    {
        using var app = AppSession.Launch("script-run");
        app.Require("NavScriptEditor").Press();

        app.Require("ScriptEditorText").AsTextBox().Text = "Write-Output 'fts-plain'; Write-Output 'Zürich 東京'; exit 3";
        PressWhenEnabled(app, "ScriptEditorRun");

        bool exited = app.Find(e => e.Name == "[Exited with code 3]", 60000) != null;
        app.Snapshot("ran");
        Assert.True(exited, "The output does not say that the script exited with code 3.");
        Assert.True(app.Find(e => e.Name == "fts-plain", 2000) != null, "The output does not show the plain word the script wrote.");
        Assert.True(app.Find(e => e.Name == "Zürich 東京", 2000) != null, "The output does not show the non-ASCII words the script wrote as written.");
    }

    [Fact]
    public void ARunningScript_CanBeStopped()
    {
        using var app = AppSession.Launch("script-stop");
        app.Require("NavScriptEditor").Press();

        app.Require("ScriptEditorText").AsTextBox().Text = "Write-Output 'fts-started'; Start-Sleep -Seconds 600";
        PressWhenEnabled(app, "ScriptEditorRun");
        Assert.True(app.Find(e => e.Name == "fts-started", 60000) != null, "The script did not start.");

        PressWhenEnabled(app, "ScriptEditorStop");

        bool stopped = app.Find(e => e.Name == "[Stopped by user]", 20000) != null;
        app.Snapshot("stopped");
        Assert.True(stopped, "The output does not say that the script was stopped.");
        Assert.True(app.Find(e => e.AutomationId == "ScriptEditorRun" && e.IsEnabled, 5000) != null, "Run is not available again after stopping.");
    }

    [Fact]
    public void AScriptSavedInTheEditor_AppearsInTheLibrary_AndStaysInTheEditorWhileLookingThere()
    {
        using var app = AppSession.Launch("script-save");
        app.Require("NavScriptEditor").Press();
        app.Require("ScriptEditorName").AsTextBox().Text = "fts ui test script";
        app.Require("ScriptEditorText").AsTextBox().Text = "Get-Date";

        PressWhenEnabled(app, "ScriptEditorSave");
        Assert.True(app.Find(e => e.Name == "[Saved to Library as 'fts ui test script']", 10000) != null, "The output does not confirm that the script was saved.");

        app.Require("NavScriptLibrary").Press();
        app.Require("LibraryScriptsTab").Select();
        bool listed = app.Find(e => e.Name == "fts ui test script", 10000) != null;
        app.Snapshot("library");
        Assert.True(listed, "The saved script is not listed in the library.");
        Assert.Single(Directory.GetFiles(Path.Combine(app.DataDir, "Scripts"), "*.ps1"));

        app.Require("NavScriptEditor").Press();
        Assert.Equal("Get-Date", app.Require("ScriptEditorText").AsTextBox().Text.Trim());
    }

    [Fact]
    public void AScriptNotSavedYet_StaysInTheEditor_WhenAnotherLanguageBuildsThePagesAnew()
    {
        using var app = AppSession.Launch("script-rebuild");
        app.Require("NavScriptEditor").Press();
        app.Require("ScriptEditorText").AsTextBox().Text = "Write-Output 'fts-unsaved'";

        app.Require("NavSettings").Press();
        app.Require("LanguageChoice", 30000).AsComboBox().Select(2); // the language of Windows, English, German, ...
        Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == "Einstellungen", 10000) != null, "The pages were not built anew in German.");

        app.Require("NavScriptEditor").Press();
        app.Snapshot("editor");
        Assert.Equal("Write-Output 'fts-unsaved'", app.Require("ScriptEditorText").AsTextBox().Text.Trim());
    }

    /// <summary>Presses a toolbar button once the text typed before has made it available.</summary>
    private static void PressWhenEnabled(AppSession app, string automationId)
    {
        var button = app.Find(e => e.AutomationId == automationId && e.IsEnabled, 10000)
                     ?? throw new InvalidOperationException($"'{automationId}' did not become available.");
        button.AsButton().Invoke();
    }
}

/// <summary>The library: its two sections and what they list.</summary>
public class LibraryTests
{
    [Fact]
    public void TheLibrary_ListsTheTemplates_AndAfterSwitchingTheBuiltInScripts()
    {
        using var app = AppSession.Launch("library");
        app.Require("NavScriptLibrary").Press();

        Assert.True(app.FindByName("Nightly Temp File Cleanup") != null, "The temp cleanup template is not listed.");
        Assert.True(app.Require("Template.Defrag").IsEnabled, "The button that deploys a template is not available.");
        // Every card has the same buttons; a screen reader tells them apart by what they act on.
        Assert.Equal("Deploy template Nightly Temp File Cleanup", app.Require("Template.TempCleanup").Name);
        app.Snapshot("templates");

        app.Require("LibraryScriptsTab").Select();

        Assert.True(app.FindByName("System File Checker") != null, "The built-in scripts are not listed.");
        Assert.True(app.Require("Script.SystemFileChecker").IsEnabled, "The button that creates a task from a script is not available.");
        Assert.Equal("Create task from script System File Checker", app.Require("Script.SystemFileChecker").Name);
        Assert.Null(app.Find("Template.Defrag", 1500));
        app.Snapshot("scripts");
    }

    [Fact]
    public void InANarrowWindow_TheTabsKeepTheirWidth_AndTheSearchBoxGivesWay()
    {
        // The tabs are widest in Japanese.
        using var app = AppSession.Launch("library-narrow", AppSession.FirstRunDone(("Language", "ja-JP")));
        app.Require("NavScriptLibrary").Press();
        int fullWidth = app.Require("LibraryScriptsTab").BoundingRectangle.Width;

        // The navigation pane folds into a menu button, and the page gets the whole window.
        app.Resize(520, 800);
        app.Snapshot("narrow");
        var tab = app.Require("LibraryScriptsTab").BoundingRectangle;
        var search = app.Require("LibrarySearch").BoundingRectangle;

        Assert.True(tab.Width >= fullWidth - 1, $"The Scripts tab is {tab.Width} pixels wide in a narrow window and {fullWidth} in a wide one: it is cut off.");
        Assert.True(search.Left > tab.Right, $"The search box begins at {search.Left}, before the Scripts tab ends at {tab.Right}.");
    }
}

/// <summary>The quick actions: running one, and what the page says about administrator rights.</summary>
public class QuickActionsTests
{
    [Fact]
    public void FlushingTheDnsCache_ReportsSuccess_AndWhatWindowsSaid()
    {
        using var app = AppSession.Launch("quick-action");
        app.Require("NavQuickActions").Press();

        app.Require("QuickAction.FlushDNS.Run").AsButton().Invoke();

        bool succeeded = app.Find(e => e.AutomationId == "QuickAction.FlushDNS.Status" && e.Name == "Success", 60000) != null;
        app.Snapshot("ran");
        Assert.True(succeeded, "Flush DNS did not report success.");
        Assert.NotEqual("", app.Require("QuickAction.FlushDNS.Detail").Name);
    }

    [Fact]
    public void ActionsThatNeedAdministratorRights_SaySo_AndCanOnlyRunWhenTheAppHasThem()
    {
        using var app = AppSession.Launch("quick-action-elevation");
        app.Require("NavQuickActions").Press();

        Assert.Equal("Requires administrator rights", app.Require("QuickAction.SfcScannow.Elevation").Name);
        Assert.Null(app.Find("QuickAction.FlushDNS.Elevation", 1500));

        // The page offers a restart as administrator exactly when the app runs without those rights.
        bool offersRestart = app.Find("QuickActionsRestartElevated", 1500) != null;
        app.Snapshot(offersRestart ? "not-elevated" : "elevated");
        Assert.Equal(!offersRestart, app.Require("QuickAction.SfcScannow.Run").IsEnabled);
        Assert.True(app.Require("QuickAction.FlushDNS.Run").IsEnabled);
    }
}
