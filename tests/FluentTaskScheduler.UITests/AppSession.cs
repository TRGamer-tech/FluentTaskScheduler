using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
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
        using var second = Process.Start(StartInfo(DataDir))!;
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
    /// <param name="settingsJson">Content of settings.json to start with; by default <see cref="FirstRunDone"/>, which skips the first-run dialogs.</param>
    public static AppSession Launch(string scenario, string? settingsJson = null)
    {
        string artifacts = Path.Combine(
            Environment.GetEnvironmentVariable("FTS_UI_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "artifacts"),
            scenario);
        Directory.CreateDirectory(artifacts);

        // The app runs once per user. A copy left over from an earlier test, for example one that
        // crashed and is still being written to a dump, would answer this start instead.
        EndRunningCopies();

        string data = Path.Combine(Path.GetTempPath(), "FtsUiTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), settingsJson ?? FirstRunDone());

        var app = Application.Launch(StartInfo(data));
        var automation = new UIA3Automation();
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(60))
                         ?? throw new InvalidOperationException("The app did not show a main window within 60 s.");
            BringToFront(automation, app, window);
            return new AppSession(app, automation, window, artifacts, data);
        }
        catch
        {
            // No session comes of it to be disposed, but the app's logs, a crash log among them, explain why.
            End(app, automation, data, artifacts);
            throw;
        }
    }

    /// <summary>Where the app keeps its settings and state, in place of the folder of whoever runs it.</summary>
    private const string DataFolderVariable = "FLUENTTASKSCHEDULER_DATA";

    private static ProcessStartInfo StartInfo(string dataDir)
    {
        var startInfo = new ProcessStartInfo(ExePath) { WorkingDirectory = Path.GetDirectoryName(ExePath)!, UseShellExecute = false };
        startInfo.Environment[DataFolderVariable] = dataDir;
        return startInfo;
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

    /// <summary>
    /// Settings of someone who has seen the welcome tour and the release notes, with an English UI
    /// unless <paramref name="changes"/> say otherwise.
    /// </summary>
    public static string FirstRunDone(params (string Name, JsonNode? Value)[] changes)
    {
        var settings = new JsonObject { ["Language"] = "en-US", ["HasCompletedOnboarding"] = true, ["LastSeenVersion"] = "999.0.0" };
        foreach (var (name, value) in changes) settings[name] = value;
        return settings.ToJsonString();
    }

    /// <summary>
    /// Whether <paramref name="condition"/> comes true within <paramref name="seconds"/>. An element
    /// it needs that is not there yet, or has just gone, counts as not yet.
    /// </summary>
    public static bool Eventually(Func<bool> condition, int seconds = 20) =>
        Retry.WhileFalse(condition, TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(250), ignoreException: true).Success;

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

    /// <summary>Opens an expander, through itself or through its header, whichever of the two offers that.</summary>
    public void Expand(string automationId)
    {
        var expander = Require(automationId);
        var header = Find(element => element.Patterns.ExpandCollapse.IsSupported
                                     && (element.AutomationId == automationId || element.Parent?.AutomationId == automationId))
                     ?? expander;
        header.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(600); // it animates open
    }

    /// <summary>
    /// Answers the file dialog the app has just opened, through its controls rather than the
    /// keyboard, so it does not matter which window has the focus.
    /// </summary>
    public void ChooseFile(string path)
    {
        // Elevated, the app shows the classic dialog in its own process; otherwise the picker of
        // Windows, which may live in another process but belongs to the app's window. Either is
        // looked up through Windows rather than through the app's window, which does not answer
        // while the dialog it opened is up.
        AutomationElement? dialog = null, fileName = null;
        bool ready = Retry.WhileFalse(() =>
        {
            try
            {
                nint handle = FindDialogWindow();
                if (handle == 0) return false;
                dialog = _automation.FromHandle(handle);
                fileName = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByAutomationId("1148")));
                return fileName != null;
            }
            catch (Exception)
            {
                return false; // the dialog's window exists before it can be talked to
            }
        }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500)).Success;
        if (!ready || dialog is null || fileName is null)
        {
            using (var screen = Capture.Screen()) screen.ToFile(Path.Combine(ArtifactsDir, "no-file-dialog.png"));
            throw new InvalidOperationException(
                "The app opened no file dialog with a box for the file name. Windows on the screen: " + string.Join("; ", VisibleWindows()));
        }

        fileName.AsTextBox().Text = path;

        AutomationElement open = dialog.FindFirstChild(cf => cf.ByAutomationId("1"))
                                 ?? throw new InvalidOperationException("The file dialog has no Open button.");
        open.Press();
    }

    // A visible top-level dialog window that the app's process shows or its window owns.
    private nint FindDialogWindow()
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisibleNative(window)) return true;

            var className = new StringBuilder(64);
            GetClassNameW(window, className, className.Capacity);
            if (className.ToString() != "#32770") return true;

            GetWindowThreadProcessId(window, out uint process);
            if (process != _app.ProcessId && GetWindow(window, OwnerWindow) != _windowHandle) return true;

            found = window;
            return false;
        }, 0);
        return found;
    }

    private const uint OwnerWindow = 4; // GW_OWNER

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint relation);

    // Class, title and program of every visible top-level window, to tell what a test met instead of what it expected.
    private static List<string> VisibleWindows()
    {
        var windows = new List<string>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisibleNative(window)) return true;
            var className = new StringBuilder(64);
            GetClassNameW(window, className, className.Capacity);
            var title = new StringBuilder(128);
            GetWindowTextW(window, title, title.Capacity);
            GetWindowThreadProcessId(window, out uint owner);
            string program;
            try { using var process = Process.GetProcessById((int)owner); program = process.ProcessName; }
            catch (ArgumentException) { program = "?"; }
            windows.Add($"{className} \"{title}\" ({program} {owner})");
            return true;
        }, 0);
        return windows;
    }

    private delegate bool WindowVisitor(nint window, nint parameter);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visitor, nint parameter);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int capacity);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);

    /// <summary>
    /// The text of a dialog the app is showing, or null if there is none. A dialog nobody asked
    /// for is how the app reports that something went wrong.
    /// </summary>
    public string? OpenDialogText()
    {
        AutomationElement? dialog = Find(element => element.ControlType == ControlType.Window
                                                    && element.Parent?.ControlType == ControlType.Pane
                                                    && element.FindAllChildren().Any(child => child.Properties.AutomationId.ValueOrDefault == "ContentScrollViewer"), 1500);
        return dialog is null ? null : TextIn(dialog);
    }

    /// <summary>What the elements inside an element say, joined. Elements without a name are left out.</summary>
    public static string TextIn(AutomationElement element) =>
        string.Join(" | ", element.FindAllDescendants()
            .Select(child => child.Properties.Name.ValueOrDefault)
            .Where(name => !string.IsNullOrEmpty(name)));

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
        // What the window showed last, and what the app wrote into its logs, explain a failure
        // that happened inside the app better than any assertion can.
        if (!_app.HasExited) Snapshot("last");
        End(_app, _automation, DataDir, ArtifactsDir);
    }

    // Keeps what the app wrote, ends it, and removes its data folder.
    private static void End(Application app, UIA3Automation automation, string dataDir, string artifactsDir)
    {
        KeepAppFiles(dataDir, artifactsDir);

        try { if (!app.HasExited) app.Kill(); } catch { }
        app.Dispose();
        automation.Dispose();
        EndRunningCopies();
        try { Directory.Delete(dataDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void KeepAppFiles(string dataDir, string artifactsDir)
    {
        try
        {
            string kept = Path.Combine(artifactsDir, "app-data");
            Directory.CreateDirectory(kept);
            foreach (string file in Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories)
                         .Where(file => Path.GetExtension(file) is ".txt" or ".json" or ".log"))
            {
                File.Copy(file, Path.Combine(kept, Path.GetFileName(file)), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a log still held open by an app that is being ended
        }
    }

    private static void EndRunningCopies()
    {
        foreach (Process copy in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath)))
        {
            using (copy)
            {
                try
                {
                    copy.Kill(entireProcessTree: true);
                    copy.WaitForExit(15000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already gone
                }
            }
        }
    }
}
