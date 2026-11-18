using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.NotificationArea;

/// <summary>
/// Draws the icon for the notification area: the app's icon, marked with the number of tasks
/// that are running and, while all tasks are snoozed, with a pause sign. It is drawn at the size
/// the notification area shows it at, pixel for pixel: scaled down by Windows, the marks would
/// blur beyond reading.
/// </summary>
public static class TrayIconImage
{
    private static readonly Color BadgeRed = Color.FromArgb(196, 43, 28);
    private static readonly Color PauseAmber = Color.FromArgb(202, 128, 0);
    private static readonly Color PauseBars = Color.FromArgb(32, 32, 32);

    /// <summary>
    /// The width and height in pixels of the icon on a display of <paramref name="dpi"/>. The
    /// notification area shows small icons, the size LoadIconMetric with LIM_SMALL loads for it:
    /// 16 pixels at 100 % (96 DPI), 20 at 125 %, 24 at 150 %, 32 at 200 %.
    /// </summary>
    public static int SizeAt(uint dpi) => GetSystemMetricsForDpi(SM_CXSMICON, dpi);

    /// <returns>An icon handle. Whoever asks for it has to destroy it.</returns>
    public static nint Create(string iconFile, int size, int runningTasks, bool snoozed)
    {
        using Bitmap image = Draw(iconFile, size, runningTasks, snoozed);
        return image.GetHicon();
    }

    internal static Bitmap Draw(string iconFile, int size, int runningTasks, bool snoozed)
    {
        // The image of the file nearest in size, scaled to the size asked for.
        using var appIcon = new Icon(iconFile, size, size);
        using Bitmap appImage = appIcon.ToBitmap();

        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(image);
        // Pixel n covers n to n + 1, so that shapes with whole-pixel edges fill whole pixels.
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var edges = new ImageAttributes())
        {
            // Otherwise the filter blends the outermost pixels with the emptiness beyond the
            // edges, and they come out faded.
            edges.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(appImage, new Rectangle(0, 0, size, size), 0, 0, appImage.Width, appImage.Height, GraphicsUnit.Pixel, edges);
        }

        if (snoozed) DrawPauseSign(graphics, TrayIconLayout.PauseSign(size));
        if (runningTasks > 0)
        {
            string count = runningTasks > 9 ? "9+" : runningTasks.ToString(CultureInfo.InvariantCulture);
            DrawCount(graphics, count, TrayIconLayout.CountBadge(size, count, besidePauseSign: snoozed));
        }
        return image;
    }

    private static void DrawPauseSign(Graphics graphics, PauseSign sign)
    {
        using var disc = new SolidBrush(PauseAmber);
        using var bars = new SolidBrush(PauseBars);
        graphics.FillEllipse(disc, sign.Disc);
        graphics.FillRectangle(bars, sign.LeftBar);
        graphics.FillRectangle(bars, sign.RightBar);
    }

    // White figures on a red badge, their ink scaled to fill the height or the width of the
    // badge's text box, whichever it reaches first.
    private static void DrawCount(Graphics graphics, string count, CountBadge badge)
    {
        using (var red = new SolidBrush(BadgeRed))
        using (GraphicsPath shape = RoundEnded(badge.Area))
        {
            graphics.FillPath(red, shape);
        }

        using var figures = new GraphicsPath();
        using (var font = new FontFamily("Segoe UI"))
        {
            figures.AddString(count, font, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
        }
        // Made of straight lines, the outline has the bounds of its ink, not of its curves' control points.
        figures.Flatten();
        RectangleF ink = figures.GetBounds();
        Rectangle box = badge.Text;
        float scale = Math.Min(box.Width / ink.Width, box.Height / ink.Height);
        using var fit = new Matrix();
        fit.Translate(box.X + (box.Width - ink.Width * scale) / 2, box.Y + (box.Height - ink.Height * scale) / 2);
        fit.Scale(scale, scale);
        fit.Translate(-ink.X, -ink.Y);
        figures.Transform(fit);
        graphics.FillPath(Brushes.White, figures);
    }

    // A rectangle with half discs for its left and right ends.
    private static GraphicsPath RoundEnded(Rectangle area)
    {
        var path = new GraphicsPath();
        path.AddArc(area.X, area.Y, area.Height, area.Height, 90, 180);
        path.AddArc(area.Right - area.Height, area.Y, area.Height, area.Height, 270, 180);
        path.CloseFigure();
        return path;
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
