using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.ViewModels;

/// <summary>Durations the user types into a text box ("15m", "1h 30m").</summary>
internal static class DurationInput
{
    /// <summary>
    /// Reads <paramref name="text"/>; empty means none. A text the user left as it was shown
    /// (<paramref name="shownText"/>) stands for the stored value exactly, even where showing it
    /// as text dropped something.
    /// </summary>
    public static bool TryRead(string text, string shownText, TimeSpan stored, out TimeSpan value)
    {
        value = stored;
        if (text == shownText) return true;
        if (string.IsNullOrWhiteSpace(text))
        {
            value = TimeSpan.Zero;
            return true;
        }
        return DurationText.TryParse(text, out value);
    }
}
