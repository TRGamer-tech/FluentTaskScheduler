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
    /// <summary>Navigation, loading and folder tree.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Navigation & Loading
        // ========================================================================================================

        private void LoadFolderStructure()
        {
            try
            {
                var rootFolder = ViewModel.TaskService.GetFolderStructure();

                // Unregister the previous pass's property-changed callbacks before discarding those
                // nodes — RegisterPropertyChangedCallback tokens are otherwise never released, which
                // leaks a callback per folder on every reload (3.11).
                foreach (var kv in _treeNodeCallbackTokens)
                    kv.Key.UnregisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, kv.Value);
                _treeNodeCallbackTokens.Clear();

                _treeNodeFolderMap.Clear();
                FolderTreeView.RootNodes.Clear();
                AddFolderToTree(rootFolder, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
        }

        private Dictionary<TreeViewNode, TaskFolderModel> _treeNodeFolderMap = new();
        private Dictionary<TreeViewNode, long> _treeNodeCallbackTokens = new();

        /// <summary>Expands and selects the tree node for the given folder path, if it still exists
        /// (used to restore the last-used folder — see 2.2).</summary>
        private void SelectFolderTreeNodeForPath(string path)
        {
            var entry = _treeNodeFolderMap.FirstOrDefault(kv => string.Equals(kv.Value.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry.Key == null) return;

            for (var ancestor = entry.Key.Parent; ancestor != null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;

            FolderTreeView.SelectedNode = entry.Key;
        }

        private void AddFolderToTree(TaskFolderModel folder, TreeViewNode? parentNode)
        {
            var displayName = folder.Name == "\\" ? "Task Scheduler Library" : folder.Name;
            var treeNode = new TreeViewNode
            {
                Content = displayName,  
                IsExpanded = _folderExpandedState.ContainsKey(folder.Path) ? _folderExpandedState[folder.Path] : (folder.Path == "\\")
            };

            // Store folder in our mapping dictionary
            _treeNodeFolderMap[treeNode] = folder;

            // Track expansion state changes
            long token = treeNode.RegisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, (sender, dp) =>
            {
                if (sender is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var f))
                    _folderExpandedState[f.Path] = node.IsExpanded;
            });
            _treeNodeCallbackTokens[treeNode] = token;
            
            // Add to parent or root
            if (parentNode != null)
                parentNode.Children.Add(treeNode);
            else
                FolderTreeView.RootNodes.Add(treeNode);

            // Add subfolders
            foreach (var sub in folder.SubFolders)
                AddFolderToTree(sub, treeNode);
        }

        private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var folder))
            {
                _currentFolderPath = folder.Path;
                Settings.LastFolderPath = folder.Path;
                ViewModel.SetFilter(folder.Path);
                
                // Restore Task View
                NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                TasksViewGrid.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Collapsed;
                
                NavView.SelectedItem = null; // Native indicator for Dashboard/ScriptLib disappears
                FolderTreeView.SelectedItem = node; 
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected || (args.SelectedItem is NavigationViewItem settingsItem && settingsItem.Tag?.ToString() == "settings")) 
            {
                 NavView.Header = L("Main.Header.Settings", "Settings");
                 ContentFrame.Visibility = Visibility.Visible;
                 TasksViewGrid.Visibility = Visibility.Collapsed;
                 ContentFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                var tag = item.Tag.ToString() ?? "";

                if (tag == "Dashboard")
                {
                    NavView.Header = L("Main.Header.Dashboard", "Dashboard");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(DashboardPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptLibrary")
                {
                    NavView.Header = L("Main.Header.Library", "Library");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptLibraryPage), this);
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptEditor")
                {
                    NavView.Header = L("Main.Header.ScriptEditor.Text", "Script Editor");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptEditorPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "QuickActions")
                {
                    NavView.Header = L("Main.Header.QuickActions", "Quick Actions");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(QuickActionsPage));
                    FolderTreeView.SelectedItem = null;
                }
                else
                {
                    // Standard Task Views (if any)
                    NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                    TasksViewGrid.Visibility = Visibility.Visible;
                    ContentFrame.Visibility = Visibility.Collapsed;
                    FolderTreeView.SelectedItem = null;

                    if (tag.StartsWith("\\"))
                        _currentFolderPath = tag;
                    
                    ViewModel.SetFilter(tag);
                }
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "Add")
            {
                NewTaskButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadTasksAsync();
        private void ImportTask_Click(object sender, RoutedEventArgs e) => ImportTask(); // Implement if needed, kept generic

        public async void NavigateToTask(string taskPath)
        {
            // Switch to Tasks View
            NavView.SelectedItem = null; // Clear selection to indicate custom state or select "All Tasks"
            NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            TasksViewGrid.Visibility = Visibility.Visible;
            ContentFrame.Visibility = Visibility.Collapsed;
            FolderTreeView.SelectedItem = null;

            // Set filter to show this task (or all tasks)
            _currentFolderPath = System.IO.Path.GetDirectoryName(taskPath) ?? "\\";
            ViewModel.SetFilter("all"); // Reset filter to show everything in the folder, or just "all" global
            
            // Wait for load if needed
            if (ViewModel.FilteredTasks.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadTasksAsync();
            }

            // Find the task
            var task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            
            // If not found in current view, try to load specific folder? 
            // For now, let's assume it's in the list if we load all. 
            // Actually SetFilter("all") loads everything? No, SetFilter("all") is global filter.
            
            if (task == null)
            {
                // Try reloading
                await ViewModel.LoadTasksAsync();
                task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            }

            if (task != null)
            {
                ViewModel.SelectedTask = task;
                TaskListView.ScrollIntoView(task);
                await ShowTaskDetails();
            }
        }

    }
}
