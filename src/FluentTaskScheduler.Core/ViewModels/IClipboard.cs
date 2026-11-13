namespace FluentTaskScheduler.ViewModels;

public interface IClipboard
{
    /// <summary>Puts <paramref name="text"/> on the clipboard, where it stays after the app has ended.</summary>
    /// <returns>False when nothing was copied because another program kept the clipboard busy.</returns>
    bool SetText(string text);
}
