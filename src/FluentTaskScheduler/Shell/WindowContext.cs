using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// The window that a set of pages and view models belongs to. Every window has its own
/// dependency-injection scope, and this is the scope's way to reach its window.
/// </summary>
public sealed class WindowContext
{
    private Window? _window;
    private SemaphoreSlim? _dialogQueue;

    public Window Window => _window ?? throw NotAttached();
    public XamlRoot XamlRoot => Window.Content.XamlRoot;
    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>The theme the window currently shows, for dialogs opened in it.</summary>
    public ElementTheme Theme => (Window.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;

    /// <summary>
    /// Lets the window show one dialog at a time. It belongs to the window, not to the scope:
    /// when the window's content is rebuilt, a dialog opened before stays open over the new content.
    /// </summary>
    public SemaphoreSlim DialogQueue => _dialogQueue ?? throw NotAttached();

    internal void Attach(Window window, SemaphoreSlim dialogQueue)
    {
        _window = window;
        _dialogQueue = dialogQueue;
    }

    private static InvalidOperationException NotAttached() => new("This scope has not been given its window yet.");
}
