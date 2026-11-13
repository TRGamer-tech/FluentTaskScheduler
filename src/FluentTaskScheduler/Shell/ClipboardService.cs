using FluentTaskScheduler.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler.Shell;

public sealed class ClipboardService : IClipboard
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
