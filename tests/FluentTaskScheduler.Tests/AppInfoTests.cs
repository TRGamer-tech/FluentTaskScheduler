namespace FluentTaskScheduler.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("1.9.0", 1, 9, 0)]
    [InlineData("V1.9.0", 1, 9, 0)]        // the tag of a release, which is what version 1.9 stored
    [InlineData("v2.0", 2, 0, 0)]
    [InlineData(" 1.10.3 ", 1, 10, 3)]
    [InlineData("1.9.0.0", 1, 9, 0)]       // an assembly version
    [InlineData("1.9.1-beta.2", 1, 9, 1)]
    [InlineData("1.9.1+4f2a9c1", 1, 9, 1)]
    public void ParseVersion_ReadsTheReleaseAVersionTextNames(string text, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), AppInfo.ParseVersion(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("V")]
    [InlineData("2")]
    [InlineData("1.x")]
    public void ParseVersion_HasNoAnswerForTextThatIsNoVersion(string? text)
    {
        Assert.Null(AppInfo.ParseVersion(text));
    }

    [Theory]
    [InlineData("1.9.0", true)]
    [InlineData("V1.9.0", true)]
    [InlineData("1.9.9", true)]
    [InlineData("1.10.0", false)]          // compared as numbers: 1.10 is the same as the build
    [InlineData("1.10", false)]
    [InlineData("V1.10.0", false)]
    [InlineData("1.10.1", false)]          // a newer version was here before: nothing new to the user
    [InlineData("999.0.0", false)]
    [InlineData("", true)]
    [InlineData("not a version", true)]
    public void IsNewerThan_ComparesTheBuildWithAStoredVersion(string stored, bool expected)
    {
        var app = new AppInfo(new Version(1, 10, 0, 0));

        Assert.Equal(expected, app.IsNewerThan(stored));
    }

    [Fact]
    public void VersionText_IsTheVersionAsReleasesAreNumbered()
    {
        Assert.Equal("1.9.0", new AppInfo(new Version(1, 9, 0, 0)).VersionText);
        Assert.Equal("2.1.0", new AppInfo(new Version(2, 1)).VersionText);
    }
}
