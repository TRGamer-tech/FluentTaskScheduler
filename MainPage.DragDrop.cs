using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Controls;
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
    /// <summary>
    /// Dragging tasks out of the task list, and the pointer-based drag-and-drop used instead of the
    /// system one when the app runs elevated. Dropping onto folders is handled by <see cref="FolderTreeControl"/>.
    /// </summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Drag and Drop
        // ========================================================================================================

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
                e.Data.SetText(FolderTreeControl.DragTaskPrefix + string.Join("\n", paths));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Task DragItemsStarting failed: {ex.Message}");
                e.Cancel = true;
            }
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
            var folder = FolderTree.FolderFromElement(element);
            if (folder != null)
            {
                // Ignore button clicks
                if (element is Button || FindParent<Button>(element) != null) return;

                if (folder.Path != "\\")
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
                        CustomDragIcon.Glyph = ""; // Task icon
                        CustomDragText.Text = tasks.Count > 1 ? $"Move {tasks.Count} tasks" : "Move task";
                    }
                    else if (_customDragItem is string folderPath)
                    {
                        CustomDragIcon.Glyph = ""; // Folder icon
                        var folderName = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? folderPath;
                        CustomDragText.Text = $"Move {folderName}";
                    }
                }
            }

            if (_isCustomDragging)
            {
                Canvas.SetLeft(CustomDragVisual, pt.X + 15);
                Canvas.SetTop(CustomDragVisual, pt.Y + 15);

                // Hit testing for drop target (a folder row in the tree)
                Grid? targetGrid = FolderTree.FolderGridAt(e.GetCurrentPoint(null).Position);

                if (_customDragHoveredFolderGrid != targetGrid)
                {
                    FolderTree.SetHighlight(_customDragHoveredFolderGrid, false);
                    _customDragHoveredFolderGrid = targetGrid;
                    FolderTree.SetHighlight(_customDragHoveredFolderGrid, true);
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
                FolderTree.SetHighlight(_customDragHoveredFolderGrid, false);

                if (_customDragHoveredFolderGrid != null)
                {
                    var targetFolder = FolderTree.FolderFromElement(_customDragHoveredFolderGrid);
                    if (targetFolder != null)
                    {
                        if (_customDragItem is List<string> tasks)
                        {
                            await FolderTree.MoveDraggedTasksAsync(string.Join("\n", tasks), targetFolder.Path);
                        }
                        else if (_customDragItem is string folderPath)
                        {
                            await FolderTree.MoveDraggedFolderAsync(folderPath, targetFolder.Path);
                        }
                    }
                }
                _customDragHoveredFolderGrid = null;
            }
            _customDragItem = null;
        }
    }
}
