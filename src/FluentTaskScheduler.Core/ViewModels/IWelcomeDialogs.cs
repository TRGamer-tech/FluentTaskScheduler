using FluentTaskScheduler.Updates;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// The dialogs that introduce the app and its releases, in the window they belong to. Both
/// return when the dialog has been closed, and wait their turn if another dialog is open.
/// </summary>
public interface IWelcomeDialogs
{
    Task ShowTourAsync();

    Task ShowReleaseNotesAsync(ReleaseNotes notes);
}
