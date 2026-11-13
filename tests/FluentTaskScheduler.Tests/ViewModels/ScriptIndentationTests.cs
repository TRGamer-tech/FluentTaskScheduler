using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class ScriptIndentationTests
{
    /// <summary>
    /// Presses Tab in the text, where "|" marks the caret, or "[" and "]" the selection, and gives
    /// back the text Tab leaves, marked the same way. Lines end in CR, as in a text box.
    /// </summary>
    private static string PressTab(string text)
    {
        int caret = text.IndexOf('|');
        int start = caret >= 0 ? caret : text.IndexOf('[');
        int end = caret >= 0 ? caret : text.IndexOf(']') - 1;
        string plain = text.Replace("|", "").Replace("[", "").Replace("]", "");

        TextEdit edit = ScriptIndentation.Tab(plain, start, end - start);

        string edited = plain[..edit.Start] + edit.Replacement + plain[(edit.Start + edit.Length)..];
        return edit.SelectionLength == 0
            ? edited.Insert(edit.SelectionStart, "|")
            : edited.Insert(edit.SelectionStart + edit.SelectionLength, "]").Insert(edit.SelectionStart, "[");
    }

    [Fact]
    public void WithoutASelection_TheIndentIsInsertedAtTheCaret()
    {
        Assert.Equal("    |Get-Date", PressTab("|Get-Date"));
        Assert.Equal("Get-Date    |\rGet-Item", PressTab("Get-Date|\rGet-Item"));
    }

    [Fact]
    public void ASelectionWithinALine_IsReplacedByTheIndent()
    {
        Assert.Equal("Remove-Item     |.txt", PressTab("Remove-Item [old].txt"));
    }

    [Fact]
    public void TheLinesASelectionTouches_AreIndented_AndStaySelected()
    {
        Assert.Equal("foreach ($f in $files) {\r[    Get-Item $f\r    Remove-Item $f]\r}", PressTab("foreach ($f in $files) {\r[Get-Item $f\rRemove-Item $f]\r}"));
        Assert.Equal("    Get-[Date\r    Get-It]em", PressTab("Get-[Date\rGet-It]em"));
    }

    [Fact]
    public void AWholeLineSelected_IsIndented_AsOftenAsTabIsPressed()
    {
        string once = PressTab("[Get-Date]\rGet-Item");

        Assert.Equal("[    Get-Date]\rGet-Item", once);
        Assert.Equal("[        Get-Date]\rGet-Item", PressTab(once));
    }

    [Fact]
    public void ALineTheSelectionEndsAtTheStartOf_IsLeftAsItIs()
    {
        Assert.Equal("[    Get-Date\r]Get-Item", PressTab("[Get-Date\r]Get-Item"));
    }

    [Fact]
    public void EmptyLines_StayEmpty()
    {
        Assert.Equal("[    one\r\r    two]", PressTab("[one\r\rtwo]"));
    }

    [Fact]
    public void LinesEndingInLfOrCrLf_AreIndentedToo()
    {
        Assert.Equal("[    one\r\n    two\n    three]", PressTab("[one\r\ntwo\nthree]"));
    }
}
