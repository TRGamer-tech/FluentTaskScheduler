namespace FluentTaskScheduler.Settings;

/// <summary>Holds the user's preferences and keeps them on disk.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Current"/> changed, on the thread that made the change.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Replaces the settings with the result of <paramref name="change"/>, e.g.
    /// <c>store.Update(s => s with { ConfirmDelete = false })</c>. Saving follows shortly after.
    /// </summary>
    void Update(Func<AppSettings, AppSettings> change);

    /// <summary>Writes pending changes now, e.g. before the app exits.</summary>
    void Flush();

    /// <summary>Writes the preferences to a file the user can keep or take to another machine.</summary>
    void Export(string path);

    /// <summary>
    /// Takes over the preferences from a file written by <see cref="Export"/>. What describes this
    /// machine rather than a preference (window size, last folder) stays as it is.
    /// </summary>
    /// <exception cref="InvalidDataException">The file does not hold settings of this app.</exception>
    void Import(string path);
}

public sealed class SettingsChangedEventArgs(AppSettings previous, AppSettings current) : EventArgs
{
    public AppSettings Previous { get; } = previous;
    public AppSettings Current { get; } = current;
}
