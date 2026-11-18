using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace FluentTaskScheduler.UITests;

/// <summary>The welcome tour that comes up at the very first start.</summary>
public class WelcomeTourTests
{
    /// <summary>Settings of someone who has never started the app, apart from the language.</summary>
    private static string FirstStart(string language) => $$"""{"Language":"{{language}}"}""";

    private static readonly string[] Buttons = ["PrimaryButton", "SecondaryButton", "CloseButton"];

    private static Rectangle[] PlacesOfTheButtons(AppSession app) => Buttons.Select(id => app.Require(id).BoundingRectangle).ToArray();

    /// <summary>The text of the slide that is showing; the other slides are there too, but hidden from automation.</summary>
    private static AutomationElement? SlideText(AppSession app) =>
        app.Find(e => e.AutomationId == "TourSlideBody" && e.Properties.IsContentElement.ValueOrDefault);

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("zh-CN")]
    public void TheButtons_StayWhereTheyAre_FromTheFirstSlideToTheLast(string language)
    {
        using var app = AppSession.Launch("tour-" + language, FirstStart(language));
        var next = app.Require("SecondaryButton", 30000);
        app.Snapshot("slide-1");

        Rectangle[] places = PlacesOfTheButtons(app);
        string nextLabel = next.Name;
        string? firstText = SlideText(app)?.Name;
        Assert.False(app.Require("PrimaryButton").IsEnabled, "There is nothing to go back to on the first slide.");

        // On the last slide the button that moves on reads differently: it ends the tour.
        int slide = 1;
        while (app.Require("SecondaryButton").Name == nextLabel)
        {
            Assert.True(++slide <= 12, "The tour does not come to a last slide.");
            app.Require("SecondaryButton").Press();
            app.Snapshot("slide-" + slide);

            Assert.Equal(places, PlacesOfTheButtons(app));
            Assert.True(app.Require("PrimaryButton").IsEnabled, "Back is not available on slide " + slide + ".");
        }
        Assert.True(slide >= 3, "The tour has fewer slides than expected.");
        Assert.NotEqual(firstText, SlideText(app)?.Name);

        app.Require("SecondaryButton").Press();

        Assert.True(Retry.WhileFalse(() => app.Find("SecondaryButton", 300) == null, TimeSpan.FromSeconds(5)).Success, "The tour did not close.");
        Assert.False(app.HasExited);
    }

    [Fact]
    public void TheTour_ShowsAtTheFirstStartOnly()
    {
        string settingsAfterwards;
        using (var first = AppSession.Launch("tour-first-start", FirstStart("en-US")))
        {
            first.Require("CloseButton", 30000).Press();

            bool noted = Retry.WhileFalse(() => first.SavedSetting("HasCompletedOnboarding") == "True", TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Success;
            Assert.True(noted, "settings.json does not note that the tour has been shown.");
            settingsAfterwards = File.ReadAllText(Path.Combine(first.DataDir, "settings.json"));
        }

        using var second = AppSession.Launch("tour-second-start", settingsAfterwards);
        second.Require("NavSettings");
        second.Snapshot("start");

        Assert.True(second.Find("TourSlideBody", 4000) == null, "The tour came up again at the second start.");
    }

    [Fact]
    public void InASmallWindow_TheButtonsAndTheTextOfTheTourAreStillInsideIt()
    {
        using var app = AppSession.Launch("tour-small", FirstStart("de-DE"));
        app.Require("SecondaryButton", 30000);

        app.Resize(820, 620);
        app.Snapshot("small");
        Rectangle window = app.Window.BoundingRectangle;

        foreach (string id in Buttons)
        {
            Rectangle button = app.Require(id).BoundingRectangle;
            Assert.True(window.Contains(button), $"{id} at {button} is not inside the window at {window}.");
        }

        AutomationElement? text = SlideText(app);
        Assert.NotNull(text);
        Assert.True(window.Contains(text.BoundingRectangle), $"The text of the slide at {text.BoundingRectangle} is not inside the window at {window}.");
    }
}
