using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The settings page: how it is laid out, and that what is chosen there takes effect and is saved.</summary>
public class SettingsTests
{
    private static AppSession OpenSettings(string scenario)
    {
        var app = AppSession.Launch(scenario);
        app.Require("NavSettings").Press();
        app.Require("ThemeChoice", 30000);
        return app;
    }

    /// <summary>What a drop-down list shows as chosen.</summary>
    private static string? ChoiceShownBy(AppSession app, string automationId) =>
        app.Require(automationId).AsComboBox().SelectedItem?.Name;

    private static void Expand(AppSession app, string automationId)
    {
        var expander = app.Require(automationId);
        var header = app.Find(element => element.Patterns.ExpandCollapse.IsSupported
                                         && (element.AutomationId == automationId || element.Parent?.AutomationId == automationId))
                     ?? expander;
        header.Patterns.ExpandCollapse.Pattern.Expand();
    }

    // The app saves a moment after a change.
    private static bool Eventually(Func<bool> condition) =>
        Retry.WhileFalse(condition, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Success;

    [Fact]
    public void FlippingASwitch_IsSavedToTheSettingsFile()
    {
        using var app = OpenSettings("settings-switch");
        var confirmDelete = app.Require("ConfirmDeleteSwitch").Patterns.Toggle.Pattern;
        Assert.Equal(ToggleState.On, confirmDelete.ToggleState.Value);

        confirmDelete.Toggle();
        app.Snapshot("confirm-delete-off");

        Assert.Equal(ToggleState.Off, confirmDelete.ToggleState.Value);
        Assert.True(Eventually(() => app.SavedSetting("ConfirmDelete") == "False"), "settings.json does not say that deleting is no longer confirmed.");
    }

    [Fact]
    public void ACategoryAddedOnThePage_IsSaved()
    {
        using var app = OpenSettings("settings-categories");

        Expand(app, "CategoriesExpander");
        app.Require("NewNameBox").AsTextBox().Text = "Backups";
        app.Require("AddButton").Press();
        app.Snapshot("category-added");

        Assert.True(app.FindByName("Backups", ControlType.Text) != null, "The new category is not listed.");
        Assert.True(Eventually(() => app.SavedSetting("SavedCategories")?.Contains("\"Backups\"") == true), "settings.json does not hold the new category.");
    }

    [Fact]
    public void TheLists_ShowWhatIsSet()
    {
        using var app = OpenSettings("settings-lists");
        app.Snapshot("top");

        Assert.Equal("English", ChoiceShownBy(app, "LanguageChoice"));
        Assert.Equal("System Default", ChoiceShownBy(app, "ThemeChoice"));
        Assert.Equal("5 minutes before", ChoiceShownBy(app, "ReminderLeadChoice"));
    }

    [Fact]
    public void ChoosingALanguage_ShowsTheAppInIt_StillOnTheSettingsPage()
    {
        using var app = OpenSettings("settings-language");

        app.Require("LanguageChoice").AsComboBox().Select(2); // the language of Windows, English, German, ...

        Assert.True(app.Find(e => e.AutomationId == "PageTitle" && e.Name == "Einstellungen", 10000) != null, "The heading did not become 'Einstellungen'.");
        app.Snapshot("german");
        Assert.Equal("Einstellungen", app.Require("NavSettings").Name);
        Assert.Equal("Deutsch", ChoiceShownBy(app, "LanguageChoice"));
        Assert.True(Eventually(() => app.SavedSetting("Language") == "de-DE"), "settings.json does not hold the chosen language.");
        Assert.False(app.HasExited);
    }

    [Fact]
    public void ASwitch_SitsAtTheRightEdgeOfItsCard_WithItsStateTextBeforeTheKnob()
    {
        using var app = OpenSettings("settings-layout");
        var list = app.Require("ThemeChoice").BoundingRectangle;
        var toggle = app.Require("MicaSwitch");
        var bounds = toggle.BoundingRectangle;
        app.Snapshot("cards");

        Assert.True(Math.Abs(bounds.Right - list.Right) <= 2, $"The switch ends at {bounds.Right}, the list in the card above it at {list.Right}.");

        // The knob is 40 wide and belongs at the very right, with "On" or "Off" written to the left of it.
        // Not every version of the switch lets automation see that text; the screenshot shows it either way.
        var stateText = AppSession.Walk(toggle).FirstOrDefault(e => e.ControlType == ControlType.Text && e.Name is "On" or "Off");
        if (stateText is null)
        {
            app.Log("The state text of the switch is not visible to automation; its place was not checked.");
            return;
        }
        int textEnd = stateText.BoundingRectangle.Right;
        Assert.True(textEnd <= bounds.Right - 40, $"The state text ends at {textEnd} and the switch at {bounds.Right}: the text is not before the knob.");
    }

    [Fact]
    public void TheCards_KeepToAMaximumWidth_CentredInAWideWindow()
    {
        using var app = OpenSettings("settings-wide");
        app.Resize(1900, 900);
        app.Snapshot("wide");
        var page = app.Require("SettingsScroller").BoundingRectangle;

        // The first card is told by what sits at its two ends: the heading above it begins where
        // the card does, and the list in it ends where the card does, less the card's padding.
        var heading = app.FindByName("Appearance", ControlType.Text);
        Assert.NotNull(heading);
        int left = heading.BoundingRectangle.Left - page.Left;
        int right = page.Right - app.Require("ThemeChoice").BoundingRectangle.Right;

        Assert.True(left > 100 && right > 100, $"The cards have {left} to their left and {right} to their right in a page of {page.Width}: they are not held to a maximum width.");
        Assert.True(Math.Abs(left - right) <= 24, $"The cards have {left} to their left and {right} to their right: they are not centred.");
    }

    [Fact]
    public void CheckingForUpdates_InACopyThatWasNotInstalled_SaysThatItCannotUpdateItself()
    {
        using var app = OpenSettings("settings-update");

        app.Require("CheckForUpdatesButton").Press();

        bool said = app.Find(e => e.ControlType == ControlType.Text && e.Name.StartsWith("This copy of the app cannot update itself"), 15000) != null;
        app.Snapshot("update-checked");
        Assert.True(said, "The page does not say that this copy cannot update itself.");
    }

    [Fact]
    public void TheReleaseNotes_OpenFromAbout_OrTheAppSaysThatTheyCannotBeHad()
    {
        using var app = OpenSettings("settings-release-notes");

        app.Require("ReleaseNotesLink").Press();

        // Which of the two it is depends on whether GitHub can be reached from where the test runs.
        var outcome = app.Find(e => e.Name is "View this release on GitHub" || e.Name.StartsWith("Could not fetch release notes"), 20000);
        app.Snapshot("release-notes");
        Assert.True(outcome != null, "Neither the release notes nor a message about them came up.");

        app.Require("CloseButton").Press();
        Assert.False(app.HasExited);
    }

    [Fact]
    public void TheWelcomeTour_CanBeOpenedAgainFromAbout()
    {
        using var app = OpenSettings("settings-tour");

        app.Require("WelcomeTourLink").Press();

        Assert.True(app.Find("TourSlideBody", 10000) != null, "The tour did not open.");
        app.Snapshot("tour");
        app.Require("CloseButton").Press();
        Assert.True(Retry.WhileFalse(() => app.Find("TourSlideBody", 300) == null, TimeSpan.FromSeconds(5)).Success, "The tour did not close.");
    }
}
