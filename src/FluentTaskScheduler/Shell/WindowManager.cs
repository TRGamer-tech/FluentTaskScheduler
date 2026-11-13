using System.Runtime.InteropServices;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Notifications;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// Creates the app's windows and keeps track of them. Each window gets its own service scope,
/// so its pages and view models live and die with it.
/// </summary>
public sealed class WindowManager(IServiceProvider services, ISettingsStore settings, INotifier notifier)
{
    private sealed class Entry(Window window, int number)
    {
        public Window Window { get; } = window;
        public int Number { get; } = number;
        public IServiceScope? Scope { get; set; }
        public ShellPage Shell { get; set; } = null!;

        /// <summary>Lets the window show one dialog at a time, whichever of its scopes asks.</summary>
        public SemaphoreSlim DialogQueue { get; } = new(1, 1);

        /// <summary>Counts up with every window that is hidden; 0 for a window that is showing.</summary>
        public int HiddenOrder { get; set; }
        public bool IsHidden => HiddenOrder > 0;
        public bool ClosesForGood { get; set; }
    }

    /// <summary>A window that lives on in the notification area after being closed or minimized.</summary>
    public sealed record HiddenWindow(string Title, Action Show, Action Close);

    private readonly List<Entry> _windows = [];
    private int _opened;
    private int _hidden;

    public Window? MainWindow => _windows.FirstOrDefault()?.Window;

    /// <summary>The frame of the first window, for what the notification area asks of a window.</summary>
    public ShellPage? MainShell => _windows.FirstOrDefault()?.Shell;

    /// <summary>The hidden windows, in the order they were hidden.</summary>
    public IReadOnlyList<HiddenWindow> HiddenWindows =>
        _windows.Where(entry => entry.IsHidden).OrderBy(entry => entry.HiddenOrder)
                .Select(entry => new HiddenWindow(entry.Window.Title, () => Show(entry), () => CloseForGood(entry)))
                .ToList();

    public void OpenWindow()
    {
        var entry = new Entry(new Window { ExtendsContentIntoTitleBar = true }, ++_opened);
        Window window = entry.Window;
        window.Title = TitleOf(entry.Number);
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _windows.Add(entry);
        BuildShell(entry, ShellPage.StartPage);
        RestoreSize(window);

        // Only the first window's size is remembered; the others are side views.
        if (entry.Number == 1) window.AppWindow.Changed += RememberSize;

        // While the app has its icon in the notification area, closing a window only hides it,
        // and so does minimizing it if the user asked for that.
        window.AppWindow.Closing += (_, args) =>
        {
            if (entry.ClosesForGood || !settings.Current.EnableTrayIcon) return;
            args.Cancel = true;
            Hide(entry);
        };
        window.AppWindow.Changed += (sender, _) =>
        {
            if (!entry.IsHidden && settings.Current is { EnableTrayIcon: true, MinimizeToTray: true }
                && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) Hide(entry);
        };

        window.Closed += (_, _) =>
        {
            _windows.Remove(entry);
            entry.Scope?.Dispose();
        };
        window.Activate();
    }

    /// <summary>Brings the first window to the front, e.g. when the app is started a second time.</summary>
    public void ShowMainWindow()
    {
        if (_windows.FirstOrDefault() is { } first) Show(first);
    }

    /// <summary>Brings back the window that was hidden last, or the first window if none is hidden.</summary>
    public void ShowLastHidden()
    {
        if (_windows.Where(entry => entry.IsHidden).MaxBy(entry => entry.HiddenOrder) is { } last) Show(last);
        else ShowMainWindow();
    }

    public void ShowAllHidden()
    {
        foreach (Entry entry in _windows.Where(entry => entry.IsHidden).ToList()) Show(entry);
    }

    /// <summary>Closes every window, hidden ones included, which ends the app.</summary>
    public void CloseAll()
    {
        foreach (Entry entry in _windows.ToList()) CloseForGood(entry);
    }

    private static void Show(Entry entry)
    {
        entry.HiddenOrder = 0;
        AppWindow window = entry.Window.AppWindow;
        window.Show();
        if (window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        entry.Window.Activate();
    }

    private void Hide(Entry entry)
    {
        entry.HiddenOrder = ++_hidden;
        entry.Window.AppWindow.Hide();

        if (settings.Current.HasSeenTrayHint) return;
        notifier.HiddenToTray();
        settings.Update(current => current with { HasSeenTrayHint = true });
    }

    private static void CloseForGood(Entry entry)
    {
        entry.ClosesForGood = true;
        entry.Window.Close();
    }

    /// <summary>Applies the current theme settings to every window.</summary>
    public void ApplyTheme()
    {
        foreach (Entry entry in _windows) WindowTheme.Apply(entry.Window, entry.Shell, settings.Current);
    }

    /// <summary>
    /// Builds every window's content anew, returning to the page that was showing. Needed for
    /// changes that existing elements do not pick up: the language, and resources read at load.
    /// The script in the editor stays; scripts and quick actions that are running end.
    /// </summary>
    public void RebuildShells()
    {
        foreach (Entry entry in _windows)
        {
            entry.Window.Title = TitleOf(entry.Number);
            BuildShell(entry, entry.Shell.CurrentPage);
        }
    }

    private void BuildShell(Entry entry, string page)
    {
        IServiceScope? previous = entry.Scope;
        entry.Scope = services.CreateScope();
        entry.Scope.ServiceProvider.GetRequiredService<WindowContext>().Attach(entry.Window, entry.DialogQueue);

        if (previous is not null)
        {
            // The script in the editor may not be saved yet; it must not go with the old view models.
            entry.Scope.ServiceProvider.GetRequiredService<ScriptEditorViewModel>()
                .ContinueFrom(previous.ServiceProvider.GetRequiredService<ScriptEditorViewModel>());
            previous.Dispose();
        }

        ShellPage shell = entry.Scope.ServiceProvider.GetRequiredService<ShellPage>();
        entry.Shell = shell;
        entry.Window.Content = shell;
        entry.Window.SetTitleBar(shell.TitleBarElement);

        // The effective theme is only known once the page is in the window, and it follows Windows
        // while the app's own setting is "use system setting".
        shell.Loaded += (_, _) => WindowTheme.Apply(entry.Window, shell, settings.Current);
        shell.ActualThemeChanged += (_, _) => WindowTheme.Apply(entry.Window, shell, settings.Current);
        WindowTheme.Apply(entry.Window, shell, settings.Current);
        shell.Open(page);
    }

    private static string TitleOf(int number)
    {
        string app = Loc.Get("App.WindowTitle");
        return number == 1 ? app : $"{app} - {Loc.Format("App.WindowTitleSuffixFormat", number)}";
    }

    // The size is stored in device-independent pixels, so it means the same on every display.
    // Windows chooses the position, and on a small screen that can leave part of the window
    // outside of it, so the window is moved back in.
    private void RestoreSize(Window window)
    {
        double scale = ScaleOf(window);
        AppWindow appWindow = window.AppWindow;
        RectInt32 workArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(settings.Current.WindowWidth * scale), workArea.Width);
        int height = Math.Min((int)(settings.Current.WindowHeight * scale), workArea.Height);
        int x = Math.Clamp(appWindow.Position.X, workArea.X, workArea.X + workArea.Width - width);
        int y = Math.Clamp(appWindow.Position.Y, workArea.Y, workArea.Y + workArea.Height - height);
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void RememberSize(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || sender.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;
        if (MainWindow is not { } window) return;

        double scale = ScaleOf(window);
        int width = (int)(sender.Size.Width / scale);
        int height = (int)(sender.Size.Height / scale);
        settings.Update(current => current with { WindowWidth = width, WindowHeight = height });
    }

    private static double ScaleOf(Window window) =>
        GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
