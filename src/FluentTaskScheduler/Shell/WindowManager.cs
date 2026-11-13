using System.Runtime.InteropServices;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// Creates the app's windows and keeps track of them. Each window gets its own service scope,
/// so its pages and view models live and die with it.
/// </summary>
public sealed class WindowManager(IServiceProvider services, ISettingsStore settings)
{
    private sealed class Entry(Window window, int number)
    {
        public Window Window { get; } = window;
        public int Number { get; } = number;
        public IServiceScope? Scope { get; set; }
        public ShellPage Shell { get; set; } = null!;
    }

    private readonly List<Entry> _windows = [];
    private int _opened;

    public Window? MainWindow => _windows.FirstOrDefault()?.Window;

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
        if (MainWindow is not { } window) return;
        window.AppWindow.Show();
        window.Activate();
    }

    /// <summary>Applies the current theme settings to every window.</summary>
    public void ApplyTheme()
    {
        foreach (Entry entry in _windows) WindowTheme.Apply(entry.Window, entry.Shell, settings.Current);
    }

    /// <summary>
    /// Builds every window's content anew, returning to the page that was showing. Needed for
    /// changes that existing elements do not pick up: the language, and resources read at load.
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
        entry.Scope?.Dispose();
        entry.Scope = services.CreateScope();
        entry.Scope.ServiceProvider.GetRequiredService<WindowContext>().Attach(entry.Window);

        ShellPage shell = entry.Scope.ServiceProvider.GetRequiredService<ShellPage>();
        entry.Shell = shell;
        entry.Window.Content = shell;
        entry.Window.SetTitleBar(shell.TitleBarElement);

        // The effective theme is only known once the page is in the window, and it follows Windows
        // while the app's own setting is "use system setting".
        shell.Loaded += (_, _) => WindowTheme.Apply(entry.Window, shell, settings.Current);
        shell.ActualThemeChanged += (_, _) => WindowTheme.Apply(entry.Window, shell, settings.Current);
        WindowTheme.Apply(entry.Window, shell, settings.Current);
        shell.Navigate(page);
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
