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
    /// <summary>Single and batch task operations (run, stop, enable, delete, ...).</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Task Operations (Single)
        // ========================================================================================================

        private void RunTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.RunTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = TaskState.Running;
                ViewModel.SelectedTask.IsRunning = true;
                WatchTaskUntilFinished(ViewModel.SelectedTask);
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask); // Refresh to show "Task Started"
            }
            catch (TaskSnoozedException ex)
            {
                // Snooze is a deliberate refusal, not an error the user needs a stack for.
                ViewModel.SelectedTask.State = ViewModel.SelectedTask.IsEnabled ? TaskState.Ready : TaskState.Disabled;
                ViewModel.SelectedTask.IsRunning = false;
                _ = ShowErrorDialog(ex.Message);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        private void StopTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.StopTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = TaskState.Ready;
                ViewModel.SelectedTask.IsRunning = false;
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        /// <summary>
        /// Polls Task Scheduler every 2 s until the task leaves the Running state,
        /// then writes the real state back to the model on the UI thread.
        /// </summary>
        // Shared by every WatchTaskUntilFinished caller: previously each started task got its own
        // 2-second poller that opened a brand-new TaskService and looked itself up individually, so
        // a batch run of N tasks spawned N parallel pollers each doing their own COM round-trip
        // every tick. One shared loop now polls every watched path in a single bulk call (3.5).
        private readonly Dictionary<string, ScheduledTaskModel> _watchedTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _watchStartTimesUtc = new(StringComparer.OrdinalIgnoreCase);
        private bool _taskWatcherRunning = false;
        private const int TaskWatcherPollIntervalMs = 2000;
        private static readonly TimeSpan MaxTaskWatchDuration = TimeSpan.FromMinutes(10);

        private void WatchTaskUntilFinished(ScheduledTaskModel task)
        {
            if (string.IsNullOrEmpty(task.Path)) return;

            bool needsStart;
            lock (_watchedTasks)
            {
                _watchedTasks[task.Path] = task;
                _watchStartTimesUtc[task.Path] = DateTime.UtcNow;
                needsStart = !_taskWatcherRunning;
                if (needsStart) _taskWatcherRunning = true;
            }

            if (needsStart) _ = RunTaskWatcherLoop();
        }

        private async System.Threading.Tasks.Task RunTaskWatcherLoop()
        {
            try
            {
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(TaskWatcherPollIntervalMs);

                    List<string> paths;
                    lock (_watchedTasks) { paths = _watchedTasks.Keys.ToList(); }
                    if (paths.Count == 0) break;

                    Dictionary<string, TaskState> statesByPath;
                    try
                    {
                        statesByPath = await System.Threading.Tasks.Task.Run(() =>
                            ViewModel.TaskService.GetAllTasks(recursive: true)
                                .Where(t => !string.IsNullOrEmpty(t.Path))
                                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().State, StringComparer.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        continue; // transient error — retry next tick rather than abandoning every watch
                    }

                    var now = DateTime.UtcNow;
                    var finishedPaths = new List<string>();
                    foreach (var path in paths)
                    {
                        if (!_watchedTasks.TryGetValue(path, out var task)) continue;

                        bool taskDeleted = !statesByPath.TryGetValue(path, out var liveState);
                        bool timedOut = now - _watchStartTimesUtc.GetValueOrDefault(path, now) > MaxTaskWatchDuration;
                        bool stillRunning = !taskDeleted && liveState == TaskState.Running;

                        if (!stillRunning || timedOut)
                        {
                            finishedPaths.Add(path);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (!taskDeleted) task.State = liveState;
                                task.IsRunning = false;
                                _ = RefreshTaskHistoryAsync(task); // Final refresh when finished
                            });
                        }
                        else
                        {
                            DispatcherQueue.TryEnqueue(() => task.State = liveState);
                        }
                    }

                    if (finishedPaths.Count > 0)
                    {
                        lock (_watchedTasks)
                        {
                            foreach (var p in finishedPaths) { _watchedTasks.Remove(p); _watchStartTimesUtc.Remove(p); }
                        }
                    }
                }
            }
            finally
            {
                lock (_watchedTasks) { _taskWatcherRunning = false; }
            }
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            // Hide the details dialog first to avoid "Only a single ContentDialog can be open" error
            try { TaskDetailsDialog.Hide(); } catch { }

            bool confirmed = !Settings.ConfirmDelete;
            if (!confirmed)
            {
                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"),
                    Content = string.Format(L("Dialog.DeleteTask.ContentFormat", "Are you sure you want to delete '{0}'?"), ViewModel.SelectedTask.Name),
                    PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            }

            if (confirmed)
            {
                try
                {
                    ViewModel.TaskService.DeleteTask(ViewModel.SelectedTask.Path);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void ExportTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;

            string? filePath = await Helpers.FilePickerHelper.PickSaveFileAsync(
                App.m_window!, "Export Task", "XML File", "xml", ViewModel.SelectedTask.Name);

            if (!string.IsNullOrEmpty(filePath))
            {
                try { ViewModel.TaskService.ExportTask(ViewModel.SelectedTask.Path, filePath); }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

        private async void ImportTask()
        {
            string? filePath = await Helpers.FilePickerHelper.PickOpenFileAsync(
                App.m_window!, "Import Task", "XML File", "xml");

            if (!string.IsNullOrEmpty(filePath))
            {
                var folderList = FolderTree.FolderPaths.Distinct().OrderBy(p => p).ToList();
                if (folderList.Count == 0) folderList.Add("\\");

                var comboBox = new ComboBox
                {
                    ItemsSource = folderList,
                    SelectedItem = _currentFolderPath ?? "\\",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = L("Dialog.ImportTask.SelectFolder", "Select the folder to import this task into:") });
                panel.Children.Add(comboBox);

                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ImportTask.Title", "Import Task"),
                    Content = panel,
                    PrimaryButtonText = L("Dialog.Common.Import", "Import"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                var folder = comboBox.SelectedItem?.ToString() ?? "\\";
                
                try
                {
                    string xml = System.IO.File.ReadAllText(filePath);
                    string taskName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    ViewModel.TaskService.RegisterTaskFromXml(folder, taskName, xml);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

    }
}
