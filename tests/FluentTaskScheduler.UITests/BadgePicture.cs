using System.Drawing;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// The red badge with the number of running tasks, measured on a picture of the notification
/// area icon: the rectangle it takes up, and how much of that is purely its red or the white of
/// its figures.
/// <para>
/// The second tells an icon that was drawn for the size it is shown at from one that was drawn
/// smaller and stretched. Along every edge of the badge and of its figures lies a band of
/// blended pixels. Drawn, the band is one pixel wide at any size, so a larger badge has a larger
/// share of pure pixels than a smaller one. A picture that was stretched smoothly has a smaller
/// share than the one it was stretched from: the band widens with everything else, and the
/// smoothing blurs it further. Only a stretch that merely repeats pixels keeps the share.
/// </para>
/// </summary>
/// <param name="Bounds">The rectangle of the picture that the badge takes up; empty if the picture shows no badge.</param>
/// <param name="PureShare">The part of that rectangle, from 0 to 1, that is purely red or white.</param>
internal sealed record BadgePicture(Rectangle Bounds, double PureShare)
{
    /// <summary>The red the app fills the badge with, in TrayIconImage.</summary>
    private static readonly Color Red = Color.FromArgb(196, 43, 28);

    // How far from that red, on each channel, a pixel is still mostly the badge, as on its edge,
    // and how far it is still nothing but the badge. The white of the figures gets the same leeway.
    private const int MostlyRed = 60, PurelyRed = 12, White = 255 - PurelyRed;

    /// <summary>Measures the badge on the picture in <paramref name="file"/>.</summary>
    public static BadgePicture In(string file)
    {
        using var picture = new Bitmap(file);
        List<Point> pixels = (from y in Enumerable.Range(0, picture.Height)
                              from x in Enumerable.Range(0, picture.Width)
                              select new Point(x, y)).ToList();

        List<Point> badge = pixels.Where(point => IsRed(picture.GetPixel(point.X, point.Y), MostlyRed)).ToList();
        if (badge.Count == 0) return new BadgePicture(Rectangle.Empty, 0);

        Rectangle bounds = Rectangle.FromLTRB(badge.Min(point => point.X), badge.Min(point => point.Y), badge.Max(point => point.X) + 1, badge.Max(point => point.Y) + 1);
        int pure = pixels.Count(point => bounds.Contains(point) && IsPure(picture.GetPixel(point.X, point.Y)));
        return new BadgePicture(bounds, (double)pure / (bounds.Width * bounds.Height));
    }

    private static bool IsPure(Color pixel) => IsRed(pixel, PurelyRed) || Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) >= White;

    private static bool IsRed(Color pixel, int leeway) =>
        Math.Abs(pixel.R - Red.R) <= leeway && Math.Abs(pixel.G - Red.G) <= leeway && Math.Abs(pixel.B - Red.B) <= leeway;
}
