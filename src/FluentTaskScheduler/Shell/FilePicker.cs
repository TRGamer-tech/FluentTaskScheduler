using FluentTaskScheduler.ViewModels;
using Windows.Storage.Pickers;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// File dialogs for one window. The pickers of the Windows Runtime refuse to open in an elevated
/// process, which is how this app often runs, so there the classic Win32 dialogs are used.
/// </summary>
public sealed class FilePicker(WindowContext window, IAppLifetime app) : IFilePicker
{
    public async Task<string?> PickOpenFileAsync(string title, string typeName, string extension)
    {
        string ext = extension.TrimStart('.');
        if (app.IsElevated)
        {
            string filter = ext == "*" ? "All files (*.*)|*.*" : $"{typeName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
            return ClassicFileDialog.PickOpenFile(window.Handle, title, filter);
        }

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);
        picker.FileTypeFilter.Add(ext == "*" ? "*" : "." + ext);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string title, string typeName, string extension, string suggestedFileName)
    {
        string ext = extension.TrimStart('.');
        if (app.IsElevated)
        {
            string filter = $"{typeName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
            return ClassicFileDialog.PickSaveFile(window.Handle, title, filter, ext, suggestedFileName);
        }

        var picker = new FileSavePicker { SuggestedFileName = suggestedFileName };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.Handle);
        picker.FileTypeChoices.Add(typeName, ["." + ext]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
