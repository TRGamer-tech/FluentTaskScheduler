using System.Drawing;

namespace FluentTaskScheduler.UITests;

/// <summary>The task editor in a narrow window, such as one snapped to half of a small screen.</summary>
public sealed class TaskEditorLayoutTests
{
    /// <summary>Controls at the right edge of the upper part of the form, which shows without scrolling.</summary>
    private static readonly string[] RightmostControls = ["EditorTags", "EditorAddTrigger", "EditorMoveTriggerDown"];

    [Fact]
    public void InANarrowWindow_TheFormGivesWay_InsteadOfBeingCutOff()
    {
        // German has the longest texts.
        using var app = AppSession.Launch("editor-narrow", """{"Language":"de-DE","HasCompletedOnboarding":true,"LastSeenVersion":"999.0.0"}""");
        app.ToolbarButton("NewTaskButton").Press();
        app.Require("EditorName", 20000);

        app.Resize(560, 800);
        app.Snapshot("narrow");

        Rectangle window = app.Window.BoundingRectangle;
        foreach (string id in RightmostControls)
        {
            var control = app.Require(id);
            Rectangle bounds = control.BoundingRectangle;
            Assert.False(control.IsOffscreen, $"{id} cannot be seen.");
            Assert.True(bounds.Left >= window.Left && bounds.Right <= window.Right, $"{id} at {bounds} is not inside the window at {window}.");
        }
    }
}
