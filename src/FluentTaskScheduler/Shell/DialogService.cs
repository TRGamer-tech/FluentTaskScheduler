using FluentTaskScheduler.Localization;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Shell;

/// <summary>Shows dialogs in one window.</summary>
public interface IDialogService : IPrompts
{
    /// <summary>
    /// Shows <paramref name="dialog"/> in this window. A window can only show one dialog at a
    /// time, so a request made while another dialog is open waits for that one to close. To go
    /// from one dialog to the next, close the first before asking for the second.
    /// </summary>
    Task<ContentDialogResult> ShowAsync(ContentDialog dialog);
}

public sealed class DialogService(WindowContext window) : IDialogService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            dialog.XamlRoot = window.XamlRoot;
            dialog.RequestedTheme = window.Theme;
            return await dialog.ShowAsync();
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public Task ShowMessageAsync(string title, string message) => ShowAsync(new ContentDialog
    {
        Title = title,
        Content = Text(message),
        CloseButtonText = Loc.Get("Dialog.Common.OK"),
        DefaultButton = ContentDialogButton.Close
    });

    public Task ShowErrorAsync(string message) => ShowMessageAsync(Loc.Get("Dialog.Error.Title"), message);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = Text(message),
            PrimaryButtonText = confirmText,
            CloseButtonText = Loc.Get("Dialog.Common.Cancel"),
            // Enter must not carry out something that cannot be undone.
            DefaultButton = isDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private static TextBlock Text(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };
}
