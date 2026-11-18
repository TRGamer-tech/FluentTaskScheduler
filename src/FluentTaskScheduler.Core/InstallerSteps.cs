namespace FluentTaskScheduler;

/// <summary>What the installer or the updater started the app for: a start without a window that has a few seconds.</summary>
public enum InstallerStep
{
    /// <summary>The app was not started by them.</summary>
    None,

    /// <summary>The app has just been installed.</summary>
    Installed,

    /// <summary>The app is about to be uninstalled.</summary>
    Uninstalling,

    /// <summary>Any other step, such as an update: it asks nothing of the app.</summary>
    Other
}

/// <summary>
/// Tells the starts by which the installer and the updater let the app take part in being
/// installed, updated and uninstalled. Velopack names the step in the first argument.
/// </summary>
public static class InstallerSteps
{
    private const string Prefix = "--veloapp-";

    /// <summary>
    /// Goes by the prefix alone, so that a step this version has never heard of is still one:
    /// whatever the step, such a start must end without showing the window.
    /// </summary>
    public static InstallerStep Of(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || !args[0].StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return InstallerStep.None;

        return args[0][Prefix.Length..].ToLowerInvariant() switch
        {
            "install" => InstallerStep.Installed,
            "uninstall" => InstallerStep.Uninstalling,
            _ => InstallerStep.Other
        };
    }
}
