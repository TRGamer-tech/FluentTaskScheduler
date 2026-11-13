using FluentTaskScheduler.Localization;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        // WinUI refuses to open a second dialog in a window. The queue is the window's, so this
        // also waits for a dialog that was opened before the window's content was rebuilt.
        await window.DialogQueue.WaitAsync();
        try
        {
            dialog.XamlRoot = window.XamlRoot;
            dialog.RequestedTheme = window.Theme;
            // The controls' resources give ContentDialog its current look through an implicit style,
            // and implicit styles reach no derived class: the app's own dialogs would fall back to
            // the framework's old template.
            dialog.Style ??= (Style)Application.Current.Resources["DefaultContentDialogStyle"];
            return await dialog.ShowAsync();
        }
        finally
        {
            window.DialogQueue.Release();
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

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false, string? declineText = null)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = Text(message),
            PrimaryButtonText = confirmText,
            CloseButtonText = declineText ?? Loc.Get("Dialog.Common.Cancel"),
            // Enter must not carry out something that cannot be undone.
            DefaultButton = isDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<string?> AskForTextAsync(string title, string label, string initialText, string confirmText)
    {
        var input = new TextBox { Header = label, Text = initialText, MinWidth = 320 };
        AutomationProperties.SetAutomationId(input, "PromptInput");
        input.SelectAll();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = confirmText,
            CloseButtonText = Loc.Get("Dialog.Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary && input.Text.Trim() is { Length: > 0 } text ? text : null;
    }

    private static TextBlock Text(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };
}
