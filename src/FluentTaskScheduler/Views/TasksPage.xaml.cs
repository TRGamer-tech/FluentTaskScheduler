using System.Globalization;
using FluentTaskScheduler.Dialogs;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Shell;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentTaskScheduler.Views;

/// <summary>The list of tasks.</summary>
public sealed partial class TasksPage : Page
{
    private readonly IDialogService _dialogs;
    private readonly FoldersViewModel _folders;
    private readonly TaskDetailsFactory _details;
    private bool _loaded;
    private bool _dialogOpen;
    private TaskRow? _menuRow;

    public TasksPage(TasksViewModel viewModel, FoldersViewModel folders, TaskDetailsFactory details, IDialogService dialogs)
    {
        ViewModel = viewModel;
        _folders = folders;
        _details = details;
        _dialogs = dialogs;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await ViewModel.ReloadAsync();
        };
    }

    public TasksViewModel ViewModel { get; }

    /// <summary>Opens the editor for a new task and returns the path of what was created, or null.</summary>
    public Task<string?> CreateTaskAsync(TaskDraft? template = null) => ShowEditorAsync(ViewModel.EditNew(template));

    /// <summary>Selects the task at <paramref name="path"/> and scrolls to it, loading the list first if it does not have the task yet.</summary>
    public async Task ShowAsync(string path)
    {
        ViewModel.FolderPath = null;
        ViewModel.SearchText = "";
        ViewModel.StatusFilterIndex = 0;
        if (ViewModel.Find(path) is null) await ViewModel.ReloadAsync();
        if (ViewModel.Find(path) is not { } row) return;

        TaskList.SelectedItem = row;
        TaskList.ScrollIntoView(row);
    }

    public static bool Not(bool value) => !value;
    public static double OpacityOf(bool isHidden) => isHidden ? 0.55 : 1;
    public static Visibility VisibleIfText(string text) => text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfSet(DateTime? time) => time is null ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfNone(int count, bool isLoading) => count == 0 && !isLoading ? Visibility.Visible : Visibility.Collapsed;

    public static string NextRunText(DateTime? time) =>
        time is { } next ? Loc.Format("Tasks.NextRunFormat", next.ToString("g", CultureInfo.CurrentCulture)) : "";

    private async Task<string?> ShowEditorAsync(TaskEditorViewModel editor)
    {
        // A second request (a shortcut pressed while a dialog is open) would only queue up behind it.
        if (_dialogOpen) return null;
        _dialogOpen = true;
        try
        {
            var dialog = new TaskEditorDialog(editor);
            if (await _dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        }
        finally
        {
            _dialogOpen = false;
        }
        if (editor.SavedPath is not { } path) return null;

        await ViewModel.ReloadAsync();
        if (ViewModel.Find(path) is { } row)
        {
            TaskList.SelectedItem = row;
            TaskList.ScrollIntoView(row);
        }
        return path;
    }

    private async Task EditAsync(TaskRow row)
    {
        if (await ViewModel.EditAsync(row) is { } editor) await ShowEditorAsync(editor);
    }

    /// <summary>Shows the task's details. What the user asks for in there is carried out once the dialog has closed.</summary>
    private async Task ShowDetailsAsync(TaskRow row)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        var dialog = new TaskDetailsDialog(_details.Create(row));
        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            _dialogOpen = false;
        }

        switch (dialog.Request)
        {
            case TaskDetailsRequest.Run: await ViewModel.RunCommand.ExecuteAsync(row); break;
            case TaskDetailsRequest.Stop: await ViewModel.StopCommand.ExecuteAsync(row); break;
            case TaskDetailsRequest.Edit: await EditAsync(row); break;
            case TaskDetailsRequest.Export: await ViewModel.ExportCommand.ExecuteAsync(row); break;
            case TaskDetailsRequest.Delete: await ViewModel.DeleteCommand.ExecuteAsync(row); break;
        }
    }

    private static TaskRow RowOf(object sender) => (TaskRow)((FrameworkElement)sender).DataContext;

    private void RowMenu_Opening(object? sender, object e)
    {
        _menuRow = (RowMenu.Target as FrameworkElement)?.DataContext as TaskRow;
        FillWithFolders(RowMoveMenu.Items, () => _menuRow);
    }

    private void BatchMoveMenu_Opening(object? sender, object e) => FillWithFolders(BatchMoveMenu.Items, () => null);

    // The folders change while the app runs, so the menu is filled each time it opens.
    private void FillWithFolders(IList<MenuFlyoutItemBase> items, Func<TaskRow?> row)
    {
        items.Clear();
        foreach (string folder in _folders.AllPaths)
        {
            var item = new MenuFlyoutItem { Text = folder };
            item.Click += async (_, _) => await ViewModel.MoveToFolderAsync(row(), folder);
            items.Add(item);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.BeginImportAsync() is not { } import) return;
        if (await _dialogs.ShowAsync(new ImportTaskDialog(import)) != ContentDialogResult.Primary || import.ImportedPath is not { } path) return;

        await ViewModel.ReloadAsync();
        await ShowAsync(path);
    }

    private async void NewTask_Click(object sender, RoutedEventArgs e) => await CreateTaskAsync();

    private async void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            await _dialogs.ShowAsync(new ShortcutsDialog());
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void TaskList_ItemClick(object sender, ItemClickEventArgs e)
    {
        // A click that extends or toggles the selection is not a request to open the task.
        if (ViewModel.HasSeveralSelected || IsDown(Windows.System.VirtualKey.Control) || IsDown(Windows.System.VirtualKey.Shift)) return;
        await ShowDetailsAsync((TaskRow)e.ClickedItem);
    }

    private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.Select(TaskList.SelectedItems.Cast<TaskRow>().ToList());

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => TaskList.SelectedItems.Clear();

    private void RowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        TaskRow row = RowOf(box);
        if (box.IsChecked == true) TaskList.SelectedItems.Add(row);
        else TaskList.SelectedItems.Remove(row);
    }

    private void CategoryBadge_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.SearchText = RowOf(sender).Category;
    }

    private async void RunRow_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) await ViewModel.RunCommand.ExecuteAsync(row);
    }

    private async void StopRow_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) await ViewModel.StopCommand.ExecuteAsync(row);
    }

    private async void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) await EditAsync(row);
    }

    private async void ExportRow_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) await ViewModel.ExportCommand.ExecuteAsync(row);
    }

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) await ViewModel.DeleteCommand.ExecuteAsync(row);
    }

    private async void EditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.SelectedTask is { } row) await EditAsync(row);
    }

    private async void RunAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.HasSelection) await ViewModel.RunCommand.ExecuteAsync(null);
    }

    private async void DeleteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.HasSelection) await ViewModel.DeleteCommand.ExecuteAsync(null);
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
