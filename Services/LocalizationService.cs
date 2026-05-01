using System;
using System.Collections.Generic;
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
            "zh-CN"
        };

        private static string _currentLanguage = "en-US";

        public static event EventHandler? LanguageChanged;

        public static string CurrentLanguage => _currentLanguage;

        public static void Initialize()
        {
            ApplyLanguage(NormalizeLanguage(SettingsService.Language), raiseEvent: false);
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
            SettingsService.Language = language;

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
                ResourceContext.SetGlobalQualifierValue("Language", language);
            }
            catch
            {
                // Ignore override failures and keep app running.
            }

            if (raiseEvent)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
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
