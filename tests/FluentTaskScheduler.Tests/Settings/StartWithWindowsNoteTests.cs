using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Settings;

public sealed class StartWithWindowsNoteTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeStartWithWindows _startup = new();

    public void Dispose() => _folder.Dispose();

    private StartWithWindowsNote Note => new(_startup, _folder.Paths);
    private string NoteFile => _folder.File("start-with-windows");

    [Fact]
    public void AnUninstallFollowedByAnInstallation_LeavesTheAppStartingWithWindows()
    {
        _startup.IsEnabled = true;

        Note.TakeEntryAway();

        Assert.False(_startup.IsEnabled);

        Note.PutEntryBack();

        Assert.True(_startup.IsEnabled);
        Assert.False(File.Exists(NoteFile));
    }

    [Fact]
    public void AnAppThatDidNotStartWithWindows_DoesNotAfterAReinstallationEither()
    {
        Note.TakeEntryAway();
        Note.PutEntryBack();

        Assert.False(_startup.IsEnabled);
        Assert.False(File.Exists(NoteFile));
    }

    [Fact]
    public void TheNote_IsWrittenAlsoWhereTheDataFolderDoesNotExistYet()
    {
        var paths = new AppPaths(_folder.File("NotThereYet"));
        _startup.IsEnabled = true;

        new StartWithWindowsNote(_startup, paths).TakeEntryAway();
        new StartWithWindowsNote(_startup, paths).PutEntryBack();

        Assert.True(_startup.IsEnabled);
    }

    [Fact]
    public void WithoutANote_AStartChangesNothing()
    {
        Note.PutEntryBack();

        Assert.False(_startup.IsEnabled);
    }

    [Fact]
    public void WhileWindowsRefusesTheEntry_TheNoteStays_AndALaterStartTriesAgain()
    {
        _startup.IsEnabled = true;
        Note.TakeEntryAway();
        _startup.Refuses = true;

        Note.PutEntryBack();

        Assert.True(File.Exists(NoteFile));

        _startup.Refuses = false;
        Note.PutEntryBack();

        Assert.True(_startup.IsEnabled);
        Assert.False(File.Exists(NoteFile));
    }

    [Fact]
    public void TheNote_IsOneOfTheAppsFiles_SoDeletingTheAppsDataTakesItAlong()
    {
        _startup.IsEnabled = true;
        Note.TakeEntryAway();

        DataFolderDeletion.Run(_folder.Paths);
        Note.PutEntryBack();

        Assert.False(_startup.IsEnabled);
    }
}
