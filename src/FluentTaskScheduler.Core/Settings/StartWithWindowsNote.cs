namespace FluentTaskScheduler.Settings;

/// <summary>
/// Carries "the app starts with Windows" from an uninstall to the installation that follows it.
/// </summary>
/// <remarks>
/// Uninstalling takes the app's entry out of what Windows starts at sign-in. But a newer Setup
/// MSI that is installed over this version, which is how winget and Chocolatey upgrade the app,
/// uninstalls it first as well, and nothing tells the app that an installation follows. The app
/// would then stay closed after the next sign-in: no notifications, and a snooze that ran out
/// would not end. So the uninstall leaves a note with the app's files, which stay, and whatever
/// finds the note, the installation or the next start, puts the entry back.
/// </remarks>
public sealed class StartWithWindowsNote(IStartWithWindows startup, AppPaths paths)
{
    internal const string FileName = "start-with-windows";

    private string File => paths.File(FileName);

    /// <summary>For the uninstall: the entry goes, and the note says that there was one.</summary>
    public void TakeEntryAway()
    {
        bool wasEnabled = startup.IsEnabled;
        startup.TrySetEnabled(false);
        if (!wasEnabled) return;

        try
        {
            Directory.CreateDirectory(paths.DataFolder);
            System.IO.File.WriteAllBytes(File, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the note the app does not start with Windows after a reinstallation, as after any first installation.
        }
    }

    /// <summary>For an installation and for every start: with a note, the entry comes back. The note stays until it has.</summary>
    public void PutEntryBack()
    {
        if (!System.IO.File.Exists(File) || !startup.TrySetEnabled(true)) return;

        try
        {
            System.IO.File.Delete(File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next start finds it again and does the same, which changes nothing.
        }
    }
}
