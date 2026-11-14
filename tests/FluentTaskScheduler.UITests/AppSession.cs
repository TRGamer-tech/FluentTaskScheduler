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
    private readonly Timer? _foregroundGuard;
    private int _guarding;
    private nint _foreignInFront;
    private nint _broughtBackOver;
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
        if (Environment.GetEnvironmentVariable(CloseCoveringWindowsVariable) == "1")
            _foregroundGuard = new Timer(_ => GuardForeground(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public UIA3Automation Automation => _automation;

    /// <summary>False once the window is hidden, as it is while the app lives in the notification area only.</summary>
    public bool IsWindowVisible => TopLevelWindows.IsVisible(_windowHandle);

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
            BringToFront(app, window);
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

    /// <summary>
    /// Set to 1 on a build machine to let the tests get windows of other programs out of the
    /// app's way, which a hosted build machine has with nobody there to click them away.
    /// </summary>
    private const string CloseCoveringWindowsVariable = "FTS_UI_CLOSE_COVERING_WINDOWS";

    private const string DialogClass = "#32770";

    /// <summary>Windows of the shell that tests work with: the taskbar, the hidden icons, menus and pop-ups.</summary>
    private static readonly HashSet<string> ShellClasses =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Xaml_WindowedPopupClass", "#32768"];

    /// <summary>Console windows, of Windows' own console or of Windows Terminal: closing one ends the programs in it.</summary>
    private static readonly HashSet<string> ConsoleClasses = ["ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow"];

    /// <summary>
    /// Mouse input goes to whatever is on top, keyboard input to the window in the foreground. A
    /// hosted build machine can have a first sign-in screen of Windows lying over everything, or a
    /// window of its own keeping the foreground, with nobody there to click it away.
    /// </summary>
    private static void BringToFront(Application app, Window window)
    {
        nint handle = window.Properties.NativeWindowHandle.Value;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            TopLevelWindows.BringToFront(handle);
            Thread.Sleep(500);

            nint foreground = TopLevelWindows.Foreground;
            nint onTop = TopLevelWindows.RootAt(window.BoundingRectangle.Center());
            (nint inTheWay, string problem) =
                IsInTheWay(foreground, app.ProcessId, handle) ? (foreground, "keeps the foreground, so keys would not reach the app")
                : IsInTheWay(onTop, app.ProcessId, handle) ? (onTop, "covers the app's window, so clicks would not reach the app")
                : (0, "");
            if (inTheWay == 0) return;

            if (Environment.GetEnvironmentVariable(CloseCoveringWindowsVariable) != "1")
            {
                throw new InvalidOperationException(
                    $"{Describe(inTheWay)} {problem}. Close it, or set {CloseCoveringWindowsVariable}=1 to let the tests get such windows out of the way.");
            }
            StepOver(inTheWay, problem);
        }
        throw new InvalidOperationException("A window of another program is still in the app's way.");
    }

    private static bool IsInTheWay(nint window, int appProcess, nint appWindow) =>
        window != 0 && !IsTheApps(window, appProcess, appWindow) && !ShellClasses.Contains(TopLevelWindows.ClassOf(window));

    /// <summary>Whether <paramref name="window"/> is the app's, or a dialog it opened, which another process may show.</summary>
    private static bool IsTheApps(nint window, int appProcess, nint appWindow) =>
        TopLevelWindows.ProcessOf(window) == appProcess || TopLevelWindows.RootOwnerOf(window) == appWindow;

    /// <summary>
    /// Gets a window of another program out of the app's way. A dialog is closed. A console window
    /// is minimized, since it can be the build machine's own and closing it would end the programs
    /// in it; so is a window of Explorer, which also runs the taskbar. Anything else is ended with
    /// its program, such as Windows Search or the Start menu: Windows starts them again when they
    /// are needed, and nothing else gets them out of the foreground.
    /// </summary>
    private static void StepOver(nint window, string problem)
    {
        string className = TopLevelWindows.ClassOf(window);
        int process = TopLevelWindows.ProcessOf(window);
        string what = Describe(window);
        try
        {
            if (className == DialogClass)
            {
                Console.WriteLine($"Closing {what}: it {problem}.");
                TopLevelWindows.Close(window);
            }
            else if (ConsoleClasses.Contains(className) || ProgramName(process) == "explorer")
            {
                Console.WriteLine($"Minimizing {what}: it {problem}.");
                TopLevelWindows.Minimize(window);
            }
            else
            {
                Console.WriteLine($"Ending {what}: it {problem}.");
                using var program = Process.GetProcessById(process);
                program.Kill();
                program.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"{what} could not be got out of the way: {ex.Message}");
        }
        Thread.Sleep(500);
    }

    /// <summary>
    /// Windows of other programs can also come up while a test runs and take the foreground, and
    /// with it the keys the test types: on the build machines a dialog of Windows, Windows Search,
    /// and console windows of the machine's own setup did. Where the build machine lets the tests
    /// get such windows out of the way, the app's window is brought back to the front while it is
    /// shown; a window that Windows does not take the foreground from is stepped over first. The
    /// taskbar and its pop-ups are left to the tests that use them. Only a window that is in the
    /// foreground at two looks in a row counts: one gets it for a moment whenever the app's window
    /// is hidden or minimized, before Windows says that it is.
    /// </summary>
    private void GuardForeground()
    {
        if (Interlocked.Exchange(ref _guarding, 1) == 1) return;
        try
        {
            nint foreground = TopLevelWindows.Foreground;
            bool shown = TopLevelWindows.IsVisible(_windowHandle) && !TopLevelWindows.IsMinimized(_windowHandle);
            nint seenBefore = _foreignInFront;
            _foreignInFront = shown && IsInTheWay(foreground, _app.ProcessId, _windowHandle) ? foreground : 0;
            if (_foreignInFront == 0 || _foreignInFront != seenBefore) return;

            // While the app has a dialog open, the foreground belongs to that rather than to the window under it.
            nint app = TopLevelWindows.LastActivePopupOf(_windowHandle);
            if (TopLevelWindows.BringToFront(app))
            {
                if (foreground != _broughtBackOver) Console.WriteLine($"Brought the app back to the front over {Describe(foreground)}.");
                _broughtBackOver = foreground;
                return;
            }
            StepOver(foreground, "keeps the foreground, so keys would not reach the app");
            TopLevelWindows.BringToFront(app);
        }
        catch (Exception ex)
        {
            // A failure on the timer's thread would end the whole test run.
            Console.WriteLine($"Keeping the app's window in the foreground failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _guarding, 0);
        }
    }

    /// <summary>Title, class and program of a window, to say in a message which one it is.</summary>
    private static string Describe(nint window)
    {
        int process = TopLevelWindows.ProcessOf(window);
        return $"\"{TopLevelWindows.TitleOf(window)}\" ({TopLevelWindows.ClassOf(window)}) of {ProgramName(process)} (process {process})";
    }

    private static string ProgramName(int process)
    {
        try
        {
            using var program = Process.GetProcessById(process);
            return program.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "?"; // gone already
        }
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
    /// Searches the task list for <paramref name="name"/> and returns the row of the task of that
    /// name once the list has stopped changing. The list is filtered anew with every key typed,
    /// and a click aimed at a row that is still moving lands elsewhere.
    /// </summary>
    public AutomationElement SearchTask(string name)
    {
        Require("SearchBox").TypeText(name);
        AutomationElement list = Require("TaskList");
        AutomationElement? row = null;
        string seen = "";
        bool settled = Retry.WhileFalse(() =>
        {
            List<AutomationElement> rows = Walk(list).Where(element => element.Is(ControlType.ListItem)).ToList();
            row = rows.FirstOrDefault(element => element.NameOrEmpty() == name);
            string layout = row is null ? "" : string.Join("|", rows.Select(element => element.NameOrEmpty() + element.BoundingRectangle));
            bool unchanged = layout.Length > 0 && layout == seen;
            seen = layout;
            return unchanged;
        }, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500), ignoreException: true).Success;
        return settled && row is not null ? row : throw new InvalidOperationException($"The task '{name}' is not in the list, or the list kept changing.");
    }

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
    private nint FindDialogWindow() =>
        TopLevelWindows.Visible().FirstOrDefault(window =>
            TopLevelWindows.ClassOf(window) == DialogClass
            && (TopLevelWindows.ProcessOf(window) == _app.ProcessId || TopLevelWindows.OwnerOf(window) == _windowHandle));

    // Every visible top-level window, to tell what a test met instead of what it expected.
    private static IEnumerable<string> VisibleWindows() => TopLevelWindows.Visible().Select(Describe);

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
        // A look at the foreground that is under way ends first; it could bring the window back.
        _foregroundGuard?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));

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
