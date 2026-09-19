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
    /// <summary>Task details dialog and history.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Task Details & History
        // ========================================================================================================

        private async Task ShowTaskDetails()
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            DialogTaskName.Text = task.Name;
            DialogTaskDescription.Text = task.Description;
            DialogTaskAuthor.Text = task.Author;
            DialogTaskCategory.Text = task.Category;
            DialogTaskTagsItems.ItemsSource = task.Tags;
            DialogTaskTagsPanel.Visibility = task.Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DialogTaskCategory.Visibility = !string.IsNullOrEmpty(task.Category) ? Visibility.Visible : Visibility.Collapsed;
            
            // Load History
            _fullHistory = await Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            UpdateHistoryList();
            UpdateHistoryStats();
            
            UpdateTaskSnoozeUi();

            TaskDetailsDialog.XamlRoot = this.Content.XamlRoot;
            await TaskDetailsDialog.ShowAsync();
        }

        private void CategoryBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            string cat = "";
            if (sender is Border b && b.Child is TextBlock tb) cat = tb.Text;
            else if (sender is Grid g && g.Children.LastOrDefault() is TextBlock tbg) cat = tbg.Text;

            if (!string.IsNullOrEmpty(cat))
            {
                SearchBox.Text = cat;
                TaskDetailsDialog.Hide();
            }
        }

        private void TagBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Border b && b.Child is TextBlock tb)
            {
                SearchBox.Text = tb.Text;
                TaskDetailsDialog.Hide();
            }
        }

        private void UpdateHistoryList()
        {
            if (InlineHistoryListView == null) return;

            IEnumerable<TaskHistoryEntry> filtered = _fullHistory;

            // Date filtering (Combo)
            if (HistoryFilterCombo != null && HistoryFilterCombo.SelectedItem is ComboBoxItem dateItem)
            {
                string dateTag = dateItem.Tag?.ToString() ?? "All";
                if (dateTag != "All")
                {
                    filtered = filtered.Where(h =>
                    {
                        if (!DateTime.TryParse(h.Time, out var entryTime)) return true;
                        return dateTag switch
                        {
                            "Today" => entryTime.Date == DateTime.Today,
                            "Yesterday" => entryTime.Date == DateTime.Today.AddDays(-1),
                            "Week" => entryTime.Date >= DateTime.Today.AddDays(-7),
                            _ => true
                        };
                    });
                }
            }

            if (_historyStatusFilter == "Success") filtered = filtered.Where(TaskHistoryClassifier.IsSuccess);
            else if (_historyStatusFilter == "Failed") filtered = filtered.Where(TaskHistoryClassifier.IsFailure);

            InlineHistoryListView.ItemsSource = filtered.ToList();
        }

        private void UpdateHistoryStats()
        {
            StatTotalRuns.Text = _fullHistory.Count.ToString();
            StatSuccess.Text = _fullHistory.Count(TaskHistoryClassifier.IsSuccess).ToString();
            StatFailed.Text = _fullHistory.Count(TaskHistoryClassifier.IsFailure).ToString();
            StatLastResult.Text = _fullHistory.FirstOrDefault()?.Result ?? "-";
            HistoryStatsGrid.Visibility = Visibility.Visible;
        }

        private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) => UpdateHistoryList(); // Placeholder for actual date logic
        private async void ExportHistoryCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_fullHistory == null || _fullHistory.Count == 0) return;
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add("CSV File", new List<string>() { ".csv" });
            picker.SuggestedFileName = (ViewModel.SelectedTask?.Name ?? "history") + "_history";
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Time,EventId,Result,User,ExitCode,Message");
                    foreach (var h in _fullHistory)
                    {
                        sb.AppendLine($"\"{h.Time}\",{h.EventId},\"{h.Result}\",\"{h.User}\",{h.ExitCode},\"{h.Message?.Replace("\"", "\"\"") ?? ""}\"");
                    }
                    // UTF-8 *with* BOM so Excel and PowerShell don't mangle non-ASCII names.
                    System.IO.File.WriteAllText(file.Path, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void CopyHistory_Click(object sender, RoutedEventArgs e)
        {
             var dp = new DataPackage();
             dp.SetText(string.Join("\n", _fullHistory.Select(h => $"{h.Time}\t{h.Result}\t{h.Message}")));
             Clipboard.SetContent(dp);
             CopyHistoryBtn.Content = L("Main.History.Copied", "✅ Copied!");
             await Task.Delay(2000);
             CopyHistoryBtn.Content = L("Main.History.Copy", "📋 Copy");
         }
        
        private void StatTotal_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Total"; UpdateHistoryList(); }
        private void StatSuccess_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Success"; UpdateHistoryList(); }
        private void StatFailed_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Failed"; UpdateHistoryList(); }
        
        private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            RefreshHistoryBtn.IsEnabled = false;
            try { await RefreshTaskHistoryAsync(ViewModel.SelectedTask); }
            finally { RefreshHistoryBtn.IsEnabled = true; }
        }

        private async System.Threading.Tasks.Task RefreshTaskHistoryAsync(ScheduledTaskModel task)
        {
            if (task == null) return;
            var history = await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            
            // Only update if the user is still looking at the same task
            if (ViewModel.SelectedTask != null && ViewModel.SelectedTask.Path == task.Path)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _fullHistory = history;
                    UpdateHistoryList();
                    UpdateHistoryStats();
                });
            }
        }


    }
}
