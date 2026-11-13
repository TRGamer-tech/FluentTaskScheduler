using System.Globalization;

namespace FluentTaskScheduler.Localization;

/// <summary>The languages the app is translated into.</summary>
public static class AppLanguages
{
    public const string Fallback = "en-US";

    public static ValueList<string> Supported { get; } = [Fallback, "de-DE", "ja-JP", "zh-CN"];

    /// <summary>
    /// The language to show: the one the user chose, or without a choice the supported language
    /// closest to the Windows display language, or English.
    /// </summary>
    public static string Resolve(string? chosen, CultureInfo windowsDisplayLanguage) =>
        Match(chosen) ?? Match(windowsDisplayLanguage.Name) ?? Fallback;

    private static string? Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string language = tag.Split('-')[0];
        return Supported.FirstOrDefault(supported => supported.Equals(tag, StringComparison.OrdinalIgnoreCase))
            ?? Supported.FirstOrDefault(supported => supported.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
    }
}
