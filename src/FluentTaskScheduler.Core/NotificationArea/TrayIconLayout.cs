using System.Drawing;

namespace FluentTaskScheduler.NotificationArea;

/// <summary>The pause sign: a disc with two upright bars.</summary>
internal sealed record PauseSign(Rectangle Disc, Rectangle LeftBar, Rectangle RightBar);

/// <summary>The badge with the number of running tasks, and the box its text is scaled into.</summary>
/// <param name="Area">With half discs for its ends: a disc where it is as wide as high.</param>
internal sealed record CountBadge(Rectangle Area, Rectangle Text);

/// <summary>
/// Where the marks of the notification area icon go on an icon of a given size: the pause sign
/// of a snooze in the top left corner, the number of running tasks in the bottom right one. They
/// are laid out for the smallest icon, 16 pixels, where a digit has to be 9 pixels high to be
/// read, and grow with the icon. Their edges are whole pixels, which keeps them sharp.
/// </summary>
internal static class TrayIconLayout
{
    // In pixels of the smallest icon.
    private const float PauseDiameter = 7, BadgeHeight = 11, Gap = 0.75f;

    public static PauseSign PauseSign(int size)
    {
        int diameter = (int)(PauseDiameter * size / 16);
        // Bars of 2/7 of the disc, 1/7 apart: 2, 1 and 2 pixels on the smallest icon.
        float middle = diameter / 2f, apart = diameter / 7f, width = 2 * apart, halfHeight = 0.32f * diameter;
        return new(
            new Rectangle(0, 0, diameter, diameter),
            Snap(middle - apart / 2 - width, middle - halfHeight, middle - apart / 2, middle + halfHeight),
            Snap(middle + apart / 2, middle - halfHeight, middle + apart / 2 + width, middle + halfHeight));
    }

    /// <param name="text">A digit, or "9+".</param>
    /// <param name="besidePauseSign">Whether the icon shows the pause sign too, which the badge keeps clear of.</param>
    public static CountBadge CountBadge(int size, string text, bool besidePauseSign)
    {
        float unit = size / 16f;
        int height = Round(BadgeHeight * unit);
        Rectangle area;
        if (text.Length == 1)
        {
            area = new Rectangle(size - height, size - height, height, height);
        }
        else if (!besidePauseSign)
        {
            area = new Rectangle(0, size - height, size, height);
        }
        else
        {
            // As high as the digits of a round badge, it reaches further to the left below the
            // pause sign before it comes too close to it.
            height -= 2 * Padding(height);
            int width = WidthBesideDisc(PauseSign(size).Disc.Width, size, height, Gap * unit);
            area = new Rectangle(size - width, size - height, width, height);
        }
        int padding = Padding(area.Height);
        return new(area, Rectangle.Inflate(area, -padding, -padding));
    }

    // Between the edge of a badge and its text: a pixel on the smallest icon.
    private static int Padding(int badgeHeight) => Round(badgeHeight / BadgeHeight);

    /// <summary>
    /// How wide a badge of <paramref name="height"/> in the bottom right corner can be before
    /// its round left end comes closer than <paramref name="gap"/> to a disc in the top left one.
    /// </summary>
    private static int WidthBesideDisc(int diameter, int size, int height, float gap)
    {
        // The centres of the disc and of the end have to be this far apart. The end's centre lies
        // this much lower than the disc's, and so at least this much further to the right.
        float discRadius = diameter / 2f, endRadius = height / 2f;
        float apart = discRadius + gap + endRadius;
        float lower = size - endRadius - discRadius;
        float right = MathF.Sqrt(MathF.Max(0, apart * apart - lower * lower));
        int left = (int)MathF.Ceiling(discRadius + right - endRadius);
        return Math.Clamp(size - left, height, size);
    }

    private static Rectangle Snap(float left, float top, float right, float bottom) =>
        Rectangle.FromLTRB(Round(left), Round(top), Round(right), Round(bottom));

    private static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
