using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using FluentTaskScheduler.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new();

        // Forwarding property for x:Bind compatibility
        public ObservableCollection<ScheduledTaskModel> FilteredTasks => ViewModel.FilteredTasks;

        private DispatcherQueueTimer _searchDebounceTimer;
        private List<TaskHistoryEntry> _fullHistory = new List<TaskHistoryEntry>(); 
        private string _historyStatusFilter = "Total";
        
        // Dialog State
        private ObservableCollection<TaskActionModel> _tempActions = new();
        private ObservableCollection<TaskTriggerModel> _tempTriggers = new();
        private bool _isEditMode = false;
        private bool _isPopulatingDetails = false;
        private bool _isFromTemplate = false;
        
        // Current folder path for new task creation
        private string _currentFolderPath = "\\";
        private Dictionary<string, bool> _folderExpandedState = new();

        public static MainPage? Current { get; private set; }

        public MainPage()
        {
            Current = this;
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ViewModel.SearchText = SearchBox.Text;
            };
            
            NavView.SelectedItem = NavView.FooterMenuItems[0];  // Select "All Tasks" 
        }

        public void OpenCreateTaskFromTemplate(ViewModels.ScriptTemplateModel template) => OpenCreateTaskDialog(template);
        private void NewTaskButton_Click(object sender, RoutedEventArgs e) => OpenCreateTaskDialog(null);

        private async void OpenCreateTaskDialog(ViewModels.ScriptTemplateModel? template)
        {
            if (this.Content?.XamlRoot == null) return;
            _isEditMode = false;
            _isFromTemplate = template != null;
            
            EditTaskName.Text = template?.Name ?? "";
            EditTaskDescription.Text = template?.Description ?? "";
            EditTaskAuthor.Text = Environment.UserName;
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

            _tempTriggers = new ObservableCollection<TaskTriggerModel> { new TaskTriggerModel { TriggerType = "Daily", ScheduleInfo = DateTime.Now.ToString("g"), DailyInterval = 1 } };
            
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

            // Feature 3: restore last-used folder
            string saved = Services.SettingsService.LastFolderPath;
            if (!string.IsNullOrEmpty(saved) && saved != "\\")
            {
                _currentFolderPath = saved;
                ViewModel.SetFilter(saved);
            }

            // Defer one frame so the ListView control template is fully applied before we set its internal ScrollViewer
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrollingSelf(Services.SettingsService.SmoothScrolling);
            });
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

        // ========================================================================================================
        // Navigation & Loading
        // ========================================================================================================

        private void LoadFolderStructure()
        {
            try
            {
                var rootFolder = ViewModel.TaskService.GetFolderStructure();
                FolderTreeView.RootNodes.Clear();
                AddFolderToTree(rootFolder, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
        }

        private Dictionary<TreeViewNode, TaskFolderModel> _treeNodeFolderMap = new();

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
            treeNode.RegisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, (sender, dp) =>
            {
                if (sender is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var f))
                    _folderExpandedState[f.Path] = node.IsExpanded;
            });
            
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
                Services.SettingsService.LastFolderPath = folder.Path; // Feature 3: persist
                ViewModel.SetFilter(folder.Path);
                
                // Restore Task View
                NavView.Header = "Scheduled Tasks";
                TasksViewGrid.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Collapsed;
                
                NavView.SelectedItem = null; // Native indicator for Dashboard/ScriptLib disappears
                FolderTreeView.SelectedItem = node; 
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected) 
            {
                 NavView.Header = "Settings";
                 Frame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                var tag = item.Tag.ToString() ?? "";

                if (tag == "Dashboard")
                {
                    NavView.Header = "Dashboard";
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(DashboardPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptLibrary")
                {
                    NavView.Header = "Script Library";
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptLibraryPage), this);
                    FolderTreeView.SelectedItem = null;
                }
                else
                {
                    // Standard Task Views (if any)
                    NavView.Header = "Scheduled Tasks";
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
            NavView.Header = "Scheduled Tasks";
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
                if (BatchCountText != null) BatchCountText.Text = $"{count} selected";
                UpdateBatchActionsState();
            }
            if (count == 1) ViewModel.SelectedTask = (ScheduledTaskModel)TaskListView.SelectedItem;
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
        
        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsLoading) return;
            if (sender is ToggleSwitch ts && ts.IsLoaded && ts.DataContext is ScheduledTaskModel task)
            {
                // Only act if the toggle was likely user-initiated (has focus).
                // Programmatic changes during virtualization/recycling will not have focus.
                if (ts.FocusState == FocusState.Unfocused) return;

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
            
            // Load History
            _fullHistory = await Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            UpdateHistoryList();
            UpdateHistoryStats();
            
            TaskDetailsDialog.XamlRoot = this.Content.XamlRoot;
            await TaskDetailsDialog.ShowAsync();
        }

        private void UpdateHistoryList()
        {
            if (InlineHistoryListView == null) return;
            if (_historyStatusFilter == "Total") InlineHistoryListView.ItemsSource = _fullHistory;
            else if (_historyStatusFilter == "Success") InlineHistoryListView.ItemsSource = _fullHistory.Where(h => h.Result == "Task Completed");
            else if (_historyStatusFilter == "Failed") InlineHistoryListView.ItemsSource = _fullHistory.Where(h => h.Result != "Task Completed" && h.Result != "Task Started" && h.Result != "Task Registered");

            // Date filtering (Combo)
            if (HistoryFilterCombo != null && HistoryFilterCombo.SelectedItem is ComboBoxItem item)
            {
                 // To implement if needed, currently reusing logic
            }
        }
        
        private void UpdateHistoryStats()
        {
            StatTotalRuns.Text = _fullHistory.Count.ToString();
            StatSuccess.Text = _fullHistory.Count(h => h.Result == "Task Completed").ToString();
            StatFailed.Text = _fullHistory.Count(h => h.Result != "Task Completed" && h.Result != "Task Started").ToString();
            StatLastResult.Text = _fullHistory.FirstOrDefault()?.Result ?? "-";
            HistoryStatsGrid.Visibility = Visibility.Visible;
        }

        private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) => UpdateHistoryList(); // Placeholder for actual date logic
        private void ExportHistoryCsv_Click(object sender, RoutedEventArgs e) { /* Implement CSV export */ }
        
        private void CopyHistory_Click(object sender, RoutedEventArgs e) 
        {
             var dp = new DataPackage();
             dp.SetText(string.Join("\n", _fullHistory.Select(h => $"{h.Time}\t{h.Result}\t{h.Message}")));
             Clipboard.SetContent(dp);
        }
        
        private void StatTotal_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Total"; UpdateHistoryList(); }
        private void StatSuccess_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Success"; UpdateHistoryList(); }
        private void StatFailed_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Failed"; UpdateHistoryList(); }
        private void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e) { /* Copy logic */ }

        // ========================================================================================================
        // Task Operations (Single)
        // ========================================================================================================

        private void RunTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.RunTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.IsRunning = true;
                _ = WatchTaskUntilFinished(ViewModel.SelectedTask);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        private void StopTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.StopTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = "Ready";
                ViewModel.SelectedTask.IsRunning = false;
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        /// <summary>
        /// Polls Task Scheduler every 2 s until the task leaves the Running state,
        /// then writes the real state back to the model on the UI thread.
        /// </summary>
        private async System.Threading.Tasks.Task WatchTaskUntilFinished(ScheduledTaskModel task)
        {
            const int pollIntervalMs = 2000;
            const int maxPolls = 300; // 10 minutes max
            for (int i = 0; i < maxPolls; i++)
            {
                await System.Threading.Tasks.Task.Delay(pollIntervalMs);
                try
                {
                    string? liveState = await System.Threading.Tasks.Task.Run(
                        () => ViewModel.TaskService.GetTaskDetails(task.Path)?.State);

                    if (liveState == null) break; // task was deleted

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        task.State = liveState;
                        if (liveState != "Running")
                            task.IsRunning = false;  // hide the ring
                    });

                    if (liveState != "Running") break;
                }
                catch { break; }
            }
            // Safety net: ensure the ring is cleared even if we exit via maxPolls or exception
            DispatcherQueue.TryEnqueue(() => task.IsRunning = false);
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            
            // Hide the details dialog first to avoid "Only a single ContentDialog can be open" error
            try { TaskDetailsDialog.Hide(); } catch { }

            var dialog = new ContentDialog 
            { 
                Title = "Confirm Delete", 
                Content = $"Are you sure you want to delete '{ViewModel.SelectedTask.Name}'?", 
                PrimaryButtonText = "Delete", 
                CloseButtonText = "Cancel", 
                DefaultButton = ContentDialogButton.Close, 
                XamlRoot = this.XamlRoot 
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add("XML File", new List<string>() { ".xml" });
            picker.SuggestedFileName = ViewModel.SelectedTask.Name;
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try { ViewModel.TaskService.ExportTask(ViewModel.SelectedTask.Path, file.Path); } catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void ImportTask()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".xml");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // Simple import flow
                var folder = NavView.SelectedItem is NavigationViewItem item && item.Tag?.ToString()?.StartsWith("\\") == true ? item.Tag.ToString() : "\\";
                try
                {
                    string xml = await Windows.Storage.FileIO.ReadTextAsync(file);
                    ViewModel.TaskService.RegisterTaskFromXml(folder ?? "\\", System.IO.Path.GetFileNameWithoutExtension(file.Name), xml);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

        // ========================================================================================================
        // Task Editing (Dialog)
        // ========================================================================================================

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try { TaskDetailsDialog.Hide(); } catch { }

            _isEditMode = true;
            _isPopulatingDetails = true;
            _isFromTemplate = false;
            
            // Populate Dialog
            EditTaskName.Text = ViewModel.SelectedTask.Name;
            EditTaskDescription.Text = ViewModel.SelectedTask.Description;
            EditTaskAuthor.Text = ViewModel.SelectedTask.Author;
            EditTaskEnabled.IsOn = ViewModel.SelectedTask.IsEnabled;
            
            // Triggers
            _tempTriggers = new ObservableCollection<TaskTriggerModel>(ViewModel.SelectedTask.TriggersList);
            TriggerList.ItemsSource = _tempTriggers;
            
            // Actions
            _tempActions = new ObservableCollection<TaskActionModel>(ViewModel.SelectedTask.Actions);
            ActionList.ItemsSource = _tempActions;
            
            // Settings - simplified map back?
            // This is hard to "Refactor Cleanly" without binding everything.
            // For now, retaining basic load logic manually.
            EditTaskOnlyIfIdle.IsChecked = ViewModel.SelectedTask.OnlyIfIdle;
            // ... (Other settings would act similarly)
            
            PopulateNetworkList(); // TODO: Select correct network
            
            _isPopulatingDetails = false;
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            await TaskEditDialog.ShowAsync();
        }



        private async void TaskEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true; // Handle async manually
            
            if (string.IsNullOrWhiteSpace(EditTaskName.Text)) return;

            var model = new ScheduledTaskModel
            {
                Name = EditTaskName.Text,
                Description = EditTaskDescription.Text,
                Author = EditTaskAuthor.Text,
                IsEnabled = EditTaskEnabled.IsOn,
                Actions = new ObservableCollection<TaskActionModel>(_tempActions),
                TriggersList = new ObservableCollection<TaskTriggerModel>(_tempTriggers),
                // Map Settings
                OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked == true,
                OnlyIfAC = EditTaskOnlyIfAC.IsChecked == true,
                OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked == true,
                WakeToRun = EditTaskWakeToRun.IsChecked == true,
                // ...
            };
            
            // Handle folder
            string folder = "\\";
            if (_isFromTemplate)
            {
                folder = "\\";
            }
            else if (!_isEditMode) // New Task
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
                TaskEditDialog.Hide();
                _ = ViewModel.LoadTasksAsync();
            }
            catch (Exception ex) { await ShowErrorDialog("Failed to save task: " + ex.Message); }
        }

        // ========================================================================================================
        // UI Logic (Dialogs)
        // ========================================================================================================

        private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                _isPopulatingDetails = true;
                // Map Trigger Model -> UI
                foreach(var item in EditTaskTriggerType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.TriggerType) EditTaskTriggerType.SelectedItem = item;
                }
                
                DateTime.TryParse(tr.ScheduleInfo, out var dt);
                EditTaskStartDate.Date = dt == DateTime.MinValue ? DateTime.Today : dt;
                EditTaskStartTime.Time = dt == DateTime.MinValue ? DateTime.Now.TimeOfDay : dt.TimeOfDay;
                
                // Session State mapping
                foreach(var item in EditTaskSessionStateType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.SessionStateChangeType) EditTaskSessionStateType.SelectedItem = item;
                }

                UpdateTriggerPanelVisibility();
                _isPopulatingDetails = false;
            }
        }

        private void EditTaskSessionStateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskSessionStateType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.SessionStateChangeType = item.Tag.ToString()!;
            }
        }
        
        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            UpdateTriggerPanelVisibility();
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskTriggerType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.TriggerType = item.Tag.ToString()!;
            }
        }

        private void UpdateTriggerPanelVisibility()
        {
            if (TriggerDetailsPanel == null || PanelDaily == null || PanelWeekly == null || 
                PanelMonthly == null || PanelEvent == null || PanelIdle == null || 
                PanelSessionState == null || PanelStartTime == null || EditTaskTriggerType == null) return;

             TriggerDetailsPanel.Visibility = Visibility.Visible;
             PanelDaily.Visibility = Visibility.Collapsed;
             PanelWeekly.Visibility = Visibility.Collapsed;
             PanelMonthly.Visibility = Visibility.Collapsed;
             PanelEvent.Visibility = Visibility.Collapsed;
             PanelIdle.Visibility = Visibility.Collapsed;
             PanelSessionState.Visibility = Visibility.Collapsed;
             PanelStartTime.Visibility = Visibility.Visible;

             if (EditTaskTriggerType.SelectedItem is ComboBoxItem item)
             {
                 string type = item.Tag?.ToString() ?? "";
                 switch (type)
                 {
                     case "Daily": PanelDaily.Visibility = Visibility.Visible; break;
                     case "Weekly": PanelWeekly.Visibility = Visibility.Visible; break;
                     case "Monthly": PanelMonthly.Visibility = Visibility.Visible; break;
                     case "Event": PanelEvent.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case "OnIdle": PanelIdle.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case "SessionStateChange": PanelSessionState.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                 }
             }
        }

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
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file != null) EditTaskActionCommand.Text = file.Path;
        }

        private void PopulateNetworkList()
        {
            // Simplified for now - avoiding direct networking calls which might crash if permission missing
            // EditTaskNetworkSelection is in XAML
        }

        // List Buttons
        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e) { _tempTriggers.Add(new TaskTriggerModel { TriggerType="Daily", ScheduleInfo=DateTime.Now.ToString("g") }); TriggerList.SelectedIndex = _tempTriggers.Count - 1; }
        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e) { if (TriggerList.SelectedItem is TaskTriggerModel t) _tempTriggers.Remove(t); }
        private void BtnMoveTriggerUp_Click(object sender, RoutedEventArgs e) { /* ... */ }
        private void BtnMoveTriggerDown_Click(object sender, RoutedEventArgs e) { /* ... */ }

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
            string args = "-WindowStyle Hidden -Command \"& {Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Task Notification', 'Fluent Launcher')}\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel t) _tempActions.Remove(t); }
        private void BtnMoveActionUp_Click(object sender, RoutedEventArgs e) { /* ... */ }
        private void BtnMoveActionDown_Click(object sender, RoutedEventArgs e) { /* ... */ }

        // Handlers to satisfy XAML connection
        private void EditTaskExpires_Click(object sender, RoutedEventArgs e) 
        { 
            bool enabled = EditTaskExpires.IsChecked == true;
            EditTaskExpirationDate.IsEnabled = enabled;
            EditTaskExpirationTime.IsEnabled = enabled;
        }
        private void EditTaskRandomDelay_Click(object sender, RoutedEventArgs e) { if (EditTaskRandomDelayVal != null) EditTaskRandomDelayVal.IsEnabled = EditTaskRandomDelay.IsChecked == true; }
        private void EditTaskStopAfter_Click(object sender, RoutedEventArgs e) { if (EditTaskStopAfterVal != null) EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked == true; }
        private void EditTaskDailyRecurrence_Checked(object sender, RoutedEventArgs e) { if (DailyInterval != null) DailyInterval.IsEnabled = EditTaskDailyRecurrence.IsChecked == true; }
        private void UserContextRadio_Checked(object sender, RoutedEventArgs e) 
        { 
             if (EditTaskRunAsUser != null) EditTaskRunAsUser.IsEnabled = RunAsSpecificUser.IsChecked == true; 
             if (SystemUserWarning != null) SystemUserWarning.IsOpen = RunAsSystem.IsChecked == true;
        }
        private void RunAsSystem_Click(object sender, RoutedEventArgs e) => RunAsSystem.IsChecked = true;
        private void DialogScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e) {} // no-op

        // Batch
        private void UpdateBatchActionsState()
        {
            if (TaskListView.SelectedItems.Count <= 1) return;
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>();
            bool anyDisabled = tasks.Any(t => !t.IsEnabled);
            if (BatchRunBtn != null) BatchRunBtn.IsEnabled = !anyDisabled;
            if (BatchStopBtn != null) BatchStopBtn.IsEnabled = !anyDisabled;
        }
        private void BatchCancel_Click(object sender, RoutedEventArgs e) => TaskListView.SelectedItems.Clear();
        private void BatchRun_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot selection before anything changes.
            // Set IsRunning=true BEFORE calling RunTask so the ring appears immediately,
            // independently of the volatile State string.
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();
            foreach (var t in tasks)
            {
                t.IsRunning = true;           // show the ring immediately
                try
                {
                    ViewModel.TaskService.RunTask(t.Path);
                }
                catch { /* RunTask failed – watcher will correct IsRunning */ }
                _ = WatchTaskUntilFinished(t);
            }
        }
        private void BatchStop_Click(object sender, RoutedEventArgs e) => PerformBatchAction(t => { ViewModel.TaskService.StopTask(t.Path); t.State = "Ready"; });
        private void BatchEnable_Click(object sender, RoutedEventArgs e) { PerformBatchAction(t => { if (!t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, true); t.IsEnabled = true; } }); UpdateBatchActionsState(); }
        private void BatchDisable_Click(object sender, RoutedEventArgs e) { PerformBatchAction(t => { if (t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, false); t.IsEnabled = false; } }); UpdateBatchActionsState(); }
        private async void BatchDelete_Click(object sender, RoutedEventArgs e) 
        { 
             var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();
             var dialog = new ContentDialog { Title = "Confirm Delete", Content = $"Delete {tasks.Count} tasks?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = this.XamlRoot };
             if (await dialog.ShowAsync() == ContentDialogResult.Primary)
             {
                 foreach(var t in tasks) try { ViewModel.TaskService.DeleteTask(t.Path); } catch {}
                 _ = ViewModel.LoadTasksAsync();
             }
        }
        private void PerformBatchAction(System.Action<ScheduledTaskModel> action) { foreach (var task in TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList()) try { action(task); } catch { } }

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
            string arrow(string col) =>
                ViewModel.SortColumn == col ? (ViewModel.SortAscending ? " ▲" : " ▼") : "";

            void AddItem(string label, string col)
            {
                var item = new MenuFlyoutItem { Text = label + arrow(col) };
                item.Click += (s, _) => { ViewModel.SortBy(col); UpdateSortButtonText(); };
                flyout.Items.Add(item);
            }

            AddItem("Name",         "Name");
            AddItem("Status",       "Status");
            AddItem("Next Run",     "NextRun");
            AddItem("Last Run",     "LastRun");
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = "Clear Sort" };
            clear.Click += (s, _) => { ViewModel.ClearSort(); UpdateSortButtonText(); };
            flyout.Items.Add(clear);

            flyout.ShowAt(SortButton);
        }

        private void UpdateSortButtonText()
        {
            string arrow = ViewModel.SortAscending ? "▲" : "▼";
            SortButton.Content = string.IsNullOrEmpty(ViewModel.SortColumn)
                ? "Sort \u2195"
                : $"Sort {arrow} {ViewModel.SortColumn}";
        }

        private async void CreateFolder_Click(string parentPath)
        {
            var dialog = new ContentDialog { Title="New Folder", Content=new TextBox{PlaceholderText="Name"}, PrimaryButtonText="Create", CloseButtonText="Cancel", DefaultButton=ContentDialogButton.Primary, XamlRoot=this.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Content is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) { try { ViewModel.TaskService.CreateFolder(parentPath == "\\" ? "\\" + tb.Text : parentPath + "\\" + tb.Text); LoadFolderStructure(); } catch (Exception ex) { await ShowErrorDialog(ex.Message); } }
        }
        private async void DeleteFolder_Click(string path)
        {
            var dialog = new ContentDialog { Title="Delete Folder", Content=$"Delete '{path}'?", PrimaryButtonText="Delete", CloseButtonText="Cancel", DefaultButton=ContentDialogButton.Close, XamlRoot=this.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) { try { ViewModel.TaskService.DeleteFolder(path); LoadFolderStructure(); ViewModel.SetFilter("all"); } catch (Exception ex) { await ShowErrorDialog(ex.Message); } }
        }

        private bool _isDialogOpen = false;
        private async Task ShowErrorDialog(string message) 
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try { await new ContentDialog { Title = "Error", Content = message, CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync(); }
            finally { _isDialogOpen = false; }
        }

    }
}
