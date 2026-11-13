using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Snoozing;

namespace FluentTaskScheduler.ViewModels;

/// <summary>One folder in the tree of task folders.</summary>
public sealed partial class FolderNode(string path, string name) : ObservableObject
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public bool IsRoot => Path == TaskPaths.Root;
    public ObservableCollection<FolderNode> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    public override string ToString() => Name;
}

/// <summary>The tree of task folders of one window, and what can be done with folders.</summary>
public sealed partial class FoldersViewModel(ITaskFolders folders, TasksViewModel tasks, IPrompts prompts, IAppLifetime app, ISnoozeService snooze) : ObservableObject
{
    /// <summary>The top of the tree: the one node that stands for the whole library.</summary>
    public ObservableCollection<FolderNode> Roots { get; } = [];

    /// <summary>The folder whose tasks the list shows; null while it shows the tasks of all folders.</summary>
    [ObservableProperty] private FolderNode? _selected;

    partial void OnSelectedChanged(FolderNode? value)
    {
        if (value is not null) tasks.FolderPath = value.Path;
    }

    /// <summary>The paths of all folders, parents first, for pickers.</summary>
    public IReadOnlyList<string> AllPaths { get; private set; } = [];

    /// <summary>Reads the folders and rebuilds the tree, keeping what was expanded and selected.</summary>
    public async Task ReloadAsync()
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = await Task.Run(folders.GetFolders);
        }
        catch (Exception ex)
        {
            // Whatever Task Scheduler answered: keep the tree as it is rather than show an empty one.
            Serilog.Log.Error(ex, "The task folders could not be loaded");
            return;
        }

        var expanded = Flatten(Roots).Where(node => node.IsExpanded).Select(node => node.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? selected = Selected?.Path;

        var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            var node = new FolderNode(path, path == TaskPaths.Root ? Loc.Get("Folders.Library") : TaskPaths.NameOf(path))
            {
                IsExpanded = path == TaskPaths.Root || expanded.Contains(path)
            };
            nodes[path] = node;
            if (path != TaskPaths.Root && nodes.TryGetValue(TaskPaths.ParentOf(path), out FolderNode? parent)) parent.Children.Add(node);
        }

        AllPaths = paths;
        Roots.Clear();
        if (nodes.TryGetValue(TaskPaths.Root, out FolderNode? root)) Roots.Add(root);
        Selected = selected is not null ? nodes.GetValueOrDefault(selected) : null;
    }

    /// <summary>Asks for a name and creates a folder in <paramref name="parent"/>.</summary>
    public async Task CreateAsync(FolderNode parent)
    {
        if (await prompts.AskForTextAsync(Loc.Get("Dialog.NewFolder.Title"), Loc.Get("Dialog.NewFolder.NamePlaceholder"), "", Loc.Get("Dialog.Common.Create")) is not { } name) return;
        if (!await IsValidAsync(name)) return;

        string path = TaskPaths.Combine(parent.Path, name);
        if (!await TryAsync(() => folders.Create(path))) return;

        parent.IsExpanded = true;
        await ReloadAsync();
    }

    /// <summary>Asks for a new name and renames <paramref name="folder"/>.</summary>
    public async Task RenameAsync(FolderNode folder)
    {
        if (folder.IsRoot) return;
        if (await prompts.AskForTextAsync(Loc.Get("Dialog.RenameFolder.Title"), Loc.Get("Dialog.NewFolder.NamePlaceholder"), folder.Name, Loc.Get("Dialog.Common.Rename")) is not { } name) return;
        if (name == folder.Name || !await IsValidAsync(name)) return;

        await MoveAsync(folder, TaskPaths.Combine(TaskPaths.ParentOf(folder.Path), name));
    }

    /// <summary>Deletes <paramref name="folder"/> and all tasks and folders in it, after asking.</summary>
    public async Task DeleteAsync(FolderNode folder)
    {
        if (folder.IsRoot) return;
        string question = Loc.Format("Dialog.DeleteFolder.ContentFormat", folder.Name);
        if (!await prompts.ConfirmAsync(Loc.Get("Dialog.DeleteFolder.Title"), question, Loc.Get("Dialog.Common.Delete"), isDestructive: true)) return;
        if (!await TryAsync(() => folders.Delete(folder.Path))) return;

        await AfterChangeAsync(folder.Path, TaskPaths.ParentOf(folder.Path));
    }

    private async Task MoveAsync(FolderNode folder, string newPath)
    {
        if (!await TryAsync(() => folders.MoveFolder(folder.Path, newPath))) return;
        // Snoozes know their tasks only by their paths, and those have changed with the folder's.
        await snooze.FolderMovedAsync(folder.Path, newPath);
        await AfterChangeAsync(folder.Path, newPath);
    }

    // The list may be showing the folder that just went away, or one inside it.
    private async Task AfterChangeAsync(string oldPath, string replacement)
    {
        if (tasks.FolderPath is { } shown && TaskPaths.IsWithin(shown, oldPath))
            tasks.FolderPath = replacement + shown[oldPath.Length..];
        if (Selected is { } selected && TaskPaths.IsWithin(selected.Path, oldPath)) Selected = null;

        await ReloadAsync();
        if (tasks.FolderPath is { } path) Selected = Flatten(Roots).FirstOrDefault(node => string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase));
        await tasks.ReloadAsync();
    }

    private async Task<bool> IsValidAsync(string name)
    {
        // A folder name follows the same rules as a task name.
        if (TaskDraftValidator.Validate(new TaskDraft { Name = name, Actions = [new ActionDraft { Path = "." }] }) is not [var problem, ..]) return true;
        await prompts.ShowErrorAsync(problem.Message);
        return false;
    }

    private async Task<bool> TryAsync(Action change)
    {
        try
        {
            await Task.Run(change);
            return true;
        }
        catch (TaskAccessDeniedException)
        {
            await prompts.ShowErrorAsync(Loc.Get(app.IsElevated ? "Editor.Error.AccessDenied" : "Editor.Error.AccessDeniedNotElevated"));
        }
        catch (Exception ex)
        {
            // Task Scheduler refuses with whatever COM error fits; the text is the best there is to show.
            Serilog.Log.Error(ex, "A folder operation failed");
            await prompts.ShowErrorAsync(ex.Message);
        }
        return false;
    }

    private static IEnumerable<FolderNode> Flatten(IEnumerable<FolderNode> nodes) =>
        nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}
