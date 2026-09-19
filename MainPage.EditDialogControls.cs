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
    /// <summary>Create/edit task dialog: trigger, action and option control handlers.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // UI Logic (Dialogs)
        // ========================================================================================================


        private static string FormatScheduleInfo(DateTime value) => DurationUtil.FormatScheduleInfo(value);
        private static DateTime? TryParseScheduleInfo(string? value) => DurationUtil.TryParseScheduleInfo(value);

        private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionList.SelectedItem is TaskActionModel act)
            {
                ActionDetailsPanel.Visibility = Visibility.Visible;
                EditTaskActionCommand.Text = act.Command ?? "";
                EditTaskArguments.Text = act.Arguments ?? "";
                EditTaskWorkingDirectory.Text = act.WorkingDirectory ?? "";
            }
            else
            {
                ActionDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
        
        private void EditTaskActionCommand_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Command = EditTaskActionCommand.Text; }
        private void EditTaskArguments_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Arguments = EditTaskArguments.Text; }
        private void EditTaskWorkingDirectory_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.WorkingDirectory = EditTaskWorkingDirectory.Text; }

        private async void BrowseAction_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = await Helpers.FilePickerHelper.PickOpenFileAsync(
                App.m_window!, "Select File", "All Files", "*");

            if (!string.IsNullOrEmpty(filePath)) EditTaskActionCommand.Text = filePath;
        }

        private void PopulateNetworkList()
        {
            bool isAdmin = Helpers.ElevationHelper.IsElevated();

            if (!isAdmin)
            {
                // Non-admin: disable dropdown and show explanation notice
                EditTaskNetworkSelection.IsEnabled = false;
                EditTaskNetworkSelection.Items.Clear();
                EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });
                EditTaskNetworkSelection.SelectedIndex = 0;
                NetworkAdminNotice.IsOpen = true;
                return;
            }

            // Admin: populate from registry (exact NLM profile GUIDs)
            NetworkAdminNotice.IsOpen = false;
            EditTaskNetworkSelection.IsEnabled = true;
            EditTaskNetworkSelection.Items.Clear();
            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });

            try
            {
                using var profilesKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
                if (profilesKey != null)
                {
                    foreach (var subKeyName in profilesKey.GetSubKeyNames())
                    {
                        using var profileKey = profilesKey.OpenSubKey(subKeyName);
                        var name = profileKey?.GetValue("ProfileName") as string;
                        if (!string.IsNullOrWhiteSpace(name))
                            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = name, Tag = subKeyName });
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Warning("{Message}", $"Could not populate network list: {ex.Message}"); }
        }

        private void BtnAddAction_Click(object sender, RoutedEventArgs e) { _tempActions.Add(new TaskActionModel { Command="notepad.exe" }); ActionList.SelectedIndex = _tempActions.Count - 1; }
        
        private void AddAction_SendEmail_Click(object sender, RoutedEventArgs e) 
        {
            string ps = "powershell.exe";
            string args = "-ExecutionPolicy Bypass -Command \"Send-MailMessage -To 'recipient@example.com' -From 'scheduler@example.com' -Subject 'Task Started' -Body 'The task has started.' -SmtpServer 'smtp.example.com'\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void AddAction_ShowNotification_Click(object sender, RoutedEventArgs e)
        {
            string ps = "powershell.exe";
            string args = "-WindowStyle Hidden -Command \"& {Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Task Notification', 'FluentTaskScheduler')}\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel t) _tempActions.Remove(t); }
        private void BtnMoveActionUp_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx > 0) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx - 1, item);
                ActionList.SelectedIndex = idx - 1;
            }
        }
        private void BtnMoveActionDown_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx >= 0 && idx < _tempActions.Count - 1) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx + 1, item);
                ActionList.SelectedIndex = idx + 1;
            }
        }

        private void EditTaskRepetitionInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerEditor.SelectedTrigger is TaskTriggerModel tr && EditTaskRepetitionInterval.SelectedItem is ComboBoxItem item)
                tr.RepetitionInterval = item.Tag?.ToString() ?? "";
        }
        private void EditTaskRepetitionDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerEditor.SelectedTrigger is TaskTriggerModel tr && EditTaskRepetitionDuration.SelectedItem is ComboBoxItem item)
                tr.RepetitionDuration = item.Tag?.ToString() ?? "";
        }

        // Repetition lives outside the trigger editor, so the page loads it when a trigger is selected there.
        private void TriggerEditor_SelectedTriggerChanged(object? sender, TaskTriggerModel tr)
        {
            _isPopulatingDetails = true;
            foreach (var item in EditTaskRepetitionInterval.Items.Cast<ComboBoxItem>())
                if (item.Tag?.ToString() == (tr.RepetitionInterval ?? "")) { EditTaskRepetitionInterval.SelectedItem = item; break; }
            foreach (var item in EditTaskRepetitionDuration.Items.Cast<ComboBoxItem>())
                if (item.Tag?.ToString() == (tr.RepetitionDuration ?? "")) { EditTaskRepetitionDuration.SelectedItem = item; break; }
            _isPopulatingDetails = false;
        }
        private void UserContextRadio_Checked(object sender, RoutedEventArgs e) 
        { 
             if (EditTaskRunAsUser != null) EditTaskRunAsUser.IsEnabled = RunAsSpecificUser.IsChecked == true; 
             if (SystemUserWarning != null)
             {
                 bool isElevated = Helpers.ElevationHelper.IsElevated();
                 SystemUserWarning.IsOpen = (!isElevated) && (RunAsSystem.IsChecked == true);
             }
        }
        private void RunAsSystem_Click(object sender, RoutedEventArgs e) => RunAsSystem.IsChecked = true;
        private void DialogScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // When the user clicks on empty space (no interactive element), WinUI shifts focus to
            // the ScrollViewer and calls BringIntoView on it, which resets the scroll position to
            // the top. We prevent this by capturing the current vertical offset and restoring it
            // on the next dispatcher frame (after BringIntoView has already fired).
            if (sender is not ScrollViewer sv) return;
            double savedOffset = sv.VerticalOffset;
            DispatcherQueue.TryEnqueue(() => sv.ChangeView(null, savedOffset, null, true));
        }

        private void InfoIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true; // Don't bubble to ScrollViewer
            if (sender is FrameworkElement icon) Helpers.InfoFlyoutHelper.Show(icon, EditScrollViewer);
        }

        // Batch
        private void UpdateBatchActionsState()
        {
            ViewModel.UpdateBatchSelection(SelectedTasks());
        }
        private void BatchCancel_Click(object sender, RoutedEventArgs e) => TaskListView.SelectedItems.Clear();
        private async void BatchRun_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot selection before anything changes.
            // Set IsRunning=true BEFORE calling RunTask so the ring appears immediately,
            // independently of the volatile State string.
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

            if (SnoozeService.IsActive)
            {
                foreach (var t in tasks) SnoozeService.RecordSuppressedRun(t.Path, "Manual");
                await ShowErrorDialog(string.Format(
                    L("Snooze.Error.BatchBlocked", "{0} task(s) were not started because Global Snooze is active."),
                    tasks.Count));
                return;
            }

            foreach (var t in tasks)
            {
                t.State = TaskState.Running;
                t.IsRunning = true;           // show the ring immediately
                try
                {
                    ViewModel.TaskService.RunTask(t.Path);
                }
                catch (Exception ex)
                {
                    // The watcher below corrects IsRunning; log so a silent failure is traceable.
                    Serilog.Log.Error(ex, "{Message}", $"Batch run could not start task '{t.Path}'.");
                }
                WatchTaskUntilFinished(t);
            }
        }
        private void BatchStop_Click(object sender, RoutedEventArgs e) => ViewModel.BatchStop(SelectedTasks());
        private async void BatchEnable_Click(object sender, RoutedEventArgs e) { var denied = ViewModel.BatchSetEnabled(SelectedTasks(), true); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to enable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDisable_Click(object sender, RoutedEventArgs e) { var denied = ViewModel.BatchSetEnabled(SelectedTasks(), false); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to disable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDelete_Click(object sender, RoutedEventArgs e)
        {
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

            bool confirmed = !Settings.ConfirmDelete;
            if (!confirmed)
            {
                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"),
                    Content = string.Format(L("Dialog.BatchDelete.ContentFormat", "Delete {0} tasks?"), tasks.Count),
                    PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            }

            if (confirmed)
            {
                ViewModel.BatchDelete(tasks);
            }
        }
        private List<ScheduledTaskModel> SelectedTasks() => TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();

        // Keyboard Accelerators
        protected override void OnKeyDown(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F5) { e.Handled = true; _ = ViewModel.LoadTasksAsync(); return; }
            base.OnKeyDown(e);
        }
        private void NewTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; NewTaskButton_Click(sender, new RoutedEventArgs()); }
        private void EditTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) EditTask_Click(sender, new RoutedEventArgs()); }
        private void RunTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) RunTask_Click(sender, new RoutedEventArgs()); }
        private void DeleteTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (FocusManager.GetFocusedElement() is not TextBox) DeleteTask_Click(sender, new RoutedEventArgs()); }
        private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; try { TaskDetailsDialog.Hide(); } catch { } try { TaskEditDialog.Hide(); } catch { } }
        private void ShortcutsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; ShowShortcutsDialog(); }

        // Feature 1: Keyboard shortcuts dialog
        private void ShortcutsButton_Click(object sender, RoutedEventArgs e) => ShowShortcutsDialog();

        private async void ShowShortcutsDialog()
        {
            ShortcutsDialog.XamlRoot = this.XamlRoot;
            try { await ShortcutsDialog.ShowAsync(); } catch { }
        }

        // Feature 2: Sort button with flyout
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            void AddItem(string label, SortColumn col)
            {
                var item = new MenuFlyoutItem { Text = label + ViewModel.GetSortIndicator(col), Command = ViewModel.SortByCommand, CommandParameter = col };
                flyout.Items.Add(item);
            }

            AddItem(L("Main.Sort.Name", "Name"), SortColumn.Name);
            AddItem(L("Main.Sort.Status", "Status"), SortColumn.Status);
            AddItem(L("Main.Sort.NextRun", "Next Run"), SortColumn.NextRun);
            AddItem(L("Main.Sort.LastRun", "Last Run"), SortColumn.LastRun);
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = L("Main.Sort.Clear", "Clear Sort"), Command = ViewModel.ClearSortCommand };
            flyout.Items.Add(clear);

            flyout.ShowAt(SortButton);
        }

    }
}
