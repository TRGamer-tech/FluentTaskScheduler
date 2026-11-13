using FluentTaskScheduler.ViewModels;
using Windows.Storage.Pickers;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// File dialogs for one window. The WinRT pickers refuse to open in an elevated process, which is
/// how this app often runs, so in that case the classic Win32 dialogs are used instead.
/// </summary>
public sealed class FilePicker(WindowContext window) : IFilePicker
{
    private static readonly bool Elevated = Helpers.ElevationHelper.IsElevated();

    public async Task<string?> PickOpenFileAsync(string title, string typeName, string extension)
    {
        string ext = extension.TrimStart('.');
        if (Elevated)
        {
            string filter = ext == "*" ? "All files (*.*)|*.*" : $"{typeName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
            return Helpers.Win32FilePicker.PickOpenFile(window.Handle, title, filter);
        }

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);
        picker.FileTypeFilter.Add(ext == "*" ? "*" : "." + ext);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string title, string typeName, string extension, string suggestedFileName)
    {
        string ext = extension.TrimStart('.');
        if (Elevated)
        {
            string filter = $"{typeName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
            return Helpers.Win32FilePicker.PickSaveFile(window.Handle, title, filter, ext, suggestedFileName);
        }

        var picker = new FileSavePicker { SuggestedFileName = suggestedFileName };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);
        picker.FileTypeChoices.Add(typeName, [ "." + ext ]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
