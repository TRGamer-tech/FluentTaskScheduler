using System.Runtime.InteropServices;
using FluentTaskScheduler.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// The clipboard of Windows. Other programs open it for a moment now and then, a clipboard
/// manager or a remote desktop session reading what was copied for instance, and nobody else
/// can use it meanwhile. So each step is tried a few times before it counts as failed.
/// </summary>
public sealed class ClipboardService : IClipboard
{
    private const int Attempts = 5;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(50);

    public bool SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        for (int attempt = 1; !Clipboard.SetContentWithOptions(package, null); attempt++)
        {
            if (attempt == Attempts)
            {
                Serilog.Log.Warning("Nothing was copied: another program kept the clipboard open");
                return false;
            }
            Thread.Sleep(Pause);
        }
        Flush();
        return true;
    }

    // Until it is flushed, the clipboard holds the text on the app's behalf, and the text is gone
    // when the app ends. Programs that watch the clipboard read it as soon as it changes, so it is
    // often busy right now.
    private static void Flush()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.Flush();
                return;
            }
            catch (COMException) when (attempt < Attempts)
            {
                Thread.Sleep(Pause);
            }
            catch (COMException ex)
            {
                // The text can still be pasted for as long as the app runs.
                Serilog.Log.Warning(ex, "The clipboard could not be flushed; the copied text is gone once the app ends");
                return;
            }
        }
    }
}
