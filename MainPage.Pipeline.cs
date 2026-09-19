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
    /// <summary>Task completion actions / pipeline editing.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Completion actions / pipelines (v1.9)
        // ========================================================================================================

        private void UpdatePipelineSummaryLabel()
        {
            if (DlgPipelineSummary == null) return;

            if (_tempPipeline == null || !_tempPipeline.HasAnyTargets)
            {
                DlgPipelineSummary.Text = L("Pipeline.Summary.None", "No downstream tasks configured.");
                return;
            }

            string state = _tempPipeline.IsEnabled
                ? L("Pipeline.Summary.Enabled", "Enabled")
                : L("Pipeline.Summary.Disabled", "Disabled");

            DlgPipelineSummary.Text = string.Format(
                L("Pipeline.Summary.Format", "{0} — {1} on success, {2} on failure"),
                state, _tempPipeline.OnSuccessTasks.Count, _tempPipeline.OnFailureTasks.Count);
        }

        private async void ConfigurePipeline_Click(object sender, RoutedEventArgs e)
        {
            if (this.Content?.XamlRoot == null) return;

            try
            {
                string ownPath = ViewModel.DialogMode == DialogMode.Edit && ViewModel.SelectedTask != null ? ViewModel.SelectedTask.Path : "";

                var available = await System.Threading.Tasks.Task.Run(
                    () => ViewModel.TaskService.GetAllTasks(recursive: true));

                var dialog = new Dialogs.TaskPipelineDialog(ownPath, _tempPipeline, available)
                {
                    XamlRoot = this.Content.XamlRoot
                };

                // WinUI allows only one dialog per XamlRoot, so the editor must step aside.
                TaskEditDialog.Hide();
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _tempPipeline = dialog.Result;
                    UpdatePipelineSummaryLabel();
                }

                await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Failed to open the completion actions dialog.");
                await ShowErrorDialog(L("Pipeline.Error.Open", "Could not open completion actions: ") + ex.Message);
            }
        }

        /// <summary>Opens the task editor pre-filled from a built-in task template.</summary>
        public async void OpenCreateTaskFromTaskTemplate(TaskTemplate template)
        {
            if (this.Content?.XamlRoot == null || template == null) return;

            try
            {
                try { TaskDetailsDialog.Hide(); } catch { }

                var model = Services.TaskTemplateLibrary.ToTaskModel(template);

                ViewModel.DialogMode = DialogMode.FromTemplate;
                _isPopulatingDetails = true;
                _tempPipeline = new TaskPipeline();

                EditTaskName.Text = model.Name;
                EditTaskDescription.Text = model.Description;
                EditTaskAuthor.Text = model.Author;
                EditTaskCategory.Text = model.Category;
                EditTaskTags.Text = string.Join(", ", model.Tags);
                EditTaskEnabled.IsOn = true;

                _tempActions = new ObservableCollection<TaskActionModel>(model.Actions);
                _tempTriggers = new ObservableCollection<TaskTriggerModel>(model.TriggersList);
                ActionList.ItemsSource = _tempActions;
                TriggerList.ItemsSource = _tempTriggers;

                EditTaskRunWithHighestPrivileges.IsChecked = model.RunWithHighestPrivileges;
                EditTaskRunIfMissed.IsChecked = model.RunIfMissed;
                EditTaskOnlyIfIdle.IsChecked = model.OnlyIfIdle;
                EditTaskOnlyIfAC.IsChecked = model.OnlyIfAC;
                EditTaskWakeToRun.IsChecked = model.WakeToRun;

                PopulateNetworkList();
                UpdatePipelineSummaryLabel();

                _isPopulatingDetails = false;
                ActionList.SelectedIndex = 0;
                TriggerList.SelectedIndex = 0;

                EditTaskErrorBar.IsOpen = false;
                TaskEditDialog.XamlRoot = this.Content.XamlRoot;
                await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", $"Failed to open the editor for template '{template.Id}'.");
                await ShowErrorDialog(L("Templates.Error.Deploy", "Could not open this template: ") + ex.Message);
            }
        }

        private async void OpenCreateTaskDialog(ViewModels.ScriptTemplateModel? template)
        {
            if (this.Content?.XamlRoot == null) return;
            // WinUI only allows one open ContentDialog per XamlRoot - avoid throwing if
            // Task Details (or another dialog) is already showing when Ctrl+N is pressed.
            try { TaskDetailsDialog.Hide(); } catch { }
            ViewModel.DialogMode = template != null ? DialogMode.FromTemplate : DialogMode.Create;
            _tempPipeline = new TaskPipeline();
            UpdatePipelineSummaryLabel();

            EditTaskName.Text = template?.Name ?? "";
            EditTaskDescription.Text = template?.Description ?? "";
            EditTaskAuthor.Text = Environment.UserName;
            EditTaskCategory.Text = "";
            EditTaskTags.Text = "";
            EditTaskEnabled.IsOn = true;
            
            _tempActions = new ObservableCollection<TaskActionModel>();
            if (template != null)
            {
                _tempActions.Add(new TaskActionModel { Command = template.Command, Arguments = template.Arguments });
            }
            else
            {
                _tempActions.Add(new TaskActionModel { Command = "notepad.exe" });
            }

            _tempTriggers = new ObservableCollection<TaskTriggerModel> { new TaskTriggerModel { TriggerType = TriggerType.Daily, ScheduleInfo = FormatScheduleInfo(DateTime.Now), DailyInterval = 1 } };
            
            ActionList.ItemsSource = _tempActions;
            TriggerList.ItemsSource = _tempTriggers;
            ActionList.SelectedIndex = 0;
            TriggerList.SelectedIndex = 0;
            
            // Settings defaults
            EditTaskRunWithHighestPrivileges.IsChecked = template?.RunAsAdmin ?? false;

            PopulateNetworkList();
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            await TaskEditDialog.ShowAsync();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFolderStructure();
            _ = ViewModel.LoadTasksAsync();
            TaskListView.Focus(FocusState.Programmatic);
            UpdateFolderTreeMaxHeight();

            // Restore the last-used folder (see 2.2)
            string saved = Settings.LastFolderPath;
            if (!string.IsNullOrEmpty(saved) && saved != "\\")
            {
                _currentFolderPath = saved;
                ViewModel.SetFilter(saved);
                SelectFolderTreeNodeForPath(saved);
            }

            // Defer one frame so the ListView control template is fully applied before we set its internal ScrollViewer
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrollingSelf(Settings.SmoothScrolling);
                
                // Set custom title bar drag region
                App.m_window?.SetTitleBar(AppTitleBarDragArea);

                // Check for elevation and handle drag-and-drop limitations
                if (Helpers.ElevationHelper.IsElevated())
                {
                    AdminDragWarning.Visibility = Visibility.Collapsed; // We handle it via custom drag
                    TaskListView.CanDragItems = false;
                    TaskListView.AllowDrop = false;
                    FolderTreeView.AllowDrop = false;
                    
                    // Hook up custom drag events
                    this.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCustomDragPointerPressed), true);
                    this.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnCustomDragPointerMoved), true);
                    this.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnCustomDragPointerReleased), true);
                    // Note: Folder item dragging is disabled via early-return in FolderItem_DragStarting
                }
            });

            // Show startup dialogs in order: onboarding first, then changelog
            _ = CheckStartupDialogsAsync();
        }

        private async System.Threading.Tasks.Task CheckStartupDialogsAsync()
        {
            // Await onboarding first — on a fresh install the user must finish the
            // walkthrough before the "What's New" popup is shown on top.
            await CheckAndShowOnboardingAsync();

            // Only reaches here once onboarding is fully dismissed.
            await CheckAndShowChangelogAsync();
        }

        private async System.Threading.Tasks.Task CheckAndShowOnboardingAsync()
        {
            if (Settings.HasCompletedOnboarding) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = Settings.Theme };
                    await dialog.ShowAsync();
                }
                catch { /* XamlRoot not ready or dialog already open — skip silently */ }
                finally { tcs.TrySetResult(); }
            });
            await tcs.Task;
        }

        private async System.Threading.Tasks.Task CheckAndShowChangelogAsync()
        {
            try
            {
                var release = await Services.GitHubReleaseService.GetLatestReleaseAsync();
                if (release == null) return;

                string lastSeen = Settings.LastSeenVersion;
                if (string.Equals(release.TagName, lastSeen, StringComparison.OrdinalIgnoreCase)) return;

                // New version — marshal back to UI thread via TCS
                var tcs = new System.Threading.Tasks.TaskCompletionSource();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new Dialogs.WhatsNewDialog(release)
                        {
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Settings.Theme
                        };
                        await dialog.ShowAsync();
                        // Only persist after the user has actually seen the dialog
                        Settings.LastSeenVersion = release.TagName;
                    }
                    catch { /* dialog already open or XamlRoot not ready — skip silently */ }
                    finally { tcs.TrySetResult(); }
                });
                await tcs.Task;
            }
            catch { /* network unavailable or any other error — fail silently */ }
        }

        /// <summary>Directly applies smooth scrolling to all ScrollViewers owned by MainPage,
        /// including hidden dialog content and the ListView's internal ScrollViewer.
        /// Called both from Loaded and from the Settings toggle handler.</summary>
        public void ApplySmoothScrollingSelf(bool enable)
        {
            DetailsScrollViewer.IsScrollInertiaEnabled = enable;
            EditScrollViewer.IsScrollInertiaEnabled = enable;
            HistoryScrollViewer.IsScrollInertiaEnabled = enable;
            // TaskListView has an internal ScrollViewer in its control template
            foreach (var sv in FindDescendants<ScrollViewer>(TaskListView))
                sv.IsScrollInertiaEnabled = enable;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T match) return match;
            return FindParent<T>(parent);
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFolderTreeMaxHeight();


        private void UpdateFolderTreeMaxHeight()
        {
            if (NavView == null || FolderTreeView == null) return;
            // Estimated height of Footer Items (4 items + Settings) + Header ("New Task") + Margins
            // Footer: ~200px
            // Header (PaneCustomContent top part): 
            //   Dashboard (40) + ScriptLib (40) + NewTask (40) + Separator (10) + Margins (~20) = ~150px
            // "Folders" Label: ~30px
            // Buffer: ~50px 
            // Total deduction: ~430px
            double availableHeight = NavView.ActualHeight - 430; 
            if (availableHeight < 100) availableHeight = 100;
            FolderTreeView.MaxHeight = availableHeight;
        }

    }
}
