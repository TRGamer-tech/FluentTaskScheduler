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

        // Any other startup dialog (What's New, update prompt, crash dialog): record and close it.
        for (int i = 0; i < 4; i++)
        {
            var dialog = app.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window));
            if (dialog == null) break;
            app.Log($"startup dialog: '{dialog.Name}' class '{dialog.ClassName}'");
            app.Snapshot($"startup-dialog-{i}");
            var close = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CloseButton"))
                        ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("PrimaryButton"));
            if (close == null) { Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE); }
            else close.AsButton().Invoke();
            Thread.Sleep(800);
        }

        app.Snapshot("main");

        foreach (string nav in new[] { "NavDashboard", "NavQuickActions", "NavScriptLibrary", "NavScriptEditor", "NavSettings", "NavAllTasks" })
        {
            var item = app.Find(nav);
            if (item == null) { app.Log($"nav item {nav} not found"); continue; }
            item.Click();
            Thread.Sleep(1500);
            app.Log($"navigated via {nav}; exited={app.HasExited}");
            app.Snapshot(nav);
        }

        // New task editor
        var add = app.Find("NavAdd");
        add?.Click();
        Thread.Sleep(1500);
        app.Snapshot("new-task-editor");
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
}
