using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentTaskScheduler.Helpers;
using FluentTaskScheduler.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler.Controls
{
    /// <summary>
    /// Dropping tasks or folders onto folders (system drag-and-drop), plus the small set of members
    /// the page's pointer-based fallback needs when the app runs elevated.
    /// </summary>
    public sealed partial class FolderTreeControl
    {
        /// <summary>Prefix of the text payload that carries dragged task paths (newline separated).</summary>
        public const string DragTaskPrefix = "FTS_TASKS:";
        /// <summary>Prefix of the text payload that carries a dragged folder path.</summary>
        public const string DragFolderPrefix = "FTS_FOLDER:";

        private Grid? _dragHighlightedGrid;

        // ── Helpers shared with the page's elevated-mode drag ────────────────────────

        /// <summary>The folder whose tree row contains <paramref name="element"/>, or null if it isn't part of this tree.</summary>
        public TaskFolderModel? FolderFromElement(DependencyObject element)
        {
            var tvi = VisualTreeUtil.FindParent<TreeViewItem>(element);
            if (tvi == null) return null;
            var node = FolderTreeView.NodeFromContainer(tvi);
            if (node == null) return null;
            return _nodeFolders.TryGetValue(node, out var f) ? f : null;
        }

        /// <summary>The folder row under a point given in host (window) coordinates, or null.</summary>
        public Grid? FolderGridAt(Windows.Foundation.Point hostPosition)
        {
            var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(hostPosition, FolderTreeView);
            foreach (var el in elements)
            {
                if (el is Grid g && FolderFromElement(g) != null)
                    return g;
            }
            return null;
        }

        /// <summary>Shows or hides the drop-target highlight on a folder row.</summary>
        public void SetHighlight(Grid? grid, bool on)
        {
            if (grid == null) return;
            grid.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 103, 192))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        // ── System drag-and-drop ─────────────────────────────────────────────────────

        private void FolderItem_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    ElevatedDragBlocked?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (sender is not FrameworkElement fe) return;
                var folder = FolderFromElement(fe);
                if (folder == null || folder.Path == RootPath) { e.Cancel = true; return; }
                e.Data.RequestedOperation = DataPackageOperation.Move;
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
            var folder = FolderFromElement(grid);
            if (folder == null) { e.AcceptedOperation = DataPackageOperation.None; return; }

            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move to \"{folder.Name}\"";
            e.DragUIOverride.IsGlyphVisible = true;

            if (_dragHighlightedGrid != grid)
            {
                SetHighlight(_dragHighlightedGrid, false);
                _dragHighlightedGrid = grid;
                SetHighlight(grid, true);
            }
        }

        private void FolderItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && grid == _dragHighlightedGrid)
            {
                SetHighlight(grid, false);
                _dragHighlightedGrid = null;
            }
        }

        private async void FolderItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FolderFromElement(grid);
            SetHighlight(grid, false);
            _dragHighlightedGrid = null;
            if (folder == null) return;
            if (!e.DataView.Contains(StandardDataFormats.Text)) return;

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
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private void FolderTreeView_DragLeave(object sender, DragEventArgs e)
        {
            SetHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        private void FolderTreeView_Drop(object sender, DragEventArgs e)
        {
            SetHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // ── Moves ────────────────────────────────────────────────────────────────────

        /// <summary>Moves tasks (newline-separated paths) into a folder, then refreshes the tree and asks the page to reload its list.</summary>
        public async Task MoveDraggedTasksAsync(string rawPaths, string targetFolderPath)
        {
            var paths = rawPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            foreach (var path in paths)
            {
                try { await Task.Run(() => Folders.MoveTask(path, targetFolderPath)); }
                catch (Exception ex) { errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
            }

            Reload();
            ContentMoved?.Invoke(this, EventArgs.Empty);

            if (errors.Count > 0)
                RaiseError("Some tasks could not be moved:\n\n" + string.Join("\n", errors));
        }

        /// <summary>Moves a folder under another folder, then refreshes the tree and asks the page to reload its list.</summary>
        public async Task MoveDraggedFolderAsync(string sourceFolderPath, string targetFolderPath)
        {
            try
            {
                // Ensure target folder is expanded so user sees the change
                _expandedState[targetFolderPath] = true;

                await Task.Run(() => Folders.MoveFolder(sourceFolderPath, targetFolderPath));
                Reload();
                ContentMoved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { RaiseError($"Could not move folder: {ex.Message}"); }
        }
    }
}
