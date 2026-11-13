namespace FluentTaskScheduler.ViewModels;

/// <summary>Messages and questions a view model puts to the user, in the window it belongs to.</summary>
public interface IPrompts
{
    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows <paramref name="message"/> under the standard error title.</summary>
    Task ShowErrorAsync(string message);

    /// <returns>True when the user chose <paramref name="confirmText"/>.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false);

    /// <summary>Asks for a short text, such as a name.</summary>
    /// <returns>What the user entered, trimmed; null when the user cancelled or entered nothing.</returns>
    Task<string?> AskForTextAsync(string title, string label, string initialText, string confirmText);
}
