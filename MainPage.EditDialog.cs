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
    /// <summary>Create/edit task dialog: open, populate, validate, save.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Task Editing (Dialog)
        // ========================================================================================================

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try { TaskDetailsDialog.Hide(); } catch { }

            if (ViewModel.SelectedTask.HasUnsupportedElements)
            {
                // Editing would silently drop or corrupt elements this app can't represent
                // (non-Exec actions, unrecognized trigger types) — refuse rather than risk it.
                await ShowErrorDialog(string.Format(
                    L("Dialog.UnsupportedElements",
                      "This task contains elements FluentTaskScheduler can't edit safely ({0}). Editing and saving it here would remove or corrupt those elements. Use Windows Task Scheduler to modify this task instead."),
                    ViewModel.SelectedTask.UnsupportedElementsDescription));
                return;
            }

            ViewModel.DialogMode = DialogMode.Edit;
            _isPopulatingDetails = true;
            _tempPipeline = ViewModel.SelectedTask.Pipeline?.Clone() ?? new TaskPipeline();
            UpdatePipelineSummaryLabel();

            // Populate Dialog
            EditTaskName.Text = ViewModel.SelectedTask.Name;
            EditTaskDescription.Text = ViewModel.SelectedTask.Description;
            EditTaskAuthor.Text = ViewModel.SelectedTask.Author;
            EditTaskCategory.Text = ViewModel.SelectedTask.Category;
            EditTaskTags.Text = ViewModel.SelectedTask.Tags != null ? string.Join(", ", ViewModel.SelectedTask.Tags) : "";
            EditTaskEnabled.IsOn = ViewModel.SelectedTask.IsEnabled;
            
            // Triggers
            TriggerEditor.EditedTriggers = new ObservableCollection<TaskTriggerModel>(ViewModel.SelectedTask.TriggersList);
            
            // Actions
            _tempActions = new ObservableCollection<TaskActionModel>(ViewModel.SelectedTask.Actions);
            ActionList.ItemsSource = _tempActions;
            
            // Settings - simplified map back?
            // This is hard to "Refactor Cleanly" without binding everything.
            // For now, retaining basic load logic manually.
            EditTaskOnlyIfIdle.IsChecked = ViewModel.SelectedTask.OnlyIfIdle;
            EditTaskIdleDurationSetting.Text = ViewModel.SelectedTask.IdleDuration;
            EditTaskStopOnIdleEnd.IsChecked = ViewModel.SelectedTask.StopOnIdleEnd;
            EditTaskOnlyIfAC.IsChecked = ViewModel.SelectedTask.OnlyIfAC;
            EditTaskStopBatterySwitch.IsChecked = ViewModel.SelectedTask.StopOnBattery;
            EditTaskOnBattery.IsChecked = ViewModel.SelectedTask.DisallowStartOnBatteries;
            EditTaskOnlyIfNetwork.IsChecked = ViewModel.SelectedTask.OnlyIfNetwork;
            EditTaskWakeToRun.IsChecked = ViewModel.SelectedTask.WakeToRun;
            EditTaskIsHidden.IsChecked = ViewModel.SelectedTask.IsHidden;
            EditTaskRunWithHighestPrivileges.IsChecked = ViewModel.SelectedTask.RunWithHighestPrivileges;
            
            if (ViewModel.SelectedTask.RunAsSystem)
            {
                RunAsSystem.IsChecked = true;
            }
            else if (!string.IsNullOrEmpty(ViewModel.SelectedTask.RunAsUser))
            {
                RunAsSpecificUser.IsChecked = true;
                EditTaskRunAsUser.Text = ViewModel.SelectedTask.RunAsUser;
            }
            else
            {
                RunAsCurrentUser.IsChecked = true;
            }
            EditTaskRunIfMissed.IsChecked = ViewModel.SelectedTask.RunIfMissed;
            foreach (var item in EditTaskMultipleInstances.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.MultipleInstancesPolicy.ToString()) { EditTaskMultipleInstances.SelectedItem = item; break; }
            foreach (var item in EditTaskPriority.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.TaskPriority.ToString()) { EditTaskPriority.SelectedItem = item; break; }
            EditTaskDeleteExpired.IsChecked = ViewModel.SelectedTask.DeleteExpiredTaskAfter;
            EditTaskAllowHardTerminate.IsChecked = ViewModel.SelectedTask.AllowHardTerminate;
            EditTaskRestartOnFailure.IsChecked = ViewModel.SelectedTask.RestartOnFailure;
            EditTaskRestartInterval.Text = ViewModel.SelectedTask.RestartInterval;
            if (EditTaskRestartCount != null) EditTaskRestartCount.Value = ViewModel.SelectedTask.RestartCount;

            // Stop task if runs longer than
            TriggerEditor.SetStopAfter(ViewModel.SelectedTask.StopIfRunsLongerThan);
            // All settings mapped
            
            PopulateNetworkList();
            if (!string.IsNullOrEmpty(ViewModel.SelectedTask.NetworkId) &&
                Guid.TryParse(ViewModel.SelectedTask.NetworkId, out var taskNetworkGuid))
            {
                foreach (Microsoft.UI.Xaml.Controls.ComboBoxItem item in EditTaskNetworkSelection.Items)
                {
                    if (Guid.TryParse(item.Tag?.ToString(), out var itemGuid) && itemGuid == taskNetworkGuid)
                    {
                        EditTaskNetworkSelection.SelectedItem = item;
                        break;
                    }
                }
            }
            else
                EditTaskNetworkSelection.SelectedIndex = 0;
            
            _isPopulatingDetails = false;
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            EditTaskErrorBar.IsOpen = false;
            await TaskEditDialog.ShowAsync();
        }



        private async void TaskEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true; // Handle async manually

            if (string.IsNullOrWhiteSpace(EditTaskName.Text))
            {
                EditTaskErrorBar.Message = L("Dialog.Error.EmptyName", "Task name cannot be empty.");
                EditTaskErrorBar.IsOpen = true;
                return;
            }

            // "Stop task if runs longer than" is an editable combo box: a typed custom value has no
            // SelectedItem, so it must be read from .Text — falling back to the preset's Tag only
            // silently replaced any custom duration with 72h (see 1.4).
            string stopAfterValue = "";
            if (TriggerEditor.IsStopAfterEnabled)
            {
                stopAfterValue = TriggerEditor.StopAfterValue;
                if (string.IsNullOrWhiteSpace(stopAfterValue) || !TryParseIsoDuration(stopAfterValue, out _))
                {
                    EditTaskErrorBar.Message = string.Format(
                        L("Dialog.Error.InvalidStopAfter", "\"{0}\" is not a valid duration for \"Stop task if runs longer than\". Use an ISO-8601 duration such as PT90M or P1D."),
                        stopAfterValue);
                    EditTaskErrorBar.IsOpen = true;
                    return;
                }
            }

            // Random delay is validated per-trigger here rather than silently discarded on a parse
            // failure (see 1.3).
            foreach (var trig in TriggerEditor.EditedTriggers)
            {
                if (!string.IsNullOrWhiteSpace(trig.RandomDelay) && !TryParseIsoDuration(trig.RandomDelay, out _))
                {
                    EditTaskErrorBar.Message = string.Format(
                        L("Dialog.Error.InvalidRandomDelay", "\"{0}\" is not a valid random delay value. Use an ISO-8601 duration such as PT30M or PT1H."),
                        trig.RandomDelay);
                    EditTaskErrorBar.IsOpen = true;
                    return;
                }
            }

            // IdleDuration/RestartInterval placeholders advertise a friendly shorthand ("10m", "1h")
            // but were previously validated with the strict ISO-8601-only parser and silently
            // discarded on failure (see 2.3).
            if (EditTaskOnlyIfIdle.IsChecked == true &&
                !DurationUtil.TryParseFlexibleDuration(EditTaskIdleDurationSetting.Text, out _))
            {
                EditTaskErrorBar.Message = string.Format(
                    L("Dialog.Error.InvalidIdleDuration", "\"{0}\" is not a valid idle duration. Use a value like 10m, 1h, or an ISO-8601 duration such as PT10M."),
                    EditTaskIdleDurationSetting.Text);
                EditTaskErrorBar.IsOpen = true;
                return;
            }
            if (EditTaskRestartOnFailure.IsChecked == true &&
                !string.IsNullOrWhiteSpace(EditTaskRestartInterval.Text) &&
                !DurationUtil.TryParseFlexibleDuration(EditTaskRestartInterval.Text, out _))
            {
                EditTaskErrorBar.Message = string.Format(
                    L("Dialog.Error.InvalidRestartInterval", "\"{0}\" is not a valid restart interval. Use a value like 1m, 30s, or an ISO-8601 duration such as PT1M."),
                    EditTaskRestartInterval.Text);
                EditTaskErrorBar.IsOpen = true;
                return;
            }

            var model = new ScheduledTaskModel
            {
                Name = EditTaskName.Text,
                Description = EditTaskDescription.Text,
                Author = EditTaskAuthor.Text,
                IsEnabled = EditTaskEnabled.IsOn,
                Category = EditTaskCategory.Text,
                Tags = new ObservableCollection<string>(EditTaskTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                Actions = new ObservableCollection<TaskActionModel>(_tempActions),
                TriggersList = new ObservableCollection<TaskTriggerModel>(TriggerEditor.EditedTriggers),
                // Map Settings
                OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked == true,
                IdleDuration = EditTaskIdleDurationSetting.Text ?? "",
                StopOnIdleEnd = EditTaskStopOnIdleEnd.IsChecked == true,
                OnlyIfAC = EditTaskOnlyIfAC.IsChecked == true,
                StopOnBattery = EditTaskStopBatterySwitch.IsChecked == true,
                DisallowStartOnBatteries = EditTaskOnBattery.IsChecked == true,
                OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked == true,
                NetworkId = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString() ?? "",
                NetworkName = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content?.ToString() ?? "",
                WakeToRun = EditTaskWakeToRun.IsChecked == true,
                IsHidden = EditTaskIsHidden.IsChecked == true,
                RunWithHighestPrivileges = EditTaskRunWithHighestPrivileges.IsChecked == true,
                RunAsSystem = RunAsSystem.IsChecked == true,
                RunAsUser = RunAsSpecificUser.IsChecked == true ? EditTaskRunAsUser.Text : "",
                RunIfMissed = EditTaskRunIfMissed.IsChecked == true,
                MultipleInstancesPolicy = Enum.TryParse<Microsoft.Win32.TaskScheduler.TaskInstancesPolicy>(
                    (EditTaskMultipleInstances.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString(), out var mip)
                    ? mip : Microsoft.Win32.TaskScheduler.TaskInstancesPolicy.IgnoreNew,
                TaskPriority = int.TryParse((EditTaskPriority.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString(), out int p) ? p : 7,
                DeleteExpiredTaskAfter = EditTaskDeleteExpired.IsChecked == true,
                AllowHardTerminate = EditTaskAllowHardTerminate.IsChecked == true,
                RestartOnFailure = EditTaskRestartOnFailure.IsChecked == true,
                RestartInterval = EditTaskRestartInterval.Text ?? "",
                RestartCount = EditTaskRestartCount != null ? (int)double.Round(EditTaskRestartCount.Value) : 3,
                StopIfRunsLongerThan = stopAfterValue
            };

            model.Pipeline = _tempPipeline?.Clone() ?? new TaskPipeline();
            
            // Handle folder
            string folder = "\\";
            if (ViewModel.DialogMode == DialogMode.FromTemplate)
            {
                folder = "\\";
            }
            else if (ViewModel.DialogMode == DialogMode.Create) // New Task
            {
                folder = _currentFolderPath;  // Use tracked folder path
            }
            else // Edit - keep original folder logic (extracted from Path)
            {
                if (ViewModel.SelectedTask != null)
                    folder = System.IO.Path.GetDirectoryName(ViewModel.SelectedTask.Path) ?? "\\";
            }

            try
            {
                ViewModel.TaskService.RegisterTask(folder ?? "\\", model);

                // Handle renaming: if name changed (case-sensitive check for the file system/TS behavior)
                // but Task Scheduler is case-insensitive, so we only delete if it's truly a different task
                if (ViewModel.DialogMode == DialogMode.Edit && ViewModel.SelectedTask != null &&
                    !model.Name.Equals(ViewModel.SelectedTask.Name, StringComparison.OrdinalIgnoreCase))
                {
                    string oldPath = ViewModel.SelectedTask.Path;
                    string newPath = (folder ?? "\\").TrimEnd('\\') + "\\" + model.Name;
                    try
                    {
                        ViewModel.TaskService.DeleteTask(oldPath);
                        Serilog.Log.Information("{Message}", $"Renamed task - deleted old task at '{oldPath}'");
                    }
                    catch (Exception deleteEx)
                    {
                        // The new copy already exists at this point (RegisterTask above succeeded),
                        // so a failed delete of the old one would leave a duplicate. Undo the new
                        // copy so the rename fails cleanly instead of silently duplicating the task.
                        Serilog.Log.Error(deleteEx, "{Message}", $"Rename failed: could not delete old task '{oldPath}' after registering '{newPath}'. Rolling back the new copy.");
                        try { ViewModel.TaskService.DeleteTask(newPath); }
                        catch (Exception rollbackEx)
                        {
                            Serilog.Log.Error(rollbackEx, "{Message}", $"Rollback also failed: could not delete the new copy '{newPath}'. Both '{oldPath}' and '{newPath}' may now exist.");
                            EditTaskErrorBar.Message = string.Format(
                                L("Dialog.Error.RenameDuplicated", "Rename failed: both \"{0}\" and \"{1}\" now exist. Please delete one manually in Task Scheduler."),
                                System.IO.Path.GetFileName(oldPath), System.IO.Path.GetFileName(newPath));
                            EditTaskErrorBar.IsOpen = true;
                            return;
                        }
                        EditTaskErrorBar.Message = string.Format(
                            L("Dialog.Error.RenameFailed", "Could not rename \"{0}\" to \"{1}\": the old task could not be deleted ({2})."),
                            System.IO.Path.GetFileName(oldPath), model.Name, deleteEx.Message);
                        EditTaskErrorBar.IsOpen = true;
                        return;
                    }
                }

                // The pipeline watcher caches configuration; force it to re-read after a save.
                TaskPipelineService.InvalidatePipelineCache();

                TaskEditDialog.Hide();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex)
            {
                EditTaskErrorBar.Message = L("Dialog.Error.SaveTaskFailedPrefix", "Failed to save task: ") + ex.Message;
                EditTaskErrorBar.IsOpen = true;
            }
        }

    }
}
