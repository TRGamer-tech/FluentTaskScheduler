using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>Without resources a text shows its resource names, which is what these tests read.</summary>
public class ActionTextTests
{
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
        Assert.Equal("Action.Unsupported (SendEmail)", ActionText.Describe(new ActionDraft { SourceIndex = 0, UnsupportedSummary = "SendEmail" }));
    }
}
