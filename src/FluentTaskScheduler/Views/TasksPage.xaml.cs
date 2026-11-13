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
    private bool _loaded;
    private bool _editing;
    private TaskRow? _menuRow;

    public TasksPage(TasksViewModel viewModel, IDialogService dialogs)
    {
        ViewModel = viewModel;
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
        // A second request (a shortcut pressed while the editor is open) would only queue up behind it.
        if (_editing) return null;
        _editing = true;
        try
        {
            var dialog = new TaskEditorDialog(editor);
            if (await _dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        }
        finally
        {
            _editing = false;
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

    private static TaskRow RowOf(object sender) => (TaskRow)((FrameworkElement)sender).DataContext;

    private void RowMenu_Opening(object? sender, object e) => _menuRow = (RowMenu.Target as FrameworkElement)?.DataContext as TaskRow;

    private async void NewTask_Click(object sender, RoutedEventArgs e) => await CreateTaskAsync();

    private async void TaskList_ItemClick(object sender, ItemClickEventArgs e)
    {
        // A click that extends or toggles the selection is not a request to open the task.
        if (ViewModel.HasSeveralSelected || IsDown(Windows.System.VirtualKey.Control) || IsDown(Windows.System.VirtualKey.Shift)) return;
        await EditAsync((TaskRow)e.ClickedItem);
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

    private async void EnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        // The switch also reports "toggled" when the binding sets it; only a difference to the row is the user's doing.
        var toggle = (ToggleSwitch)sender;
        if (toggle.DataContext is TaskRow row && toggle.IsOn != row.IsEnabled) await ViewModel.SetEnabledAsync(row, toggle.IsOn);
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
