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

    /// <summary>The folder this instance of the app keeps its settings and state in.</summary>
    public string DataDir { get; }

    private readonly nint _windowHandle;

    private AppSession(Application app, UIA3Automation automation, Window window, string artifactsDir, string dataDir)
    {
        _app = app;
        _automation = automation;
        Window = window;
        _windowHandle = window.Properties.NativeWindowHandle.Value;
        ArtifactsDir = artifactsDir;
        DataDir = dataDir;
    }

    public UIA3Automation Automation => _automation;

    /// <summary>False once the window is hidden, as it is while the app lives in the notification area only.</summary>
    public bool IsWindowVisible => IsWindowVisibleNative(_windowHandle);

    /// <summary>Starts the app a second time, the way a user who cannot find its window would, and waits for that instance to end.</summary>
    public void StartAgain()
    {
        using var second = Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false })!;
        if (second.WaitForExit(TimeSpan.FromSeconds(30))) return;
        second.Kill();
        throw new InvalidOperationException("The second instance kept running instead of handing over to the first.");
    }

    /// <summary>The value of one setting as the app last saved it, or null if it has not been saved.</summary>
    public string? SavedSetting(string name)
    {
        try
        {
            using var settings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "settings.json")));
            return settings.RootElement.TryGetProperty(name, out var value) ? value.ToString() : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null; // being written right now
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsWindowVisibleNative(nint window);

    public static string ExePath =>
        Environment.GetEnvironmentVariable("FTS_APP_EXE")
        ?? throw new InvalidOperationException("Set FTS_APP_EXE to the built FluentTaskScheduler.exe.");

    /// <summary>
    /// Starts the app with a data folder of its own, so a test neither sees nor changes the
    /// settings of whoever runs it.
    /// </summary>
    /// <param name="scenario">Names the folder the screenshots go to.</param>
    /// <param name="settingsJson">Content of settings.json to start with; the default skips the first-run dialogs.</param>
    public static AppSession Launch(string scenario, string settingsJson = FirstRunDone)
    {
        string artifacts = Path.Combine(
            Environment.GetEnvironmentVariable("FTS_UI_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"),
            scenario);
        Directory.CreateDirectory(artifacts);

        string data = Path.Combine(Path.GetTempPath(), "FtsUiTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), settingsJson);

        var startInfo = new ProcessStartInfo(ExePath) { WorkingDirectory = Path.GetDirectoryName(ExePath)!, UseShellExecute = false };
        startInfo.Environment["FLUENTTASKSCHEDULER_DATA"] = data;
        var app = Application.Launch(startInfo);
        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                     ?? throw new InvalidOperationException("The app did not show a main window within 60 s.");
        try
        {
            BringToFront(automation, app, window);
        }
        catch
        {
            app.Kill();
            throw;
        }
        return new AppSession(app, automation, window, artifacts, data);
    }

    /// <summary>Set to 1 on a build machine to let the tests end programs whose windows lie over the app.</summary>
    private const string CloseCoveringWindowsVariable = "FTS_UI_CLOSE_COVERING_WINDOWS";

    /// <summary>
    /// Mouse and keyboard input goes to whatever is on top. A hosted build machine can have a
    /// first sign-in screen of Windows lying over everything, with nobody there to click it away.
    /// </summary>
    private static void BringToFront(UIA3Automation automation, Application app, Window window)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            window.SetForeground();
            Thread.Sleep(500);

            int owner = automation.FromPoint(window.BoundingRectangle.Center()).Properties.ProcessId.ValueOrDefault;
            if (owner == app.ProcessId) return;

            using var covering = Process.GetProcessById(owner);
            string what = $"{covering.ProcessName} (process {owner})";
            if (Environment.GetEnvironmentVariable(CloseCoveringWindowsVariable) != "1")
            {
                throw new InvalidOperationException(
                    $"The app's window is covered by a window of {what}, so clicks would not reach the app. " +
                    $"Close that window, or set {CloseCoveringWindowsVariable}=1 to let the tests end such programs.");
            }

            Console.WriteLine($"Ending {what}: its window covers the app.");
            covering.Kill();
            covering.WaitForExit(5000);
        }
        throw new InvalidOperationException("The app's window is still covered by another window.");
    }

    /// <summary>Settings of someone who has seen the welcome tour and the release notes, with an English UI.</summary>
    public const string FirstRunDone = """{"Language":"en-US","HasCompletedOnboarding":true,"LastSeenVersion":"999.0.0"}""";

    public bool HasExited => _app.HasExited;

    /// <summary>The whole desktop, in the pixels window positions are given in.</summary>
    public System.Drawing.Rectangle Screen => _automation.GetDesktop().BoundingRectangle;

    /// <summary>
    /// Finds an element by walking the tree child by child. UI Automation's own descendant search
    /// does not reach into the WinUI content island reliably, while a manual walk does.
    /// </summary>
    public AutomationElement? Find(Func<AutomationElement, bool> predicate, int timeoutMs = 5000) =>
        Retry.WhileNull(() => Walk(Window).FirstOrDefault(e => Matches(e, predicate)),
            TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(250)).Result;

    public AutomationElement? Find(string automationId, int timeoutMs = 5000) =>
        Find(e => e.AutomationId == automationId, timeoutMs);

    public AutomationElement? FindByName(string name, ControlType? type = null, int timeoutMs = 5000) =>
        Find(e => e.Name == name && (type is null || e.ControlType == type), timeoutMs);

    private static bool Matches(AutomationElement element, Func<AutomationElement, bool> predicate)
    {
        try { return predicate(element); }
        catch { return false; } // properties of elements that just went away throw
    }

    /// <summary>The element and everything below it, level by level.</summary>
    public static IEnumerable<AutomationElement> Walk(AutomationElement root)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var element = queue.Dequeue();
            yield return element;
            AutomationElement[] children;
            try { children = element.FindAllChildren(); } catch { continue; }
            foreach (var child in children) queue.Enqueue(child);
        }
    }

    public AutomationElement Require(string automationId, int timeoutMs = 10000) =>
        Find(automationId, timeoutMs) ?? throw new InvalidOperationException($"Element '{automationId}' was not found.");

    /// <summary>
    /// A button of the task list's toolbar, wherever the toolbar put it: what does not fit next
    /// to the search box moves into the menu behind the "more" button.
    /// </summary>
    public AutomationElement ToolbarButton(string automationId)
    {
        if (Find(automationId, 3000) is { IsOffscreen: false } button) return button;
        Require("MoreButton").Press();
        return Require(automationId);
    }

    /// <summary>
    /// The text of a dialog the app is showing, or null if there is none. A dialog nobody asked
    /// for is how the app reports that something went wrong.
    /// </summary>
    public string? OpenDialogText()
    {
        AutomationElement? dialog = Find(element => element.ControlType == ControlType.Window
                                                    && element.Parent?.ControlType == ControlType.Pane
                                                    && element.FindAllChildren().Any(child => child.Properties.AutomationId.ValueOrDefault == "ContentScrollViewer"), 1500);
        return dialog is null ? null : string.Join(" | ", dialog.FindAllDescendants().Select(element => element.Name).Where(name => name.Length > 0));
    }

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

    /// <summary>Resizes the window; sizes are in physical pixels.</summary>
    public void Resize(int width, int height)
    {
        Window.Move(10, 10);
        Window.Patterns.Transform.Pattern.Resize(width, height);
        Thread.Sleep(1200); // the navigation pane animates between its modes
    }

    public void Dispose()
    {
        try { if (!_app.HasExited) _app.Kill(); } catch { }
        _app.Dispose();
        _automation.Dispose();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
