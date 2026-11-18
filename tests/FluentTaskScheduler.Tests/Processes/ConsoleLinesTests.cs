using FluentTaskScheduler.Processes;

namespace FluentTaskScheduler.Tests.Processes;

public sealed class ConsoleLinesTests
{
    /// <summary>Hands out the given pieces one read at a time, the way output arrives from a program that pauses in between.</summary>
    private sealed class PiecewiseReader(params string[] pieces) : TextReader
    {
        private readonly Queue<string> _pieces = new(pieces);

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!_pieces.TryDequeue(out string? piece)) return ValueTask.FromResult(0);

            piece.CopyTo(buffer.Span);
            return ValueTask.FromResult(piece.Length);
        }
    }

    /// <summary>What was reported, as "text" for a complete line and "text..." for one that is not.</summary>
    private static async Task<List<string>> Read(params string[] pieces)
    {
        var reported = new List<string>();
        await ConsoleLines.ReadAsync(new PiecewiseReader(pieces), (line, isComplete) => reported.Add(isComplete ? line : line + "..."));
        return reported;
    }

    [Fact]
    public async Task Lines_EndWithAnyOfTheUsualLineEnds()
    {
        Assert.Equal(["one", "two", "", "three"], await Read("one\r\ntwo\n\nthree\r\n"));
    }

    [Fact]
    public async Task TextWithoutALineEnd_IsReportedWhenItArrives_AndAgainWhenItsLineIsComplete()
    {
        Assert.Equal(["Working...", "Working, still...", "Working, still, done"], await Read("Working", ", still", ", done\n"));
    }

    [Fact]
    public async Task TheLastLine_IsCompleteWhenTheProgramEnds_EvenWithoutALineEnd()
    {
        Assert.Equal(["first", "last...", "last"], await Read("first\nlast"));
    }

    [Fact]
    public async Task ACarriageReturnAlone_StartsTheLineOver_SoAProgressDisplayIsOneLineThatChanges()
    {
        Assert.Equal(
            ["Scanning", "10%...", "55%...", "100%", "Done"],
            await Read("Scanning\r\n", "\r10%", "\r55%", "\r100%\r\n", "Done\r\n"));
    }

    [Fact]
    public async Task ProgressThatArrivesInOnePiece_IsReportedStepByStep()
    {
        Assert.Equal(["10%...", "55%...", "100%"], await Read("10%\r55%\r100%\n"));
    }

    [Fact]
    public async Task ALineEndSplitBetweenTwoPieces_IsStillOneLineEnd()
    {
        Assert.Equal(["one...", "one", "two"], await Read("one\r", "\ntwo\n"));
    }

    [Fact]
    public async Task ACarriageReturnRightAfterAnother_ChangesNothing_SoLinesEndingInCrCrLfKeepTheirText()
    {
        Assert.Equal(["one", "two"], await Read("one\r\r\ntwo\r\r\n"));
        Assert.Equal(["one...", "one"], await Read("one\r", "\r\n"));
    }

    [Fact]
    public async Task NothingIsReportedTwice_WhenAPieceAddsNothingToTheLine()
    {
        Assert.Equal(["one...", "one"], await Read("one", "\r", "\n"));
    }

    [Fact]
    public async Task ALineWithoutEnd_IsBrokenUp_InsteadOfGrowingForever()
    {
        string piece = new('x', 4096);
        int pieces = 2 * ConsoleLines.MaxLineLength / piece.Length + 1;

        List<string> reported = await Read(Enumerable.Repeat(piece, pieces).ToArray());

        Assert.All(reported, line => Assert.True(line.Length <= ConsoleLines.MaxLineLength + "...".Length));
        Assert.Equal(pieces * piece.Length, reported.Where(line => !line.EndsWith("...")).Sum(line => line.Length));
    }
}
