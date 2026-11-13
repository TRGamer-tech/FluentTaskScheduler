using System.Globalization;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// Applies what the user chose about the app's look (language, theme, a few app-wide control
/// defaults) and keeps it applied when the settings change.
/// </summary>
public sealed class AppAppearance(ISettingsStore settings, WindowManager windows, IUiThread ui)
{
    // Read before the app replaces the UI culture with its own choice.
    private static readonly CultureInfo WindowsDisplayLanguage = CultureInfo.CurrentUICulture;

    private readonly ResourceStrings _strings = new(AppLanguages.Fallback);

    /// <summary>Call once, before the first window is created.</summary>
    public void Start()
    {
        Loc.Use(_strings.Find);
        ApplyLanguage();
        ApplyControlDefaults();
        settings.Changed += (_, change) => ui.Post(() => React(change.Previous, change.Current));
    }

    private void React(AppSettings before, AppSettings now)
    {
        if (now.Language != before.Language) ApplyLanguage();

        if (now.Language != before.Language || now.IsOledMode != before.IsOledMode || now.SmoothScrolling != before.SmoothScrolling)
        {
            // Existing elements keep the texts and styles they were created with.
            ApplyControlDefaults();
            windows.RebuildShells();
        }
        else if (now.Theme != before.Theme || now.IsMicaEnabled != before.IsMicaEnabled)
        {
            windows.ApplyTheme();
        }
    }

    private void ApplyLanguage()
    {
        string language = AppLanguages.Resolve(settings.Current.Language, WindowsDisplayLanguage);
        _strings.UseLanguage(language);

        // Texts follow the app's language; dates and numbers keep following the Windows region.
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    // App-wide implicit styles, set from code because their values come from the settings and
    // the current language.
    private void ApplyControlDefaults()
    {
        ResourceDictionary resources = Application.Current.Resources;

        // Without this a switch labels its states in the language of Windows, not of the app.
        var toggle = new Style(typeof(ToggleSwitch)) { BasedOn = (Style)resources["DefaultToggleSwitchStyle"] };
        toggle.Setters.Add(new Setter(ToggleSwitch.OnContentProperty, Loc.Get("Dialog.Common.On")));
        toggle.Setters.Add(new Setter(ToggleSwitch.OffContentProperty, Loc.Get("Dialog.Common.Off")));
        resources[typeof(ToggleSwitch)] = toggle;

        var scrolling = new Style(typeof(ScrollViewer)) { BasedOn = (Style)resources["DefaultScrollViewerStyle"] };
        scrolling.Setters.Add(new Setter(ScrollViewer.IsScrollInertiaEnabledProperty, settings.Current.SmoothScrolling));
        resources[typeof(ScrollViewer)] = scrolling;
    }
}
