using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Updates;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>Shows what a release says about itself.</summary>
public sealed partial class ReleaseNotesDialog : ContentDialog
{
    public ReleaseNotesDialog(ReleaseNotes notes)
    {
        Notes = notes;
        ReleasePage = MarkdownView.WebAddress(notes.Url);
        InitializeComponent();
        MarkdownView.Show(Markdown.Parse(notes.Markdown), NotesPanel);
    }

    public ReleaseNotes Notes { get; }

    public string Heading => Loc.Format("WhatsNew.TitleFormat", Notes.Version);
    public bool HasTitle => Notes.Title.Length > 0;

    public Uri? ReleasePage { get; }
    public bool HasReleasePage => ReleasePage is not null;
}
