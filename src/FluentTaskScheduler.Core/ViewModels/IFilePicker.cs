namespace FluentTaskScheduler.ViewModels;

/// <summary>Lets the user choose a file. Both methods return null when the user cancels.</summary>
public interface IFilePicker
{
    /// <param name="typeName">How the file type is called in the picker, e.g. "Task XML".</param>
    /// <param name="extension">Extension without the dot, or "*" for any file.</param>
    Task<string?> PickOpenFileAsync(string title, string typeName, string extension);

    Task<string?> PickSaveFileAsync(string title, string typeName, string extension, string suggestedFileName);
}
