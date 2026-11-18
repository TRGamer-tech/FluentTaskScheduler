using FluentTaskScheduler.Shell;
using FluentTaskScheduler.Updates;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Dialogs;

/// <summary>Shows the welcome tour and release notes in one window, through that window's queue of dialogs.</summary>
public sealed class WelcomeDialogs(IDialogService dialogs) : IWelcomeDialogs
{
    public Task ShowTourAsync() => dialogs.ShowAsync(new WelcomeTourDialog(new WelcomeTourViewModel()));

    public Task ShowReleaseNotesAsync(ReleaseNotes notes) => dialogs.ShowAsync(new ReleaseNotesDialog(notes));
}
