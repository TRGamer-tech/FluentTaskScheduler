using FluentTaskScheduler.Settings;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentTaskScheduler.Shell;

/// <summary>Applies the theme settings to a window: light or dark, the backdrop, and the caption buttons.</summary>
internal static class WindowTheme
{
    public static void Apply(Window window, ShellPage shell, AppSettings settings)
    {
        shell.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        bool dark = shell.ActualTheme == ElementTheme.Dark;
        // Black beats Mica: Mica always lets some of the wallpaper through.
        bool oled = settings.IsOledMode && dark;
        bool mica = !oled && settings.IsMicaEnabled && MicaController.IsSupported();

        if (!mica) window.SystemBackdrop = null;
        else if (window.SystemBackdrop is not MicaBackdrop) window.SystemBackdrop = new MicaBackdrop();

        shell.ShowBackdrop(mica ? "Mica" : oled ? "Oled" : "Solid");
        ColorCaptionButtons(window.AppWindow, dark);
    }

    private static void ColorCaptionButtons(AppWindow appWindow, bool dark)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        Color ink = dark ? Colors.White : Colors.Black;
        AppWindowTitleBar bar = appWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ink;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedForegroundColor = ink;
        bar.ButtonInactiveForegroundColor = Color.FromArgb(100, ink.R, ink.G, ink.B);
        bar.ButtonHoverBackgroundColor = Color.FromArgb(25, ink.R, ink.G, ink.B);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(40, ink.R, ink.G, ink.B);
    }
}
