using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Updates;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// What the app brings up of its own accord once a window is there: the welcome tour at the very
/// first start, the release notes after an update, and the offer to install an update that has
/// been downloaded. There is one per window; it may run for every window and again whenever a
/// window's content is rebuilt, and still each of the three comes up once.
/// </summary>
public sealed class StartupFlow(
    ISettingsStore settings,
    AppInfo app,
    IReleaseNotesSource releaseNotes,
    UpdateService updates,
    IWelcomeDialogs dialogs,
    IPrompts prompts) : IDisposable
{
    private volatile bool _windowGone;

    /// <summary>Keeps the flow from showing anything more: its window was closed, or the window's content was built anew.</summary>
    public void Dispose() => _windowGone = true;

    /// <summary>Call when the window's content has loaded.</summary>
    public async Task RunAsync()
    {
        // Looking for an update takes its time; the question it may lead to waits its turn behind the dialogs below.
        Task update = OfferUpdateAsync();
        await IntroduceAsync();
        await update;
    }

    private async Task IntroduceAsync()
    {
        if (!settings.Current.HasCompletedOnboarding)
        {
            // Noted before the tour shows, so that a second window does not show it as well. Someone who
            // is new has no earlier version to compare this one with: no release notes for them.
            settings.Update(s => s with { HasCompletedOnboarding = true, LastSeenVersion = app.VersionText });
            await dialogs.ShowTourAsync();
            return;
        }

        if (!HasUnseenRelease) return;
        ReleaseNotes? notes = await releaseNotes.FindAsync(app.VersionText);

        // Without notes nothing is noted either, so the next start asks again. Another window may
        // have shown them while this one was waiting, hence the second look.
        if (notes is null || _windowGone || !HasUnseenRelease) return;
        settings.Update(s => s with { LastSeenVersion = app.VersionText });
        await dialogs.ShowReleaseNotesAsync(notes);
    }

    private async Task OfferUpdateAsync()
    {
        if (await updates.CheckAfterStartAsync() is { Status: UpdateStatus.Ready } check && !_windowGone)
            await updates.OfferRestartAsync(prompts, check.Version);
    }

    private bool HasUnseenRelease => app.IsNewerThan(settings.Current.LastSeenVersion);
}
