using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

public class TaskPathsTests
{
    [Theory]
    [InlineData(@"\", "Backup", @"\Backup")]
    [InlineData(@"\Jobs", "Backup", @"\Jobs\Backup")]
    [InlineData(@"\Jobs\", "Backup", @"\Jobs\Backup")]
    public void Combine_JoinsAFolderAndAName(string folder, string name, string expected) =>
        Assert.Equal(expected, TaskPaths.Combine(folder, name));

    [Theory]
    [InlineData(@"\Backup", @"\", "Backup")]
    [InlineData(@"\Jobs\Backup", @"\Jobs", "Backup")]
    [InlineData(@"\Jobs\Nightly\", @"\Jobs", "Nightly")]
    [InlineData(@"\", @"\", "")]
    public void APath_SplitsIntoParentAndName(string path, string parent, string name)
    {
        Assert.Equal(parent, TaskPaths.ParentOf(path));
        Assert.Equal(name, TaskPaths.NameOf(path));
    }

    [Theory]
    [InlineData(@"\Jobs", @"\Jobs", true)]
    [InlineData(@"\jobs\Nightly", @"\Jobs", true)]
    [InlineData(@"\Jobs2", @"\Jobs", false)]
    [InlineData(@"\Other\Jobs", @"\Jobs", false)]
    [InlineData(@"\Anything", @"\", true)]
    public void IsWithin_MeansTheFolderItselfOrBelowIt_NotANameThatStartsTheSame(string path, string folder, bool expected) =>
        Assert.Equal(expected, TaskPaths.IsWithin(path, folder));
}
