using System.Drawing;
using FluentTaskScheduler.NotificationArea;
using FluentTaskScheduler.Tests.Library;

namespace FluentTaskScheduler.Tests.NotificationArea;

/// <summary>The notification area icon as drawn, pixel by pixel, from the app's own icon. Windows only.</summary>
public class TrayIconImageTests
{
    private const string SkipReason = "draws with GDI+, which only Windows has";

    [SkippableTheory]
    [InlineData(96u, 16)]
    [InlineData(120u, 20)]
    [InlineData(144u, 24)]
    [InlineData(192u, 32)]
    public void TheIcon_HasTheSizeOfASmallIcon_AtEachDisplayScaling(uint dpi, int size)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "asks Windows");

        Assert.Equal(size, TrayIconImage.SizeAt(dpi));
    }

    [SkippableFact]
    public void ADigit_IsDrawnNinePixelsHigh_OnTheSmallestIcon()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        CountBadge badge = TrayIconLayout.CountBadge(16, "7", besidePauseSign: false);

        using Bitmap image = TrayIconImage.Draw(RepoFiles.AppIcon, 16, runningTasks: 7, snoozed: false);

        List<int> inkedRows = Enumerable.Range(badge.Area.Top, badge.Area.Height)
            .Where(y => Enumerable.Range(badge.Text.Left, badge.Text.Width).Any(x => IsWhite(image.GetPixel(x, y))))
            .ToList();
        Assert.NotEmpty(inkedRows);
        Assert.Equal((badge.Text.Top, badge.Text.Top + 8), (inkedRows.Min(), inkedRows.Max()));
    }

    [SkippableTheory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void TheAppsIcon_FillsAnImageOfTheSizeAskedFor(int size)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);

        using Bitmap image = TrayIconImage.Draw(RepoFiles.AppIcon, size, runningTasks: 0, snoozed: false);

        Assert.Equal(new Size(size, size), image.Size);
        Assert.All(new[] { new Point(size / 2, 0), new Point(0, size / 2), new Point(size / 2, size / 2), new Point(size - 1, size / 2), new Point(size / 2, size - 1) },
            point => Assert.Equal(255, image.GetPixel(point.X, point.Y).A));
    }

    [SkippableTheory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void TheMarks_ChangeNothingOutsideTheirPlaces(int size)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        Rectangle pauseSign = TrayIconLayout.PauseSign(size).Disc;
        Rectangle badge = TrayIconLayout.CountBadge(size, "9+", besidePauseSign: true).Area;

        using Bitmap plain = TrayIconImage.Draw(RepoFiles.AppIcon, size, runningTasks: 0, snoozed: false);
        using Bitmap marked = TrayIconImage.Draw(RepoFiles.AppIcon, size, runningTasks: 12, snoozed: true);

        List<Point> changed = (from x in Enumerable.Range(0, size)
                               from y in Enumerable.Range(0, size)
                               where plain.GetPixel(x, y).ToArgb() != marked.GetPixel(x, y).ToArgb()
                               select new Point(x, y)).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, point => Assert.True(pauseSign.Contains(point) || badge.Contains(point), $"The pixel at {point} changed."));
    }

    [SkippableTheory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void ThePauseSign_HasSharpBars(int size)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), SkipReason);
        PauseSign sign = TrayIconLayout.PauseSign(size);

        using Bitmap image = TrayIconImage.Draw(RepoFiles.AppIcon, size, runningTasks: 0, snoozed: true);

        // Every pixel of a bar has the bar's colour, and the pixels between the bars the disc's.
        Color bar = image.GetPixel(sign.LeftBar.X, sign.LeftBar.Y);
        Color disc = image.GetPixel(sign.LeftBar.Right, sign.Disc.Height / 2);
        Assert.All(Pixels(sign.LeftBar).Concat(Pixels(sign.RightBar)), point => Assert.Equal(bar.ToArgb(), image.GetPixel(point.X, point.Y).ToArgb()));
        Assert.All(Pixels(Rectangle.FromLTRB(sign.LeftBar.Right, sign.LeftBar.Top, sign.RightBar.Left, sign.LeftBar.Bottom)),
            point => Assert.Equal(disc.ToArgb(), image.GetPixel(point.X, point.Y).ToArgb()));
        Assert.True(bar.GetBrightness() < 0.2f && disc.GetBrightness() > 0.3f, $"The bars are {bar}, the disc {disc}.");
    }

    // Mostly covered by the white figures: the badge's red has hardly any green or blue, and the
    // app's icon is not this light where the badge lies.
    private static bool IsWhite(Color pixel) => Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 200;

    private static IEnumerable<Point> Pixels(Rectangle area) =>
        from x in Enumerable.Range(area.Left, area.Width)
        from y in Enumerable.Range(area.Top, area.Height)
        select new Point(x, y);
}
