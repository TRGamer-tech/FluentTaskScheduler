using System.Drawing;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// What a picture of a window with rounded corners keeps of its corners: the part of taking such
/// a picture that needs no window. Nothing else puts it to the test on the build machines, whose
/// windows have square corners.
/// </summary>
public class WindowPictureTests
{
    private static readonly Size Window = new(1280, 800);

    private static double Covered(int x, int y) => WindowPicture.Covered(x, y, Window);

    [Fact]
    public void TheFourPixelsAtTheVeryCorners_AreOutsideTheWindow()
    {
        Assert.Equal(0, Covered(0, 0));
        Assert.Equal(0, Covered(Window.Width - 1, 0));
        Assert.Equal(0, Covered(0, Window.Height - 1));
        Assert.Equal(0, Covered(Window.Width - 1, Window.Height - 1));
    }

    [Fact]
    public void WhereTheCurveOfACornerEnds_TheEdgeBelongsToTheWindowAgain()
    {
        Assert.Equal(1, Covered(8, 0));
        Assert.Equal(1, Covered(0, 8));
        Assert.Equal(1, Covered(Window.Width - 9, Window.Height - 1));
        Assert.Equal(1, Covered(Window.Width - 1, Window.Height - 9));
    }

    [Fact]
    public void TheMiddleOfTheWindowAndOfItsEdges_BelongsToTheWindow()
    {
        Assert.Equal(1, Covered(Window.Width / 2, Window.Height / 2));
        Assert.Equal(1, Covered(Window.Width / 2, 0));
        Assert.Equal(1, Covered(0, Window.Height / 2));
        Assert.Equal(1, Covered(Window.Width - 1, Window.Height / 2));
        Assert.Equal(1, Covered(Window.Width / 2, Window.Height - 1));
    }

    [Fact]
    public void AlongTheTopEdge_ACornerFadesInPixelByPixel()
    {
        double[] fromTheCorner = [.. Enumerable.Range(0, 9).Select(x => Covered(x, 0))];

        Assert.Equal(fromTheCorner.Order(), fromTheCorner);
        Assert.Equal(0, fromTheCorner[0]);
        Assert.Equal(1, fromTheCorner[^1]);
        Assert.Contains(fromTheCorner, covered => covered is > 0.2 and < 0.8);
    }

    [Fact]
    public void TheFourCorners_AreCutAlike()
    {
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                double topLeft = Covered(x, y);
                Assert.Equal(topLeft, Covered(Window.Width - 1 - x, y));
                Assert.Equal(topLeft, Covered(x, Window.Height - 1 - y));
                Assert.Equal(topLeft, Covered(Window.Width - 1 - x, Window.Height - 1 - y));
            }
        }
    }

    /// <summary>
    /// The pixel one in from the corner tells rounded corners from square ones because it is the
    /// window's own where the corner is square. Where it is round, it must lie wholly outside.
    /// </summary>
    [Fact]
    public void ThePixelThatTellsRoundedCornersFromSquareOnes_IsOutsideAWindowWithRoundedCorners()
    {
        Assert.Equal(0, Covered(WindowPicture.Telltale.X, WindowPicture.Telltale.Y));
    }
}
