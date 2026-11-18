using System.Globalization;

namespace FluentTaskScheduler.Localization;

/// <summary>
/// User-visible text in the app's current language. XAML reads it with
/// <c>{x:Bind l:Loc.Get('Some.Key')}</c>, code with <see cref="Get"/> and <see cref="Format"/>.
/// The texts live in Strings/&lt;language&gt;/Resources.resw; en-US is the reference and the
/// fallback for a key another language does not have.
/// </summary>
public static class Loc
{
    private static Func<string, string?> _lookup = static _ => null;

    /// <summary>
    /// Installs the lookup behind <see cref="Get"/>. Until the app has done that (and in unit
    /// tests, which never do) every key resolves to itself.
    /// </summary>
    public static void Use(Func<string, string?> lookup) => _lookup = lookup;

    /// <summary>The text for <paramref name="key"/>, or the key itself when there is none, so a missing text is visible instead of blank.</summary>
    public static string Get(string key) => _lookup(key) is { Length: > 0 } text ? text : key;

    /// <summary>The text for <paramref name="key"/> with its {0}, {1}, ... placeholders filled in.</summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>Items in a row, separated the way the language separates them: "a, b" in English, "a、b" in Japanese.</summary>
    public static string List(IEnumerable<string> items) => string.Join(Get("Text.ListSeparator"), items);
}
