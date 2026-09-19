using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.Resources.Core;

namespace FluentTaskScheduler.Services
{
    public static class LocalizationService
    {
        private static readonly HashSet<string> _supportedLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "en-US",
            "de-DE",
            "zh-CN",
            "ja-JP"
        };

        private static string _currentLanguage = "en-US";

        private static ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        public static event EventHandler? LanguageChanged;

        public static string CurrentLanguage => _currentLanguage;

        public static void Initialize()
        {
            ApplyLanguage(NormalizeLanguage(Settings.Language), raiseEvent: false);
        }

        public static bool ChangeLanguage(string language)
        {
            string normalized = NormalizeLanguage(language);
            if (string.Equals(_currentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ApplyLanguage(normalized, raiseEvent: true);
            return true;
        }

        public static string GetString(string key, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            string? localized = ResolveString(key);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            string slashKey = key.Replace('.', '/');
            if (!string.Equals(slashKey, key, StringComparison.Ordinal))
            {
                localized = ResolveString(slashKey);
                if (!string.IsNullOrWhiteSpace(localized))
                {
                    return localized;
                }
            }

            try
            {
                string value = ResourceLoader.GetForViewIndependentUse().GetString(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore and fall back.
            }

            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }

        private static string? ResolveString(string key)
        {
            try
            {
                var context = ResourceContext.GetForViewIndependentUse();
                context.Languages = new[] { _currentLanguage };

                ResourceMap rootMap = ResourceManager.Current.MainResourceMap;
                ResourceMap? resourceMap = null;
                try
                {
                    resourceMap = rootMap.GetSubtree("Resources");
                }
                catch
                {
                    resourceMap = rootMap;
                }

                var candidate = resourceMap.GetValue(key, context);
                string? value = candidate?.ValueAsString;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore and let fallback pipeline continue.
            }

            return null;
        }

        private static void ApplyLanguage(string language, bool raiseEvent)
        {
            _currentLanguage = language;
            Settings.Language = language;

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
                ResourceContext.SetGlobalQualifierValue("Language", language);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not apply the WinRT language override for '{language}': {ex.Message}");
            }

            ApplyCulture(language);

            if (raiseEvent)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Aligns the .NET culture with the chosen app language.
        ///
        /// Without this, only .resw lookups follow the language picker while everything that goes
        /// through <see cref="System.Globalization.CultureInfo"/> keeps using the Windows locale —
        /// day names and dates on the dashboard, number formats, framework exception messages, and
        /// the trigger descriptions produced by the TaskScheduler library. That is what made an
        /// English app show German strings on a German Windows install.
        ///
        /// Note this cannot reach text produced by Windows itself (COM/Win32 error messages and the
        /// Task Scheduler event log's own rendered descriptions) — those always follow the OS
        /// display language.
        /// </summary>
        private static void ApplyCulture(string language)
        {
            try
            {
                var culture = new System.Globalization.CultureInfo(language);

                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Globalization.CultureInfo.CurrentCulture = culture;
                System.Globalization.CultureInfo.CurrentUICulture = culture;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("{Message}", $"Could not switch the .NET culture to '{language}': {ex.Message}");
            }
        }

        private static string NormalizeLanguage(string? language)
        {
            if (!string.IsNullOrWhiteSpace(language) && _supportedLanguages.Contains(language))
            {
                return language;
            }

            return "en-US";
        }
    }
}
