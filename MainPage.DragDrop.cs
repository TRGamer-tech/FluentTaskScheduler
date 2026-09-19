using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using FluentTaskScheduler.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler
{
    /// <summary>Drag and drop of tasks and folders, including the elevated-mode pointer fallback.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Drag and Drop
        // ========================================================================================================

        private const string DragTaskPrefix  = "FTS_TASKS:";
        private const string DragFolderPrefix = "FTS_FOLDER:";
        private Grid? _dragHighlightedGrid;

        // --- Task dragging from TaskListView ---

        private void TaskListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                var paths = e.Items
                    .OfType<ScheduledTaskModel>()
                    .Where(t => !t.IsReadOnlyFallback)
                    .Select(t => t.Path)
                    .ToList();

                if (paths.Count == 0) { e.Cancel = true; return; }

                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragTaskPrefix + string.Join("\n", paths));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Task DragItemsStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        // --- Per-folder-item DataTemplate Grid events ---

        private TaskFolderModel? FindFolderFromItemGrid(DependencyObject element)
        {
            var tvi = FindParent<TreeViewItem>(element);
            if (tvi == null) return null;
            var node = FolderTreeView.NodeFromContainer(tvi);
            if (node == null) return null;
            return _treeNodeFolderMap.TryGetValue(node, out var f) ? f : null;
        }

        private void SetDragHighlight(Grid? grid, bool on)
        {
            if (grid == null) return;
            grid.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 103, 192))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        private void FolderItem_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                if (sender is not FrameworkElement fe) return;
                var folder = FindFolderFromItemGrid(fe);
                if (folder == null || folder.Path == "\\") { e.Cancel = true; return; }
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragFolderPrefix + folder.Path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Folder DragStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        private void FolderItem_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            if (folder == null) { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; return; }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move to \"{folder.Name}\"";
            e.DragUIOverride.IsGlyphVisible = true;

            if (_dragHighlightedGrid != grid)
            {
                SetDragHighlight(_dragHighlightedGrid, false);
                _dragHighlightedGrid = grid;
                SetDragHighlight(grid, true);
            }
        }

        private void FolderItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && grid == _dragHighlightedGrid)
            {
                SetDragHighlight(grid, false);
                _dragHighlightedGrid = null;
            }
        }

        private async void FolderItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            SetDragHighlight(grid, false);
            _dragHighlightedGrid = null;
            if (folder == null) return;
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;

            string payload;
            try { payload = await e.DataView.GetTextAsync(); }
            catch { return; }

            if (payload.StartsWith(DragTaskPrefix))
                await MoveDraggedTasksAsync(payload.Substring(DragTaskPrefix.Length), folder.Path);
            else if (payload.StartsWith(DragFolderPrefix))
                await MoveDraggedFolderAsync(payload.Substring(DragFolderPrefix.Length), folder.Path);
        }

        // --- TreeView-level fallback handlers ---

        private void FolderTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }

        private void FolderTreeView_DragLeave(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Custom Elevated Drag-and-Drop Implementation ---
        private bool _isCustomDragging = false;
        private Windows.Foundation.Point _customDragStartPos;
        private object? _customDragItem; // string (folder path) or List<string> (task paths)
        private Grid? _customDragHoveredFolderGrid;

        private void OnCustomDragPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!Helpers.ElevationHelper.IsElevated()) return;
            var pt = e.GetCurrentPoint(this).Position;
            var element = e.OriginalSource as DependencyObject;
            if (element == null) return;

            // Check if dragging a Task
            var taskListViewItem = FindParent<ListViewItem>(element);
            if (taskListViewItem != null && FindParent<ListView>(taskListViewItem) == TaskListView)
            {
                // Ignore clicks on ToggleSwitch or CheckBox or Button
                if (element is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton || 
                    FindParent<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(element) != null ||
                    element is Button || FindParent<Button>(element) != null) return;

                var model = taskListViewItem.Content as ScheduledTaskModel;
                if (model == null || model.IsReadOnlyFallback) return;

                bool isSelected = false;
                foreach (ScheduledTaskModel sel in TaskListView.SelectedItems) {
                    if (sel == model) { isSelected = true; break; }
                }

                _customDragItem = isSelected && TaskListView.SelectedItems.Count > 0 
                    ? TaskListView.SelectedItems.OfType<ScheduledTaskModel>().Where(t => !t.IsReadOnlyFallback).Select(t => t.Path).ToList()
                    : new List<string> { model.Path };
                
                _customDragStartPos = pt;
                return;
            }

            // Check if dragging a Folder
            var treeViewItem = FindParent<TreeViewItem>(element);
            if (treeViewItem != null && FindParent<TreeView>(treeViewItem) == FolderTreeView)
            {
                // Ignore button clicks
                if (element is Button || FindParent<Button>(element) != null) return;

                var folder = FindFolderFromItemGrid(element);
                if (folder != null && folder.Path != "\\")
                {
                    _customDragItem = folder.Path;
                    _customDragStartPos = pt;
                }
            }
        }

        private void OnCustomDragPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;
            
            var pt = e.GetCurrentPoint(this).Position;
            if (!_isCustomDragging)
            {
                double dx = pt.X - _customDragStartPos.X;
                double dy = pt.Y - _customDragStartPos.Y;
                if (dx * dx + dy * dy > 25) // 5 pixel threshold
                {
                    _isCustomDragging = true;
                    this.CapturePointer(e.Pointer);
                    CustomDragCanvas.Visibility = Visibility.Visible;
                    
                    if (_customDragItem is List<string> tasks)
                    {
                        CustomDragIcon.Glyph = "\uE8F1"; // Task icon
                        CustomDragText.Text = tasks.Count > 1 ? $"Move {tasks.Count} tasks" : "Move task";
                    }
                    else if (_customDragItem is string folderPath)
                    {
                        CustomDragIcon.Glyph = "\uE8B7"; // Folder icon
                        var folderName = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? folderPath;
                        CustomDragText.Text = $"Move {folderName}";
                    }
                }
            }

            if (_isCustomDragging)
            {
                Canvas.SetLeft(CustomDragVisual, pt.X + 15);
                Canvas.SetTop(CustomDragVisual, pt.Y + 15);

                // Hit testing for drop target (FolderTreeView item)
                var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(e.GetCurrentPoint(null).Position, FolderTreeView);
                Grid? targetGrid = null;
                foreach (var el in elements)
                {
                    if (el is Grid g && FindFolderFromItemGrid(g) != null)
                    {
                        targetGrid = g;
                        break;
                    }
                }

                if (_customDragHoveredFolderGrid != targetGrid)
                {
                    SetDragHighlight(_customDragHoveredFolderGrid, false);
                    _customDragHoveredFolderGrid = targetGrid;
                    SetDragHighlight(_customDragHoveredFolderGrid, true);
                }
            }
        }

        private async void OnCustomDragPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;

            if (_isCustomDragging)
            {
                this.ReleasePointerCapture(e.Pointer);
                CustomDragCanvas.Visibility = Visibility.Collapsed;
                _isCustomDragging = false;
                SetDragHighlight(_customDragHoveredFolderGrid, false);

                if (_customDragHoveredFolderGrid != null)
                {
                    var targetFolder = FindFolderFromItemGrid(_customDragHoveredFolderGrid);
                    if (targetFolder != null)
                    {
                        if (_customDragItem is List<string> tasks)
                        {
                            await MoveDraggedTasksAsync(string.Join("\n", tasks), targetFolder.Path);
                        }
                        else if (_customDragItem is string folderPath)
                        {
                            await MoveDraggedFolderAsync(folderPath, targetFolder.Path);
                        }
                    }
                }
                _customDragHoveredFolderGrid = null;
            }
            _customDragItem = null;
        }

        private void FolderTreeView_Drop(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Move helpers ---

        private async System.Threading.Tasks.Task MoveDraggedTasksAsync(string rawPaths, string targetFolderPath)
        {
            var paths = rawPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            foreach (var path in paths)
            {
                try { await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveTask(path, targetFolderPath)); }
                catch (Exception ex) { errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
            }

            await ViewModel.LoadTasksAsync();
            LoadFolderStructure();

            if (errors.Count > 0)
                await ShowErrorDialog("Some tasks could not be moved:\n\n" + string.Join("\n", errors));
        }

        private async System.Threading.Tasks.Task MoveDraggedFolderAsync(string sourceFolderPath, string targetFolderPath)
        {
            try
            {
                // Ensure target folder is expanded so user sees the change
                _folderExpandedState[targetFolderPath] = true;

                await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveFolder(sourceFolderPath, targetFolderPath));
                LoadFolderStructure();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex) { await ShowErrorDialog($"Could not move folder: {ex.Message}"); }
        }

    }
}
