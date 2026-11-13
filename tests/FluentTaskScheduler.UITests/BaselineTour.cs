using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Xunit;

namespace FluentTaskScheduler.UITests;

/// <summary>Walks through the whole app once and records what every screen looks like.</summary>
public class BaselineTour
{
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler", "settings.json");

    [Fact]
    public void FirstRun_Onboarding_Then_EveryPage()
    {
        if (File.Exists(SettingsFile)) File.Delete(SettingsFile);

        using var app = AppSession.Launch("baseline");
        app.Log("launched");
        app.Snapshot("launch");

        // Onboarding: record the Next button position on every slide.
        for (int slide = 1; slide <= 12; slide++)
        {
            var next = app.Find("NextButton", 3000);
            if (next == null) { app.Log($"no onboarding button on iteration {slide}"); break; }
            var r = next.BoundingRectangle;
            app.Log($"onboarding slide {slide}: NextButton '{next.Name}' at {r.X},{r.Y} {r.Width}x{r.Height}");
            app.Snapshot($"onboarding-{slide}");
            next.AsButton().Invoke();
            Thread.Sleep(400);
        }

        app.Snapshot("main");

        foreach (string nav in new[] { "NavDashboard", "NavQuickActions", "NavScriptLibrary", "NavScriptEditor", "NavSettings" })
        {
            Visit(app, nav);
        }

        foreach (string section in new[] { "NavNotificationsItem", "NavSystemItem", "NavAdvancedItem", "NavCategoriesItem", "NavAboutItem" })
        {
            Visit(app, section);
        }

        Visit(app, "NavAllTasks");

        // Task details dialog of the first task in the list
        var firstTask = app.Find(e => e.ControlType == ControlType.ListItem && e.Parent?.AutomationId == "TaskListView");
        if (firstTask != null)
        {
            firstTask.Click();
            Thread.Sleep(2500);
            app.Snapshot("task-details");
            Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Thread.Sleep(800);
        }

        // New task editor
        var add = app.Find("NavAdd");
        add?.Click();
        Thread.Sleep(2000);
        app.Snapshot("new-task-editor");
        var scroller = app.Find("EditScrollViewer", 2000);
        if (scroller != null)
        {
            for (int page = 1; page <= 4; page++)
            {
                scroller.Patterns.Scroll.PatternOrDefault?.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                Thread.Sleep(500);
                app.Snapshot($"new-task-editor-page{page}");
            }
        }
        var editorClose = app.Find("CloseButton", 2000);
        app.Log($"editor close button found: {editorClose != null}");
        editorClose?.AsButton().Invoke();
        Thread.Sleep(800);

        // Narrow window (sidebar behaviour)
        app.Window.Move(20, 20);
        app.Window.Patterns.Transform.Pattern.Resize(720, 700);
        Thread.Sleep(1200);
        app.Snapshot("narrow-720");

        Assert.False(app.HasExited, "The app exited during the tour.");
    }

    private static void Visit(AppSession app, string automationId)
    {
        var item = app.Find(automationId);
        if (item == null) { app.Log($"{automationId} not found"); return; }
        item.Click();
        Thread.Sleep(1800);
        app.Log($"visited {automationId}; exited={app.HasExited}");
        app.Snapshot(automationId);
    }
}
