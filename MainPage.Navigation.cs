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

        // Folder tree events (see Controls/FolderTreeControl)

        private void FolderTree_FolderInvoked(object? sender, string folderPath)
        {
            _currentFolderPath = folderPath;
            Settings.LastFolderPath = folderPath;
            ViewModel.SetFilter(folderPath);

            // Restore Task View
            NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            TasksViewGrid.Visibility = Visibility.Visible;
            ContentFrame.Visibility = Visibility.Collapsed;

            NavView.SelectedItem = null; // Native indicator for Dashboard/ScriptLib disappears
        }

        private void FolderTree_FolderRenamed(object? sender, string oldPath)
        {
            if (_currentFolderPath.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFolderPath = "\\";
                ViewModel.SetFilter("all");
                NavView.SelectedItem = NavAllTasks;
            }
        }

        private void FolderTree_FolderDeleted(object? sender, string path)
        {
            ViewModel.SetFilter("all");
            NavView.SelectedItem = NavAllTasks;
        }

        private void FolderTree_ContentMoved(object? sender, EventArgs e) => _ = ViewModel.LoadTasksAsync();

        private async void FolderTree_ErrorRaised(object? sender, string message) => await ShowErrorDialog(message);

        private void FolderTree_ElevatedDragBlocked(object? sender, EventArgs e) => AdminDragWarning.Visibility = Visibility.Visible;

        private void WireFolderTree()
        {
            FolderTree.FolderInvoked += FolderTree_FolderInvoked;
            FolderTree.FolderRenamed += FolderTree_FolderRenamed;
            FolderTree.FolderDeleted += FolderTree_FolderDeleted;
            FolderTree.ContentMoved += FolderTree_ContentMoved;
            FolderTree.ErrorRaised += FolderTree_ErrorRaised;
            FolderTree.ElevatedDragBlocked += FolderTree_ElevatedDragBlocked;
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
                    FolderTree.ClearSelection();
                }
                else if (tag == "ScriptLibrary")
                {
                    NavView.Header = L("Main.Header.Library", "Library");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptLibraryPage), this);
                    FolderTree.ClearSelection();
                }
                else if (tag == "ScriptEditor")
                {
                    NavView.Header = L("Main.Header.ScriptEditor.Text", "Script Editor");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptEditorPage));
                    FolderTree.ClearSelection();
                }
                else if (tag == "QuickActions")
                {
                    NavView.Header = L("Main.Header.QuickActions", "Quick Actions");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(QuickActionsPage));
                    FolderTree.ClearSelection();
                }
                else
                {
                    // Standard Task Views (if any)
                    NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                    TasksViewGrid.Visibility = Visibility.Visible;
                    ContentFrame.Visibility = Visibility.Collapsed;
                    FolderTree.ClearSelection();

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
            FolderTree.ClearSelection();

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
