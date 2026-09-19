using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Helpers
{
    /// <summary>Builds the simple "title + message + OK" ContentDialog that several pages used to
    /// construct independently with near-identical code (see 4.4).</summary>
    public static class DialogHelper
    {
        public static async Task<ContentDialogResult> ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = Services.LocalizationService.GetString("Dialog.Common.OK", "OK"),
                XamlRoot = xamlRoot,
                RequestedTheme = Services.SettingsService.Theme
            };
            return await dialog.ShowAsync();
        }

        public static Task<ContentDialogResult> ShowErrorAsync(XamlRoot xamlRoot, string message) =>
            ShowMessageAsync(xamlRoot, Services.LocalizationService.GetString("Dialog.Error.Title", "Error"), message);
    }
}
