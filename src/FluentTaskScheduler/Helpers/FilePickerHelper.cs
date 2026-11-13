using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.Helpers
{
    /// <summary>
    /// Picks a save/open file path, choosing the Win32 picker when running elevated (the WinRT
    /// FileSavePicker/FileOpenPicker throw when the process is Administrator) and the WinRT picker
    /// otherwise. This branch used to be copy-pasted at every call site (see 4.4); two of those
    /// copies (Settings export/import) were missing the elevated branch entirely, so exporting or
    /// importing settings while running as Administrator silently failed.
    /// </summary>
    public static class FilePickerHelper
    {
        public static async Task<string?> PickSaveFileAsync(Window window, string title, string friendlyName, string extension, string suggestedFileName)
        {
            string ext = extension.TrimStart('.');
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            if (ElevationHelper.IsElevated())
            {
                string filter = $"{friendlyName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
                return Win32FilePicker.PickSaveFile(hwnd, title, filter, ext, suggestedFileName);
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add(friendlyName, new System.Collections.Generic.List<string> { "." + ext });
            picker.SuggestedFileName = suggestedFileName;
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }

        /// <param name="extension">A file extension without the dot, or "*" to accept any file.</param>
        public static async Task<string?> PickOpenFileAsync(Window window, string title, string friendlyName, string extension)
        {
            string ext = extension.TrimStart('.');
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            if (ElevationHelper.IsElevated())
            {
                string filter = ext == "*" ? "All files (*.*)|*.*" : $"{friendlyName} (*.{ext})|*.{ext}|All files (*.*)|*.*";
                return Win32FilePicker.PickOpenFile(hwnd, title, filter);
            }

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(ext == "*" ? "*" : "." + ext);
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
    }
}
