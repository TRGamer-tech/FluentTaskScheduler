using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// The window that a set of pages and view models belongs to. Every window has its own
/// dependency-injection scope, and this is the scope's way to reach its window.
/// </summary>
public sealed class WindowContext
{
    private Window? _window;

    public Window Window => _window ?? throw new InvalidOperationException("This scope has not been given its window yet.");
    public XamlRoot XamlRoot => Window.Content.XamlRoot;
    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>The theme the window currently shows, for dialogs opened in it.</summary>
    public ElementTheme Theme => (Window.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;

    internal void Attach(Window window) => _window = window;
}
