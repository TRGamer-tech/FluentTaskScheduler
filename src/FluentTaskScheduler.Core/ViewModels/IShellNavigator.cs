using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.ViewModels;

/// <summary>What a page can ask of the window it is shown in.</summary>
public interface IShellNavigator
{
    /// <summary>Switches to the task list, selects the task and opens its details.</summary>
    void ShowTask(string taskPath);

    /// <summary>Opens the task editor for a new task that starts out as <paramref name="template"/>.</summary>
    /// <returns>The path of the task that was created, or null when the user cancelled.</returns>
    Task<string?> CreateTaskAsync(TaskDraft template);

    /// <summary>Switches to the script editor, which shows whatever its view model holds.</summary>
    void ShowScriptEditor();
}
