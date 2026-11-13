using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// Draws the icon for the notification area: the app's icon, marked with the number of tasks
/// that are running and, while tasks are snoozed, with a pause sign.
/// </summary>
internal static class TrayIconImage
{
    // Drawn at twice the size the notification area shows at 100 %, so it stays sharp when scaled.
    private const int Pixels = 32;

    /// <returns>An icon handle. Whoever asks for it has to destroy it.</returns>
    public static nint Create(string iconFile, int runningTasks, bool snoozed)
    {
        using var appIcon = new Icon(iconFile, Pixels, Pixels);
        using var bitmap = new Bitmap(Pixels, Pixels, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawIcon(appIcon, new Rectangle(0, 0, Pixels, Pixels));
            if (snoozed) DrawPauseSign(graphics);
            if (runningTasks > 0) DrawCount(graphics, runningTasks);
        }
        return bitmap.GetHicon();
    }

    // Top left: an amber disc with two white bars.
    private static void DrawPauseSign(Graphics graphics)
    {
        using var amber = new SolidBrush(Color.FromArgb(202, 128, 0));
        graphics.FillEllipse(amber, 0, 0, 15, 15);
        graphics.FillRectangle(Brushes.White, 4.5f, 3.5f, 2.2f, 8f);
        graphics.FillRectangle(Brushes.White, 8.3f, 3.5f, 2.2f, 8f);
    }

    // Bottom right: a red disc with the number, "9+" beyond nine.
    private static void DrawCount(Graphics graphics, int count)
    {
        const int disc = 14;
        const int corner = Pixels - disc;
        graphics.FillEllipse(Brushes.OrangeRed, corner, corner, disc, disc);

        string text = count > 9 ? "9+" : count.ToString();
        using var font = new Font("Segoe UI", count > 9 ? 6f : 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        SizeF size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, Brushes.White, corner + (disc - size.Width) / 2, corner + (disc - size.Height) / 2);
    }
}
