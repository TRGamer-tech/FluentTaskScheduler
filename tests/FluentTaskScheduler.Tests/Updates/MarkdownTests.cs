using FluentTaskScheduler.Updates;

namespace FluentTaskScheduler.Tests.Updates;

public class MarkdownTests
{
    private static MarkdownRun Plain(string text) => new(text);
    private static MarkdownRun Bold(string text) => new(text, Bold: true);
    private static MarkdownRun Italic(string text) => new(text, Italic: true);
    private static MarkdownRun Code(string text) => new(text, Code: true);
    private static MarkdownRun Link(string text, string address) => new(text, Link: address);

    /// <summary>The runs of a text that is a single paragraph.</summary>
    private static ValueList<MarkdownRun> Runs(string line) =>
        Assert.IsType<MarkdownParagraph>(Assert.Single(Markdown.Parse(line))).Text;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\r\n ")]
    public void NoText_GivesNoBlocks(string? markdown)
    {
        Assert.Empty(Markdown.Parse(markdown));
    }

    [Fact]
    public void Headings_HaveTheirLevel_AndKeepTheEmojiReleaseNotesLike()
    {
        var blocks = Markdown.Parse("# Title\n## 🚀 New Features\n###### Small ##");

        Assert.Equal(
            [
                new MarkdownHeading(1, [Plain("Title")]),
                new MarkdownHeading(2, [Plain("🚀 New Features")]),
                new MarkdownHeading(6, [Plain("Small")])
            ],
            blocks);
    }

    [Fact]
    public void LinesOfAParagraph_AreJoined_AndABlankLineStartsANewOne()
    {
        var blocks = Markdown.Parse("The main downloads are these.\r\nThe rest is for updates.\r\n\r\nA second paragraph.");

        Assert.Equal(
            [
                new MarkdownParagraph([Plain("The main downloads are these. The rest is for updates.")]),
                new MarkdownParagraph([Plain("A second paragraph.")])
            ],
            blocks);
    }

    [Fact]
    public void Bullets_MayUseAnyOfTheThreeMarkers_AndExtraSpaces()
    {
        var blocks = Markdown.Parse("- one\n* two\n+ three\n*   four");

        Assert.Equal(
            [
                new MarkdownListItem([Plain("one")]),
                new MarkdownListItem([Plain("two")]),
                new MarkdownListItem([Plain("three")]),
                new MarkdownListItem([Plain("four")])
            ],
            blocks);
    }

    [Fact]
    public void AnIndentedBullet_IsNestedInTheOneAboveIt_HoweverFarItIsIndented()
    {
        var blocks = Markdown.Parse("- top\n    - inside\n        - deeper\n    - inside again\n- top again");

        Assert.Equal([0, 1, 2, 1, 0], blocks.Cast<MarkdownListItem>().Select(item => item.Depth));
    }

    [Fact]
    public void NumberedItems_KeepTheirNumbering()
    {
        var blocks = Markdown.Parse("1. first\n2. second\n10) tenth");

        Assert.Equal(
            [
                new MarkdownListItem([Plain("first")], Number: "1."),
                new MarkdownListItem([Plain("second")], Number: "2."),
                new MarkdownListItem([Plain("tenth")], Number: "10)")
            ],
            blocks);
    }

    [Fact]
    public void ALineThatFollowsAListItem_ContinuesIt()
    {
        var blocks = Markdown.Parse("- a long entry that the author\n  wrapped by hand\n- next");

        Assert.Equal(
            [
                new MarkdownListItem([Plain("a long entry that the author wrapped by hand")]),
                new MarkdownListItem([Plain("next")])
            ],
            blocks);
    }

    [Fact]
    public void BoldItalicAndCode_AreTakenOutOfTheText()
    {
        Assert.Equal(
            [Bold("Task Chaining"), Plain(": uses "), Code("x:Uid"), Plain(", the "), Italic("Windows display language"), Plain(" and "), Italic("more"), Plain(".")],
            Runs("**Task Chaining**: uses `x:Uid`, the *Windows display language* and _more_."));
    }

    [Fact]
    public void EmphasisInsideEmphasis_AddsUp()
    {
        Assert.Equal(
            [Plain("It "), new MarkdownRun("does not", Bold: true, Italic: true), Plain(" need to be downloaded.")],
            Runs("It **_does not_** need to be downloaded."));
        Assert.Equal([new MarkdownRun("both", Bold: true, Italic: true)], Runs("***both***"));
        Assert.Equal([Bold("bold "), new MarkdownRun("and italic", Bold: true, Italic: true), Bold(" bold")], Runs("**bold *and italic* bold**"));
        Assert.Equal([Italic("italic "), new MarkdownRun("and bold", Bold: true, Italic: true), Italic(" italic")], Runs("*italic **and bold** italic*"));
    }

    [Fact]
    public void BoldMayHoldCode()
    {
        Assert.Equal(
            [Bold("The main downloads are "), new MarkdownRun("Setup-x64.msi", Bold: true, Code: true), Bold(" and more")],
            Runs("**The main downloads are `Setup-x64.msi` and more**"));
    }

    [Theory]
    [InlineData("snake_case_name stays")]
    [InlineData("2 * 3 * 4")]
    [InlineData("a lone * star")]
    [InlineData("an *unclosed emphasis")]
    [InlineData("an unclosed `code")]
    [InlineData("four **** stars")]
    [InlineData("a [bracket] without an address")]
    [InlineData("a < b > c")]
    public void WhatOnlyLooksLikeFormatting_StaysText(string line)
    {
        Assert.Equal([Plain(line)], Runs(line));
    }

    [Fact]
    public void ABackslash_MakesTheNextCharacterPlainText()
    {
        Assert.Equal([Plain("*not italic* and a \\ backslash")], Runs(@"\*not italic\* and a \ backslash"));
    }

    [Fact]
    public void CodeIsTakenLiterally_WhateverItContains()
    {
        Assert.Equal([Code("**not bold** [nor](a link)")], Runs("`**not bold** [nor](a link)`"));
        Assert.Equal([Code("a ` inside")], Runs("`` a ` inside ``"));
        Assert.Equal([Italic("a "), new MarkdownRun("*.tmp", Italic: true, Code: true), Italic(" file")], Runs("*a `*.tmp` file*"));
    }

    [Fact]
    public void Links_KeepTheirTextAndAddress_AndTheTextMayBeFormatted()
    {
        Assert.Equal(
            [Plain("(Fixing "), Link("#5", "https://github.com/TRGamer-tech/FluentTaskScheduler/issues/5"), Plain(")")],
            Runs("(Fixing [#5](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/5))"));
        Assert.Equal(
            [Plain("See "), new MarkdownRun("the docs", Bold: true, Link: "https://example.com/a_(b)"), Plain(".")],
            Runs("See [**the docs**](https://example.com/a_(b) \"a title\")."));
    }

    [Fact]
    public void AnImage_BecomesALinkToIt()
    {
        Assert.Equal([Link("Screenshot", "https://example.com/shot.png")], Runs("![Screenshot](https://example.com/shot.png)"));
        Assert.Equal([Link("https://example.com/shot.png", "https://example.com/shot.png")], Runs("![](https://example.com/shot.png)"));
    }

    [Fact]
    public void AnAddressWrittenOut_IsALink_WithoutThePunctuationThatFollowsIt()
    {
        Assert.Equal(
            [Plain("See "), Link("https://example.com/a?b=1", "https://example.com/a?b=1"), Plain(", or ("), Link("http://example.org", "http://example.org"), Plain(").")],
            Runs("See https://example.com/a?b=1, or (http://example.org)."));
        Assert.Equal([Link("https://example.com", "https://example.com")], Runs("<https://example.com>"));
    }

    [Fact]
    public void TheNotesGitHubGenerates_ReadAsHeadingsAndItemsWithShortLinks()
    {
        const string generated = """
            <!-- Release notes generated using configuration in .github/release.yml at V1.9.1 -->

            ## What's Changed
            * Fix the tray icon by @someone in https://github.com/TRGamer-tech/FluentTaskScheduler/pull/31

            **Full Changelog**: https://github.com/TRGamer-tech/FluentTaskScheduler/compare/V1.9.0...V1.9.1
            """;

        const string compare = "https://github.com/TRGamer-tech/FluentTaskScheduler/compare/V1.9.0...V1.9.1";
        Assert.Equal(
            [
                new MarkdownHeading(2, [Plain("What's Changed")]),
                new MarkdownListItem([Plain("Fix the tray icon by @someone in "), Link("#31", "https://github.com/TRGamer-tech/FluentTaskScheduler/pull/31")]),
                new MarkdownParagraph([Bold("Full Changelog"), Plain(": "), Link(compare, compare)])
            ],
            Markdown.Parse(generated));
    }

    [Fact]
    public void AnIssueOfAnotherProject_KeepsThatProjectInItsShortName_AsOnGitHub()
    {
        const string runtime = "https://github.com/dotnet/runtime/issues/12345";
        Assert.Equal(
            [Plain("Fixed a crash in the runtime: "), Link("dotnet/runtime#12345", runtime)],
            Runs("Fixed a crash in the runtime: " + runtime));

        // GitHub does not tell owners and names apart by case: this is the app's own project.
        const string own = "https://github.com/trgamer-tech/fluenttaskscheduler/pull/31";
        Assert.Equal([Link("#31", own)], Runs(own));
    }

    [Fact]
    public void ATable_GivesItsCellsRowByRow_HeadingsFirst()
    {
        const string table = """
            | Artifact | SHA-256 Hash |
            | :--- | :--- |
            | **Setup-x64.msi** | `833360EB` |
            | Portable \| zip |
            after the table
            """;

        Assert.Equal(
            [
                new MarkdownTable(2,
                [
                    [Plain("Artifact")], [Plain("SHA-256 Hash")],
                    [Bold("Setup-x64.msi")], [Code("833360EB")],
                    [Plain("Portable | zip")], []
                ]),
                new MarkdownParagraph([Plain("after the table")])
            ],
            Markdown.Parse(table));
    }

    [Fact]
    public void ALineWithABar_ThatNoDividerFollows_IsOrdinaryText()
    {
        Assert.Equal([Plain("this | that")], Runs("this | that"));
    }

    [Fact]
    public void FencedCode_IsKeptLineByLine_Unformatted()
    {
        var blocks = Markdown.Parse("Run:\n```powershell\nwinget upgrade **all**\n  --silent\n```\nDone.");

        Assert.Equal(
            [
                new MarkdownParagraph([Plain("Run:")]),
                new MarkdownCode("winget upgrade **all**\n  --silent"),
                new MarkdownParagraph([Plain("Done.")])
            ],
            blocks);
    }

    [Fact]
    public void ADividingLine_EndsTheParagraph_AndLeavesNothingBehind()
    {
        var blocks = Markdown.Parse("above\n---\nbelow\n\n* * *\n");

        Assert.Equal([new MarkdownParagraph([Plain("above")]), new MarkdownParagraph([Plain("below")])], blocks);
    }

    [Fact]
    public void TheNotesOfVersion19_AreReadWithoutLeavingMarkupInTheText()
    {
        const string notes = "## 🧰 Package Manager Status:\r\n- Winget: 🥉Updated!\r\n\r\n## ⬇️ Downloading the App\r\n"
            + "**The main downloads are `Setup-x64.msi`, `Setup-arm64.msi`, `Portable-x64.msi` and `Portable-arm64.zip`**\r\n"
            + "The rest is for the auto update functionality and **_does not_** need to be downloaded in order for the app to work.\r\n\r\n"
            + "## 🚀 New Features\r\n- **Task Chaining (Completion Actions)**: Chain tasks together.\r\n"
            + "- **Actually Multilingual Now**: Several dialogs relied on `x:Uid`, which follows the *Windows display language* instead of the in-app picker.\r\n\r\n"
            + "## 📎 Artifacts and Hashes (for your reference)\r\n| Artifact | SHA-256 Hash |\r\n| :--- | :--- |\r\n"
            + "| **Setup-x64.msi** | `833360EBB51466EE2C207FBB0BBFC7327FD1E31D125E64249F71BEF9DE11A944` |\r\n\r\n";

        var blocks = Markdown.Parse(notes);

        Assert.Equal(
            ["Heading", "ListItem", "Heading", "Paragraph", "Heading", "ListItem", "ListItem", "Heading", "Table"],
            blocks.Select(block => block.GetType().Name.Replace("Markdown", "")));

        IEnumerable<MarkdownRun> runs = blocks.SelectMany(block => block switch
        {
            MarkdownHeading heading => heading.Text,
            MarkdownParagraph paragraph => paragraph.Text,
            MarkdownListItem item => item.Text,
            MarkdownTable table => table.Cells.SelectMany(cell => cell),
            _ => []
        });
        Assert.DoesNotContain(runs, run => !run.Code && run.Text.IndexOfAny(['*', '`', '|']) >= 0);
        Assert.DoesNotContain(runs, run => run.Text.Contains("_does"));
    }
}
