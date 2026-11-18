using System.Drawing;
using FluentTaskScheduler.NotificationArea;

namespace FluentTaskScheduler.Tests.NotificationArea;

/// <summary>Where the marks go on the notification area icon, at every size Windows shows it at.</summary>
public class TrayIconLayoutTests
{
    /// <summary>The sizes of a small icon at the display scalings Windows offers, 100 % to 400 %.</summary>
    private static readonly int[] SmallIconSizes = [16, 20, 24, 28, 32, 36, 40, 48, 56, 64];

    public static TheoryData<int> Sizes => new(SmallIconSizes);

    /// <summary>A badge for one digit and one for more than nine, with and without the pause sign, at every size.</summary>
    public static TheoryData<int, string, bool> Badges
    {
        get
        {
            var badges = new TheoryData<int, string, bool>();
            foreach (int size in SmallIconSizes)
            {
                foreach (string count in new[] { "7", "9+" })
                {
                    badges.Add(size, count, false);
                    badges.Add(size, count, true);
                }
            }
            return badges;
        }
    }

    [Fact]
    public void ADigit_IsNinePixelsHigh_OnTheSmallestIcon()
    {
        Assert.Equal(9, TrayIconLayout.CountBadge(16, "7", besidePauseSign: false).Text.Height);
    }

    [Theory, MemberData(nameof(Sizes))]
    public void ADigit_GrowsWithTheIcon(int size)
    {
        int height = TrayIconLayout.CountBadge(size, "7", besidePauseSign: false).Text.Height;

        Assert.InRange(height, 9f * size / 16 - 1, 9f * size / 16 + 1);
    }

    [Theory, MemberData(nameof(Badges))]
    public void TheCount_StaysInsideTheIcon_AndItsTextInsideItsBadge(int size, string count, bool besidePauseSign)
    {
        CountBadge badge = TrayIconLayout.CountBadge(size, count, besidePauseSign);

        Assert.True(new Rectangle(0, 0, size, size).Contains(badge.Area), $"The badge is at {badge.Area}.");
        Assert.True(badge.Area.Contains(badge.Text), $"The text is at {badge.Text}, its badge at {badge.Area}.");
    }

    [Theory, MemberData(nameof(Sizes))]
    public void NinePlus_GetsAWiderBadgeThanADigit_InTheSameCorner(int size)
    {
        var corner = new Point(size, size);
        foreach (bool besidePauseSign in new[] { false, true })
        {
            Rectangle digit = TrayIconLayout.CountBadge(size, "7", besidePauseSign).Area;
            Rectangle ninePlus = TrayIconLayout.CountBadge(size, "9+", besidePauseSign).Area;
            Assert.True(ninePlus.Width > digit.Width, $"9+ is at {ninePlus}, a digit at {digit}.");
            Assert.Equal(corner, new Point(digit.Right, digit.Bottom));
            Assert.Equal(corner, new Point(ninePlus.Right, ninePlus.Bottom));
        }
    }

    [Theory, MemberData(nameof(Sizes))]
    public void TheCountAndThePauseSign_KeepClearOfEachOther(int size)
    {
        Rectangle disc = TrayIconLayout.PauseSign(size).Disc;
        foreach (string count in new[] { "7", "9+" })
        {
            Rectangle badge = TrayIconLayout.CountBadge(size, count, besidePauseSign: true).Area;
            double gap = Gap(disc, badge);
            Assert.True(gap >= 0.75 * size / 16, $"The badge for {count} at {badge} comes within {gap:0.00} pixels of the pause sign at {disc}.");
        }
    }

    [Theory, MemberData(nameof(Sizes))]
    public void ThePauseSign_IsTwoEqualBarsInTheMiddleOfADisc_InTheTopLeftCorner(int size)
    {
        PauseSign sign = TrayIconLayout.PauseSign(size);

        Assert.Equal(Point.Empty, sign.Disc.Location);
        Assert.True(sign.Disc.Contains(sign.LeftBar) && sign.Disc.Contains(sign.RightBar), $"The bars at {sign.LeftBar} and {sign.RightBar} leave the disc at {sign.Disc}.");
        Assert.Equal(sign.LeftBar.Size, sign.RightBar.Size);
        Assert.True(sign.LeftBar.Width >= 2, $"The bars are {sign.LeftBar.Width} pixels wide.");
        Assert.True(sign.RightBar.Left > sign.LeftBar.Right, $"The bars at {sign.LeftBar} and {sign.RightBar} touch.");
        Assert.Equal(sign.LeftBar.Left - sign.Disc.Left, sign.Disc.Right - sign.RightBar.Right);
        Assert.Equal(sign.LeftBar.Top - sign.Disc.Top, sign.Disc.Bottom - sign.LeftBar.Bottom);
        Assert.Equal(sign.LeftBar.Top, sign.RightBar.Top);
    }

    /// <summary>
    /// How far apart the disc and the badge are where they come nearest. The badge is the line
    /// between the centres of its round ends, widened by their radius.
    /// </summary>
    private static double Gap(Rectangle disc, Rectangle badge)
    {
        double radius = badge.Height / 2.0;
        double discX = disc.X + disc.Width / 2.0, discY = disc.Y + disc.Height / 2.0;
        double nearestX = Math.Clamp(discX, badge.Left + radius, badge.Right - radius);
        return double.Hypot(discX - nearestX, discY - (badge.Y + radius)) - radius - disc.Width / 2.0;
    }
}
