using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Library;
using FluentTaskScheduler.Tests.Snoozing;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>The descriptions with the app's English texts in place.</summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class ActionTextTests : IDisposable
{
    public ActionTextTests()
    {
        var english = RepoFiles.Texts("en-US");
        Loc.Use(key => english.GetValueOrDefault(key));
    }

    public void Dispose() => Loc.Use(static _ => null);

    [Theory]
    [InlineData("cmd.exe", "/c exit 0", "cmd.exe /c exit 0")]
    [InlineData("backup.exe", "", "backup.exe")]
    public void AProgram_IsDescribedByWhatItRunsWithWhichArguments(string path, string arguments, string expected)
    {
        Assert.Equal(expected, ActionText.Describe(new ActionDraft { Path = path, Arguments = arguments, WorkingDirectory = @"C:\Work" }));
    }

    [Fact]
    public void AnActionTheEditorCannotChange_IsCalledUnsupported_AndNamesItsKind()
    {
        Assert.Equal("Unsupported action (send an email)", ActionText.Describe(new ActionDraft { SourceIndex = 0, UnsupportedSummary = "send an email" }));
    }
}
