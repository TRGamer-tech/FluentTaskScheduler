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
    /// <summary>Per-task snooze controls.</summary>
    public sealed partial class MainPage
    {
        // ── Per-task snooze ─────────────────────────────────────────────────────

        /// <summary>
        /// Syncs the detail dialog's snooze controls with the selected task: the status line and
        /// "Resume now" only make sense while that task is actually snoozed.
        /// </summary>
        private void UpdateTaskSnoozeUi()
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            string status = TaskSnoozeService.StatusTextFor(task.Path);
            bool snoozed = !string.IsNullOrEmpty(status);

            TaskSnoozeStatusText.Text = status;
            TaskSnoozeStatusText.Visibility = snoozed ? Visibility.Visible : Visibility.Collapsed;
            SnoozeTaskCancel.Visibility = snoozed ? Visibility.Visible : Visibility.Collapsed;

            // The picker is transient — never leave it open across tasks or reopens.
            TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
            TaskSnoozeCustomError.IsOpen = false;
        }

        private void SnoozeTaskDuration_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;
            if (sender is not MenuFlyoutItem item || !int.TryParse(item.Tag?.ToString(), out int minutes)) return;

            TaskSnoozeService.Snooze(task.Path, TimeSpan.FromMinutes(minutes));
            AfterTaskSnoozeChanged();
        }

        private void SnoozeTaskUntilReboot_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            TaskSnoozeService.SnoozeUntilReboot(task.Path);
            AfterTaskSnoozeChanged();
        }

        private void SnoozeTaskCancel_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            TaskSnoozeService.Cancel(task.Path);
            AfterTaskSnoozeChanged();
        }

        /// <summary>
        /// Reveals the inline custom-end-time picker. This cannot be a ContentDialog of its own:
        /// it is invoked from inside TaskDetailsDialog, and WinUI permits only one ContentDialog
        /// open at a time — the nested ShowAsync threw and the click appeared to do nothing.
        /// </summary>
        private void SnoozeTaskCustom_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            TaskSnoozeCustomError.IsOpen = false;
            TaskSnoozeCustomDate.Date = DateTimeOffset.Now;
            TaskSnoozeCustomTime.Time = DateTime.Now.AddHours(1).TimeOfDay;
            TaskSnoozeCustomPanel.Visibility = Visibility.Visible;
        }

        private void TaskSnoozeCustomCancel_Click(object sender, RoutedEventArgs e)
        {
            TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
            TaskSnoozeCustomError.IsOpen = false;
        }

        private void TaskSnoozeCustomApply_Click(object sender, RoutedEventArgs e)
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            try
            {
                var end = TaskSnoozeCustomDate.Date.Date + TaskSnoozeCustomTime.Time;
                if (end <= DateTime.Now)
                {
                    TaskSnoozeCustomError.Message = L("Snooze.Error.PastTime", "Pick a time in the future.");
                    TaskSnoozeCustomError.IsOpen = true;
                    return;
                }

                TaskSnoozeService.SnoozeUntilLocalTime(task.Path, end);
                TaskSnoozeCustomPanel.Visibility = Visibility.Collapsed;
                TaskSnoozeCustomError.IsOpen = false;
                AfterTaskSnoozeChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Failed to snooze task '{task.Path}' until a custom time.");
                TaskSnoozeCustomError.Message = ex.Message;
                TaskSnoozeCustomError.IsOpen = true;
            }
        }

        /// <summary>Refreshes the dialog and the task list after a task's snooze state changed.</summary>
        private void AfterTaskSnoozeChanged()
        {
            UpdateTaskSnoozeUi();
            _ = ViewModel.LoadTasksAsync();
        }

    }
}
