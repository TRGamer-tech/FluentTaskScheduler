using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.Capturing;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// A picture of a window alone, as the README shows the app: what can be seen of the window, and
/// nothing of the desktop around it or behind it. It is taken off the screen, so the window has
/// to be in front and the display at a scale of 100 %.
/// </summary>
internal static class WindowPicture
{
    /// <summary>The radius Windows 11 rounds the corners of a window with, in pixels at a display scale of 100 %.</summary>
    private const double CornerRadius = 8;

    /// <summary>
    /// The pixel that tells whether the corners are rounded: one in from the top left corner both
    /// ways. In a window with square corners it lies just inside the border, in one with rounded
    /// corners outside the window.
    /// </summary>
    internal static readonly Point Telltale = new(1, 1);

    /// <summary>
    /// Takes the picture, with 32 bits to the pixel. Where Windows has rounded the corners of the
    /// window, what lies outside them is transparent.
    /// </summary>
    public static Bitmap Take(nint window)
    {
        Rectangle frame = TopLevelWindows.FrameOf(window);

        // Whether the corners are rounded cannot be asked. The preference a window may state says
        // "as Windows sees fit" either way, and in a virtual machine, which the build machines are,
        // Windows sees fit not to. So it is looked at: a pixel outside the window changes with what
        // is behind the window, a pixel of the window does not. The border changes as well, being
        // see-through, so the dark rectangle is also what gives it one colour all the way round.
        using Bitmap overWhite = OnScreen(window, frame, white: true);
        using Bitmap overDark = OnScreen(window, frame, white: false);

        Bitmap picture = overDark.Clone(new Rectangle(Point.Empty, overDark.Size), PixelFormat.Format32bppArgb);
        if (Differ(overWhite.GetPixel(Telltale.X, Telltale.Y), overDark.GetPixel(Telltale.X, Telltale.Y))) CutCorners(picture);
        return picture;
    }

    /// <summary>
    /// How much of the pixel at (<paramref name="x"/>, <paramref name="y"/>) a window of the given
    /// size with rounded corners covers: 1 inside the window, 0 outside it, and something in
    /// between along the curve of a corner, which is what keeps that curve smooth.
    /// </summary>
    internal static double Covered(int x, int y, Size window)
    {
        // How far the middle of the pixel lies beyond the centre of the nearest corner's curve,
        // towards the corner: not at all in a direction in which the pixel is past the corner.
        double acrossX = Math.Max(0, Math.Max(CornerRadius - (x + 0.5), x + 0.5 - (window.Width - CornerRadius)));
        double acrossY = Math.Max(0, Math.Max(CornerRadius - (y + 0.5), y + 0.5 - (window.Height - CornerRadius)));
        return Math.Clamp(CornerRadius + 0.5 - Math.Sqrt(acrossX * acrossX + acrossY * acrossY), 0, 1);
    }

    private static Bitmap OnScreen(nint window, Rectangle frame, bool white)
    {
        nint rectangle = TopLevelWindows.ShowRectangleBehind(window, frame, white);
        try
        {
            return Capture.Rectangle(frame).Bitmap;
        }
        finally
        {
            TopLevelWindows.Destroy(rectangle);
        }
    }

    // White behind the window against dark grey is far more than anything else changes a pixel by from one moment to the next.
    private static bool Differ(Color one, Color other) => Math.Abs(one.GetBrightness() - other.GetBrightness()) > 0.2;

    private static void CutCorners(Bitmap picture)
    {
        int reach = (int)Math.Ceiling(CornerRadius);
        foreach (int x in Enumerable.Range(0, reach).Concat(Enumerable.Range(picture.Width - reach, reach)))
        {
            foreach (int y in Enumerable.Range(0, reach).Concat(Enumerable.Range(picture.Height - reach, reach)))
            {
                int opacity = (int)Math.Round(255 * Covered(x, y, picture.Size));
                picture.SetPixel(x, y, Color.FromArgb(opacity, picture.GetPixel(x, y)));
            }
        }
    }
}
