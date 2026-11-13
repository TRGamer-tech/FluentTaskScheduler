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
    /// <summary>Task list selection and filtering.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Task List & Selection
        // ========================================================================================================

        private async void TaskListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            
            if (TaskListView.SelectedItems.Count > 1 || ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) || shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;

            if (e.ClickedItem is ScheduledTaskModel task)
            {
               ViewModel.SelectedTask = task;
               await ShowTaskDetails();
            }
        }

        private void TaskListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (ScheduledTaskModel added in e.AddedItems) added.IsSelected = true;
            foreach (ScheduledTaskModel removed in e.RemovedItems) removed.IsSelected = false;

            int count = TaskListView.SelectedItems.Count;
            if (BatchActionBar != null)
            {
                BatchActionBar.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
                UpdateBatchCountText();
                UpdateBatchActionsState();
            }
            if (count == 1) ViewModel.SelectedTask = (ScheduledTaskModel)TaskListView.SelectedItem;
        }

        private void UpdateBatchCountText()
        {
            if (BatchCountText == null) return;
            BatchCountText.Text = string.Format(L("Main.Batch.SelectedCountFormat", "{0} selected"), TaskListView.SelectedItems.Count);
        }

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
             if (sender is CheckBox cb && cb.DataContext is ScheduledTaskModel task)
             {
                 var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                 bool isShiftHeld = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                 if (isShiftHeld && ViewModel.SelectedTask != null && ViewModel.SelectedTask != task)
                 {
                     var list = FilteredTasks;
                     int start = list.IndexOf(ViewModel.SelectedTask);
                     int end = list.IndexOf(task);

                     if (start > -1 && end > -1)
                     {
                         int min = Math.Min(start, end);
                         int max = Math.Max(start, end);
                         for (int i = min; i <= max; i++)
                         {
                             if (!TaskListView.SelectedItems.Contains(list[i])) TaskListView.SelectedItems.Add(list[i]);
                         }
                     }
                 }
                 else
                 {
                     if (cb.IsChecked == true) { TaskListView.SelectedItems.Add(task); ViewModel.SelectedTask = task; }
                     else TaskListView.SelectedItems.Remove(task);
                 }
             }
        }
        
        private void ToggleSwitch_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true; // Prevent row click

        // Set while a ListView container is being recycled/rebound to a different task, so the
        // resulting programmatic IsOn change (which still raises Toggled) isn't mistaken for a user
        // click. Replaces a FocusState-based heuristic that broke for touch/UIA input (3.11).
        private bool _isPopulatingToggle = false;

        private void TaskListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            _isPopulatingToggle = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isPopulatingToggle = false);
        }

        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsLoading || _isPopulatingToggle) return;
            if (sender is ToggleSwitch ts && ts.IsLoaded && ts.DataContext is ScheduledTaskModel task)
            {
                try
                {
                    if (task.IsEnabled != ts.IsOn) 
                        ViewModel.TaskService.SetTaskEnabled(task.Path, ts.IsOn);
                    task.IsEnabled = ts.IsOn;
                }
                catch (Exception ex) 
                { 
                    // Revert UI if failed
                    ts.Toggled -= ToggleSwitch_Toggled;
                    ts.IsOn = !ts.IsOn;
                    ts.Toggled += ToggleSwitch_Toggled;
                    await ShowErrorDialog(ex.Message);
                }
            }
        }

    }
}
