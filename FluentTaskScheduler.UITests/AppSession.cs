using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// One running instance of the app under test, driven through UI Automation. Every step can leave
/// a screenshot and a text dump of the automation tree in the artifacts folder, which CI uploads.
/// </summary>
public sealed class AppSession : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private int _shotIndex;

    public Window Window { get; }
    public string ArtifactsDir { get; }

    private AppSession(Application app, UIA3Automation automation, Window window, string artifactsDir)
    {
        _app = app;
        _automation = automation;
        Window = window;
        ArtifactsDir = artifactsDir;
    }

    public static string ExePath =>
        Environment.GetEnvironmentVariable("FTS_APP_EXE")
        ?? throw new InvalidOperationException("Set FTS_APP_EXE to the built FluentTaskScheduler.exe.");

    public static AppSession Launch(string scenario)
    {
        string artifacts = Path.Combine(
            Environment.GetEnvironmentVariable("FTS_UI_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"),
            scenario);
        Directory.CreateDirectory(artifacts);

        var startInfo = new ProcessStartInfo(ExePath) { WorkingDirectory = Path.GetDirectoryName(ExePath)! };
        var app = Application.Launch(startInfo);
        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                     ?? throw new InvalidOperationException("The app did not show a main window within 60 s.");
        window.SetForeground();
        return new AppSession(app, automation, window, artifacts);
    }

    public bool HasExited => _app.HasExited;

    public AutomationElement? Find(string automationId, int timeoutMs = 5000) =>
        Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(200)).Result;

    public AutomationElement? FindByName(string name, ControlType? type = null, int timeoutMs = 5000) =>
        Retry.WhileNull(() => Window.FindFirstDescendant(cf =>
                type is { } t ? cf.ByName(name).And(cf.ByControlType(t)) : cf.ByName(name)),
            TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(200)).Result;

    public AutomationElement Require(string automationId, int timeoutMs = 10000) =>
        Find(automationId, timeoutMs) ?? throw new InvalidOperationException($"Element '{automationId}' was not found.");

    /// <summary>Saves a screenshot of the app window plus a dump of its automation tree.</summary>
    public void Snapshot(string name)
    {
        string baseName = $"{++_shotIndex:00}-{name}";
        try
        {
            Thread.Sleep(600); // let animations settle
            using var image = Capture.Element(Window);
            image.ToFile(Path.Combine(ArtifactsDir, baseName + ".png"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(ArtifactsDir, baseName + ".screenshot-error.txt"), ex.ToString());
        }

        try { File.WriteAllText(Path.Combine(ArtifactsDir, baseName + ".tree.txt"), DumpTree()); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(ArtifactsDir, baseName + ".tree-error.txt"), ex.ToString()); }
    }

    public string DumpTree()
    {
        var sb = new StringBuilder();
        Dump(Window, 0, sb);
        return sb.ToString();
    }

    private static void Dump(AutomationElement element, int depth, StringBuilder sb)
    {
        if (depth > 40) return;
        string Safe(Func<string?> get) { try { return get() ?? ""; } catch { return "?"; } }

        string type = Safe(() => element.ControlType.ToString());
        string id = Safe(() => element.AutomationId);
        string name = Safe(() => element.Name).Replace("\r", " ").Replace("\n", " ");
        if (name.Length > 120) name = name[..120] + "...";
        string rect = Safe(() => { var r = element.BoundingRectangle; return $"{r.X},{r.Y} {r.Width}x{r.Height}"; });
        string offscreen = Safe(() => element.IsOffscreen ? " offscreen" : "");
        string enabled = Safe(() => element.IsEnabled ? "" : " disabled");
        sb.Append(' ', depth * 2).Append(type);
        if (id.Length > 0) sb.Append(" #").Append(id);
        if (name.Length > 0) sb.Append(" \"").Append(name).Append('"');
        sb.Append(" [").Append(rect).Append(']').Append(offscreen).Append(enabled).AppendLine();

        AutomationElement[] children;
        try { children = element.FindAllChildren(); } catch { return; }
        foreach (var child in children) Dump(child, depth + 1, sb);
    }

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        File.AppendAllText(Path.Combine(ArtifactsDir, "log.txt"), line + Environment.NewLine);
    }

    public void Dispose()
    {
        try { if (!_app.HasExited) _app.Kill(); } catch { }
        _app.Dispose();
        _automation.Dispose();
    }
}
