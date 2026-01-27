using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using System.Threading.Tasks;
using System.Linq;

namespace FluentTaskScheduler
{
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<ScheduledTaskModel> FilteredTasks { get; } = new();
        private List<ScheduledTaskModel> _allTasks = new();
        private readonly Services.TaskServiceWrapper _taskService = new();
        private ScheduledTaskModel _selectedTask = null!;
        private bool _isEditMode = false;
        private Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue = null!;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _searchDebounceTimer = null!;
        private List<TaskHistoryEntry> _fullHistory = new List<TaskHistoryEntry>();

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            NavView.SelectedItem = NavView.MenuItems[0]; // Default to 'All Tasks'
            
            // Initialize debounce timer for search
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _searchDebounceTimer = _dispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = System.TimeSpan.FromMilliseconds(300);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                if (!_isLoading) ApplyFilters();
            };
        }

        // Keyboard Accelerators
        private void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            LoadTasks();
        }

        private void NewTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            NewTaskButton_Click(this, new RoutedEventArgs());
        }

        private void EditTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (TaskListHasSelection())
                EditTask_Click(this, new RoutedEventArgs());
        }

        private void RunTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (TaskListHasSelection())
                RunTask_Click(this, new RoutedEventArgs());
        }

        private void DeleteTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            // Only handle if not editing text
            if (FocusManager.GetFocusedElement() is TextBox) return;
            
            args.Handled = true;
            if (TaskListHasSelection())
                DeleteTask_Click(this, new RoutedEventArgs());
        }

        private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (TaskDetailsDialog.Visibility == Visibility.Visible)
                TaskDetailsDialog.Hide();
            else if (TaskEditDialog.Visibility == Visibility.Visible)
                TaskEditDialog.Hide();
        }

        private bool TaskListHasSelection()
        {
            if (_selectedTask != null) return true;
            if (TaskListView.SelectedItem is ScheduledTaskModel task)
            {
                _selectedTask = task;
                return true;
            }
            return false;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTasks();
            
            // Ensure focus is on list for immediate keyboard usage
            TaskListView.Focus(FocusState.Programmatic);
        }

        private bool _isLoading = false;
        private bool _isApplyingFilters = false; // Guard to prevent toggle events during filtering
        private readonly Dictionary<string, bool> _userInteractedToggles = new Dictionary<string, bool>(); // Track which toggles user actually clicked
        
        // Multiple Actions Support
        private ObservableCollection<TaskActionModel> _tempActions = new();
        private bool _isPopulatingActionDetails = false;

        // Multiple Triggers Support
        private ObservableCollection<TaskTriggerModel> _tempTriggers = new();
        private bool _isPopulatingTriggerDetails = false;

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag != null && item.Tag.ToString() == "Add")
            {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
                NewTaskButton_Click(null, null);
#pragma warning restore CS8625
            }
        }
        
        private void EditTaskExpires_Click(object sender, RoutedEventArgs e)
        {
            var isEnabled = EditTaskExpires.IsChecked == true;
            EditTaskExpirationDate.IsEnabled = isEnabled;
            EditTaskExpirationTime.IsEnabled = isEnabled;
        }

        private void EditTaskRandomDelay_Click(object sender, RoutedEventArgs e)
        {
            EditTaskRandomDelayVal.IsEnabled = EditTaskRandomDelay.IsChecked == true;
        }

        private void EditTaskStopAfter_Click(object sender, RoutedEventArgs e)
        {
            EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked == true;
        }

        private async void LoadTasks()
        {
            if (_isLoading) return;
            _isLoading = true;
            
            try
            {
                if (LoadingRing != null) 
                {
                    LoadingRing.IsActive = true;
                    // Ensure UI updates before starting background work
                    await Task.Delay(10);
                }
                
                // Fetch on background thread
                var tasks = await Task.Run(() => 
                {
                    return _taskService.GetAllTasks();
                });
                
                _allTasks = tasks ?? new List<ScheduledTaskModel>();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tasks: {ex.Message}");
                _allTasks = new List<ScheduledTaskModel>();
            }
            finally
            {
                _isLoading = false;
                if (LoadingRing != null) LoadingRing.IsActive = false;
                
                // Apply filters AFTER setting _isLoading to false
                ApplyFilters();
            }
        }

        private void ApplyFilters()
        {
            _isApplyingFilters = true; // Set guard
            try
            {
                if (_allTasks == null || _allTasks.Count == 0)
                {
                    FilteredTasks.Clear();
                    return;
                }
                
                var query = _allTasks.AsEnumerable();

                // Search filter
                if (SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    query = query.Where(t => t.Name != null && t.Name.Contains(SearchBox.Text, System.StringComparison.OrdinalIgnoreCase));
                }

                // Tab filter
                if (NavView != null && NavView.SelectedItem is NavigationViewItem item && item.Tag != null)
                {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
                    string tag = item.Tag.ToString();
#pragma warning restore CS8600
                    if (tag == "running")
                    {
                        query = query.Where(t => t.State == "Running");
                    }
                    else if (tag == "enabled")
                    {
                        // Use Boolean IsEnabled instead of State string causing disconnects
                        query = query.Where(t => t.IsEnabled);
                    }
                    else if (tag == "disabled")
                    {
                        // Use Boolean IsEnabled instead of State string causing disconnects
                        query = query.Where(t => !t.IsEnabled);
                    }
                }

                // Materialize query to list BEFORE modifying FilteredTasks
                // This prevents "Collection modified" exceptions during enumeration
                var results = query.ToList();

                // Optimization: Handle initial load or empty state efficiently (O(N))
                if (FilteredTasks.Count == 0)
                {
                    foreach (var taskModel in results)
                    {
                        FilteredTasks.Add(taskModel);
                    }
                    return;
                }

                // Optimization: Use HashSet for O(1) lookups instead of O(N) Linear Search
                // This reduces the complexity from O(N^2) to O(N)
                var resultsSet = new HashSet<ScheduledTaskModel>(results);
                var currentSet = new HashSet<ScheduledTaskModel>(FilteredTasks);

                // Synchronize FilteredTasks with results to preserve scroll position
                // Removing items that are no longer in the filtered results
                for (int i = FilteredTasks.Count - 1; i >= 0; i--)
                {
                    if (!resultsSet.Contains(FilteredTasks[i]))
                    {
                        currentSet.Remove(FilteredTasks[i]);
                        FilteredTasks.RemoveAt(i);
                    }
                }

                // Inserting or moving items to match the results list
                for (int i = 0; i < results.Count; i++)
                {
                    var taskModel = results[i];
                    if (!currentSet.Contains(taskModel))
                    {
                        FilteredTasks.Insert(i, taskModel);
                        currentSet.Add(taskModel);
                    }
                    else
                    {
                        int oldIndex = FilteredTasks.IndexOf(taskModel);
                        if (oldIndex != i)
                        {
                            FilteredTasks.Move(oldIndex, i);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"Error applying filters: {ex.Message}");
            }
            finally
            {
                _isApplyingFilters = false; // Clear guard
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                Frame.Navigate(typeof(SettingsPage));
            }
            else
            {
                // Don't filter while loading tasks
                if (!_isLoading)
                {
                    ApplyFilters();
                }
            }
        }

        private async void NewTaskButton_Click(object sender, RoutedEventArgs e)
        {
            // Ensure XamlRoot is available
            if (this.Content?.XamlRoot == null)
            {
                System.Diagnostics.Debug.WriteLine("XamlRoot is null, cannot show dialog");
                return;
            }

            _isEditMode = false;
            // Reset dialog fields
            EditTaskName.Text = "";
            EditTaskDescription.Text = "";
            EditTaskAuthor.Text = System.Environment.UserName;
            EditTaskEnabled.IsOn = true;
            EditTaskActionCommand.Text = "";
            EditTaskArguments.Text = "";
            EditTaskWorkingDirectory.Text = "";
            
            // Initialize Actions List for New Task
            _tempActions = new ObservableCollection<TaskActionModel>();
            _tempActions.Add(new TaskActionModel { Command = "notepad.exe" });
            ActionList.ItemsSource = _tempActions;
            ActionList.SelectedIndex = 0;
            
            // Initialize Triggers List for New Task
            _tempTriggers = new ObservableCollection<TaskTriggerModel>();
            _tempTriggers.Add(new TaskTriggerModel 
            { 
                TriggerType = "Daily", 
                ScheduleInfo = DateTime.Now.ToString("g"),
                DailyInterval = 1
            });
            TriggerList.ItemsSource = _tempTriggers;
            TriggerList.SelectedIndex = 0;
            
            // Reset granular triggers
            DailyInterval.Text = "1";
            WeeklyInterval.Text = "1";
            
            WeeklyMon.IsChecked = false;
            WeeklyTue.IsChecked = false;
            WeeklyWed.IsChecked = false;
            WeeklyThu.IsChecked = false;
            WeeklyFri.IsChecked = false;
            WeeklySat.IsChecked = false;
            WeeklySun.IsChecked = false;
            
            // Reset advanced settings
            EditTaskTriggerType.SelectedIndex = 0;
            EditTaskStartDate.Date = DateTime.Today;
            EditTaskStartTime.Time = DateTime.Now.TimeOfDay;
            
            // Monthly
            MonthJan.IsChecked = true; MonthFeb.IsChecked = true; MonthMar.IsChecked = true; MonthApr.IsChecked = true;
            MonthMay.IsChecked = true; MonthJun.IsChecked = true; MonthJul.IsChecked = true; MonthAug.IsChecked = true;
            MonthSep.IsChecked = true; MonthOct.IsChecked = true; MonthNov.IsChecked = true; MonthDec.IsChecked = true;
            MonthlyRadioDays.IsChecked = true;
            MonthlyDaysInput.Text = "1";
            MonthlyWeekCombo.SelectedIndex = 0;
            MonthlyDayCombo.SelectedIndex = 0;
            
            // Expiration
            EditTaskExpires.IsChecked = false;
            EditTaskExpirationDate.IsEnabled = false;
            EditTaskExpirationTime.IsEnabled = false;
            EditTaskExpirationDate.Date = DateTimeOffset.Now.AddDays(1);
            EditTaskExpirationTime.Time = DateTime.Now.TimeOfDay;

            EditTaskRandomDelay.IsChecked = false;
            EditTaskRandomDelayVal.Text = "";
            EditTaskStopAfter.IsChecked = false;
            EditTaskStopAfterVal.IsEnabled = false;
            EditTaskStopAfterVal.SelectedIndex = -1;
            
            // Reset advanced settings
            EditTaskRepetitionInterval.SelectedIndex = 0;
            EditTaskRepetitionDuration.SelectedIndex = 0;
            EditTaskOnlyIfIdle.IsChecked = false;
            EditTaskOnlyIfAC.IsChecked = false;
            EditTaskOnlyIfNetwork.IsChecked = false;
            EditTaskWakeToRun.IsChecked = false;
            EditTaskStopOnBattery.IsChecked = false;
            EditTaskRunIfMissed.IsChecked = false;
            EditTaskRestartOnFailure.IsChecked = false;
            EditTaskRestartInterval.Text = "1 minute";
            EditTaskRestartCount.Value = 3;

            // Trigger visibility
            UpdateTriggerPanelVisibility();

            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            await TaskEditDialog.ShowAsync();
        }

        private async void BrowseAction_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".cmd");
            picker.FileTypeFilter.Add(".ps1");
            picker.FileTypeFilter.Add("*");
            
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var path = file.Path;
                var ext = System.IO.Path.GetExtension(path).ToLower();

                if (ext == ".ps1")
                {
                    EditTaskActionCommand.Text = "powershell.exe";
                    EditTaskArguments.Text = $"-File \"{path}\"";
                }
                else if (ext == ".bat" || ext == ".cmd")
                {
                    EditTaskActionCommand.Text = "cmd.exe";
                    EditTaskArguments.Text = $"/c \"{path}\"";
                }
                else if (ext == ".py")
                {
                    EditTaskActionCommand.Text = "python.exe";
                    EditTaskArguments.Text = $"\"{path}\"";
                }
                else
                {
                    EditTaskActionCommand.Text = path;
                    // Keep existing arguments if any, or clear? User didn't specify. 
                    // Usually picking a new EXE implies new context, but let's leave arguments alone if it's just an exe replace? 
                    // Actually, for consistency with the "smart" behavior which sets arguments, we should probably clear arguments if it's a standard EXE to avoid executing "notepad.exe -File script.ps1".
                    EditTaskArguments.Text = ""; 
                }
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Debounce search to prevent crashes during rapid typing
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void TaskListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ScheduledTaskModel task)
            {
                _selectedTask = task;
                await ShowTaskDetails();
            }
        }

        private async Task ShowTaskDetails()
        {
            if (_selectedTask == null) return;
            
            DialogTaskName.Text = _selectedTask.Name;
            DialogTaskDescription.Text = _selectedTask.Description;
            DialogTaskAuthor.Text = _selectedTask.Author;
            
            // Update button states based on task state
            RunTaskButton.IsEnabled = _selectedTask.IsEnabled;
            
            // Load history inline
            await LoadTaskHistoryInline(_selectedTask.Path);
            
            await TaskDetailsDialog.ShowAsync();
        }

        private async Task LoadTaskHistoryInline(string taskPath)
        {
            try
            {
                List<TaskHistoryEntry>? history = null;
                await Task.Run(() =>
                {
                    history = _taskService.GetTaskHistory(taskPath);
                });
                
                // Store full history and apply default filter (Today)
                _fullHistory = history ?? new List<TaskHistoryEntry>();
                
                // Reset dropdown UI to match
                if (HistoryFilterCombo != null) HistoryFilterCombo.SelectedIndex = 0;
                
                ApplyHistoryFilterByTag("Today");
            }
            catch
            {
                // Silently fail - history is optional
                _fullHistory = new List<TaskHistoryEntry>();
                InlineHistoryListView.ItemsSource = new List<TaskHistoryEntry>();
            }
        }

        private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_fullHistory == null || _fullHistory.Count == 0) return;
            
            var filter = (HistoryFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Today";
            ApplyHistoryFilterByTag(filter);
        }

        private void HistoryList_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Intercept Ctrl+C
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            if (e.Key == Windows.System.VirtualKey.C && ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                e.Handled = true;
                CopyHistory_Click(sender, null);
            }
        }

        private async void CopyHistory_Click(object sender, RoutedEventArgs? e)
        {
            if (InlineHistoryListView.SelectedItems.Count == 0) return;

            var textToCopy = string.Join("\n", 
                InlineHistoryListView.SelectedItems
                    .Cast<TaskHistoryEntry>()
                    .Select(h => $"{h.Time}\t{h.Result}\t{h.Message}"));

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(textToCopy);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            // Visual feedback
            CopyHistoryBtn.Content = "✓ Copied!";
            await Task.Delay(1500);
            CopyHistoryBtn.Content = "📋 Copy";
        }

        private void ApplyHistoryFilterByTag(string selectedFilter)
        {
            if (_fullHistory == null) return;

            var now = DateTime.Now;
            List<TaskHistoryEntry> filtered;

            switch (selectedFilter)
            {
                case "Today":
                    filtered = _fullHistory.Where(h => 
                    {
                        if (DateTime.TryParse(h.Time, out var dt))
                            return dt.Date == now.Date;
                        return false;
                    }).ToList();
                    break;

                case "Yesterday":
                    var yesterday = now.AddDays(-1).Date;
                    filtered = _fullHistory.Where(h => 
                    {
                        if (DateTime.TryParse(h.Time, out var dt))
                            return dt.Date == yesterday;
                        return false;
                    }).ToList();
                    break;

                case "Week":
                    var weekAgo = now.AddDays(-7);
                    filtered = _fullHistory.Where(h => 
                    {
                        if (DateTime.TryParse(h.Time, out var dt))
                            return dt >= weekAgo;
                        return false;
                    }).ToList();
                    break;

                case "All":
                default:
                    filtered = _fullHistory;
                    break;
            }

            InlineHistoryListView.ItemsSource = filtered;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTasks();
        }

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close details dialog first to avoid conflict
                TaskDetailsDialog.Hide();
                
                if (_selectedTask != null)
                {
                    _isEditMode = true;
                    
                    // Populate dialog with existing task data
                    EditTaskName.Text = _selectedTask.Name;
                    EditTaskDescription.Text = _selectedTask.Description;
                    EditTaskAuthor.Text = _selectedTask.Author;
                    EditTaskEnabled.IsOn = _selectedTask.IsEnabled;
                    
                    // Initialize Actions List (Deep Copy)
                    _tempActions = new ObservableCollection<TaskActionModel>();
                    if (_selectedTask.Actions != null)
                    {
                        foreach (var act in _selectedTask.Actions)
                        {
                            _tempActions.Add(new TaskActionModel 
                            { 
                                Command = act.Command, 
                                Arguments = act.Arguments, 
                                WorkingDirectory = act.WorkingDirectory 
                            });
                        }
                    }
                    ActionList.ItemsSource = _tempActions;
                    if (_tempActions.Count > 0) ActionList.SelectedIndex = 0;
                    
                    // Initialize Triggers List (Deep Copy)
                    _tempTriggers = new ObservableCollection<TaskTriggerModel>();
                    if (_selectedTask.TriggersList != null && _selectedTask.TriggersList.Count > 0)
                    {
                        foreach (var trig in _selectedTask.TriggersList)
                        {
                            _tempTriggers.Add(new TaskTriggerModel 
                            { 
                                TriggerType = trig.TriggerType,
                                ScheduleInfo = trig.ScheduleInfo,
                                DailyInterval = trig.DailyInterval,
                                WeeklyInterval = trig.WeeklyInterval,
                                WeeklyDays = new List<string>(trig.WeeklyDays),
                                MonthlyIsDayOfWeek = trig.MonthlyIsDayOfWeek,
                                MonthlyMonths = new List<string>(trig.MonthlyMonths),
                                MonthlyDays = new List<int>(trig.MonthlyDays),
                                MonthlyWeek = trig.MonthlyWeek,
                                MonthlyDayOfWeek = trig.MonthlyDayOfWeek,
                                ExpirationDate = trig.ExpirationDate,
                                RandomDelay = trig.RandomDelay,
                                EventLog = trig.EventLog,
                                EventSource = trig.EventSource,
                                EventId = trig.EventId,
                                RepetitionInterval = trig.RepetitionInterval,
                                RepetitionDuration = trig.RepetitionDuration
                            });
                        }
                    }
                    else
                    {
                        // Fallback: create a trigger from legacy properties
                        _tempTriggers.Add(new TaskTriggerModel 
                        { 
                            TriggerType = _selectedTask.TriggerType,
                            ScheduleInfo = _selectedTask.ScheduleInfo,
                            DailyInterval = _selectedTask.DailyInterval,
                            WeeklyInterval = _selectedTask.WeeklyInterval,
                            WeeklyDays = new List<string>(_selectedTask.WeeklyDays),
                            MonthlyIsDayOfWeek = _selectedTask.MonthlyIsDayOfWeek,
                            MonthlyMonths = new List<string>(_selectedTask.MonthlyMonths),
                            MonthlyDays = new List<int>(_selectedTask.MonthlyDays),
                            MonthlyWeek = _selectedTask.MonthlyWeek,
                            MonthlyDayOfWeek = _selectedTask.MonthlyDayOfWeek,
                            ExpirationDate = _selectedTask.ExpirationDate,
                            RandomDelay = _selectedTask.RandomDelay,
                            EventLog = _selectedTask.EventLog,
                            EventSource = _selectedTask.EventSource,
                            EventId = _selectedTask.EventId,
                            RepetitionInterval = _selectedTask.RepetitionInterval,
                            RepetitionDuration = _selectedTask.RepetitionDuration
                        });
                    }
                    TriggerList.ItemsSource = _tempTriggers;
                    if (_tempTriggers.Count > 0) TriggerList.SelectedIndex = 0;

                    // Stop If Runs Longer Than (task level setting, not trigger level)
                    SetComboBoxByTag(EditTaskStopAfterVal, _selectedTask.StopIfRunsLongerThan);
                    if (!string.IsNullOrEmpty(_selectedTask.StopIfRunsLongerThan) && _selectedTask.StopIfRunsLongerThan != "PT0S")
                    {
                         EditTaskStopAfter.IsChecked = true;
                         EditTaskStopAfterVal.IsEnabled = true;
                    }
                    else
                    {
                         EditTaskStopAfter.IsChecked = false;
                         EditTaskStopAfterVal.IsEnabled = false;
                    }
                }
                else
                {
                    // New Task Defaults
                    _isEditMode = false;
                    EditTaskName.Text = "";
                    EditTaskDescription.Text = "";
                    EditTaskAuthor.Text = Environment.UserName;
                    EditTaskEnabled.IsOn = true;
                    //EditTaskActionCommand.Text = ""; // Removed in phase 5
                    //EditTaskArguments.Text = "";    // Removed in phase 5
                    //EditTaskWorkingDirectory.Text = ""; // Removed in phase 5
                    
                    EditTaskStartDate.Date = DateTime.Now;
                    EditTaskStartTime.Time = DateTime.Now.TimeOfDay;
                    
                    EditTaskTriggerType.SelectedIndex = 0; // Daily
                    UpdateTriggerPanelVisibility();
                    
                    EditTaskEventLog.Text = "Application";
                    EditTaskEventSource.Text = "";
                    EditTaskEventId.Text = "";
                    
                    // Default Action
                    _tempActions = new ObservableCollection<TaskActionModel>();
                    _tempActions.Add(new TaskActionModel { Command = "notepad.exe" });
                    ActionList.ItemsSource = _tempActions;
                    ActionList.SelectedIndex = 0;
                }
                
                var result = await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error Opening Task",
                    Content = $"An error occurred: {ex.Message}\n{ex.StackTrace}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        
        // --- Multiple Actions Event Handlers ---

        private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var action = ActionList.SelectedItem as TaskActionModel;
            if (action == null)
            {
                ActionDetailsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _isPopulatingActionDetails = true;
            ActionDetailsPanel.Visibility = Visibility.Visible;
            EditTaskActionCommand.Text = action.Command;
            EditTaskArguments.Text = action.Arguments;
            EditTaskWorkingDirectory.Text = action.WorkingDirectory;
            _isPopulatingActionDetails = false;
            
            // Enable/Disable move buttons
            int index = ActionList.SelectedIndex;
            BtnMoveActionUp.IsEnabled = index > 0;
            BtnMoveActionDown.IsEnabled = index >= 0 && index < _tempActions.Count - 1;
        }

        private void BtnAddAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newAction = new TaskActionModel { Command = "New Program" };
                _tempActions.Add(newAction);
                ActionList.SelectedItem = newAction;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding action: {ex.Message}");
            }
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var action = ActionList.SelectedItem as TaskActionModel;
                if (action != null)
                {
                    _tempActions.Remove(action);
                    if (_tempActions.Count > 0) ActionList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing action: {ex.Message}");
            }
        }

        private void BtnMoveActionUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int index = ActionList.SelectedIndex;
                if (index > 0)
                {
                    _tempActions.Move(index, index - 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving action up: {ex.Message}");
            }
        }

        private void BtnMoveActionDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int index = ActionList.SelectedIndex;
                if (index >= 0 && index < _tempActions.Count - 1)
                {
                    _tempActions.Move(index, index + 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving action down: {ex.Message}");
            }
        }

        private void EditTaskActionCommand_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingActionDetails) return;
            if (ActionList.SelectedItem is TaskActionModel action)
            {
                action.Command = EditTaskActionCommand.Text;
            }
        }

        private void EditTaskArguments_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingActionDetails) return;
            if (ActionList.SelectedItem is TaskActionModel action)
            {
                action.Arguments = EditTaskArguments.Text;
            }
        }

        private void EditTaskWorkingDirectory_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPopulatingActionDetails) return;
            if (ActionList.SelectedItem is TaskActionModel action)
            {
                action.WorkingDirectory = EditTaskWorkingDirectory.Text;
            }
        }

        // --- Multiple Triggers Event Handlers ---

        private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var trigger = TriggerList.SelectedItem as TaskTriggerModel;
            if (trigger == null)
            {
                TriggerDetailsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _isPopulatingTriggerDetails = true;
            TriggerDetailsPanel.Visibility = Visibility.Visible;
            
            // Populate trigger type
            for (int i = 0; i < EditTaskTriggerType.Items.Count; i++)
            {
                if ((EditTaskTriggerType.Items[i] as ComboBoxItem)?.Tag?.ToString() == trigger.TriggerType)
                {
                    EditTaskTriggerType.SelectedIndex = i;
                    break;
                }
            }
            
            // Populate start time
            DateTime start = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(trigger.ScheduleInfo) && DateTime.TryParse(trigger.ScheduleInfo, out var parsedStart))
            {
                start = parsedStart;
            }
            EditTaskStartDate.Date = start;
            EditTaskStartTime.Time = start.TimeOfDay;
            
            // Daily
            DailyInterval.Text = (trigger.DailyInterval > 0 ? trigger.DailyInterval : 1).ToString();
            
            // Weekly
            WeeklyInterval.Text = (trigger.WeeklyInterval > 0 ? trigger.WeeklyInterval : 1).ToString();
            WeeklyMon.IsChecked = trigger.WeeklyDays.Contains("Monday");
            WeeklyTue.IsChecked = trigger.WeeklyDays.Contains("Tuesday");
            WeeklyWed.IsChecked = trigger.WeeklyDays.Contains("Wednesday");
            WeeklyThu.IsChecked = trigger.WeeklyDays.Contains("Thursday");
            WeeklyFri.IsChecked = trigger.WeeklyDays.Contains("Friday");
            WeeklySat.IsChecked = trigger.WeeklyDays.Contains("Saturday");
            WeeklySun.IsChecked = trigger.WeeklyDays.Contains("Sunday");
            
            // Monthly
            SetMonthChecks(trigger.MonthlyMonths);
            if (trigger.MonthlyIsDayOfWeek)
            {
                MonthlyRadioOn.IsChecked = true;
                SetComboBoxText(MonthlyWeekCombo, trigger.MonthlyWeek);
                SetComboBoxText(MonthlyDayCombo, trigger.MonthlyDayOfWeek);
            }
            else
            {
                MonthlyRadioDays.IsChecked = true;
                var daysList = trigger.MonthlyDays.Select(d => d == 32 ? "Last" : d.ToString()).ToList();
                MonthlyDaysInput.Text = string.Join(", ", daysList);
            }
            
            // Expiration
            if (trigger.ExpirationDate.HasValue)
            {
                EditTaskExpires.IsChecked = true;
                EditTaskExpirationDate.Date = trigger.ExpirationDate.Value;
                EditTaskExpirationTime.Time = trigger.ExpirationDate.Value.TimeOfDay;
                EditTaskExpirationDate.IsEnabled = true;
                EditTaskExpirationTime.IsEnabled = true;
            }
            else
            {
                EditTaskExpires.IsChecked = false;
                EditTaskExpirationDate.IsEnabled = false;
                EditTaskExpirationTime.IsEnabled = false;
            }
            
            // Random Delay
            if (!string.IsNullOrEmpty(trigger.RandomDelay))
            {
                EditTaskRandomDelay.IsChecked = true;
                EditTaskRandomDelayVal.IsEnabled = true;
                EditTaskRandomDelayVal.Text = trigger.RandomDelay;
            }
            else
            {
                EditTaskRandomDelay.IsChecked = false;
                EditTaskRandomDelayVal.IsEnabled = false;
                EditTaskRandomDelayVal.Text = "";
            }
            
            // Event Trigger
            if (trigger.TriggerType == "Event")
            {
                EditTaskEventLog.Text = trigger.EventLog;
                EditTaskEventSource.Text = trigger.EventSource;
                EditTaskEventId.Text = trigger.EventId?.ToString() ?? "";
            }
            
            // Repetition
            SetComboBoxByTag(EditTaskRepetitionInterval, trigger.RepetitionInterval);
            SetComboBoxByTag(EditTaskRepetitionDuration, trigger.RepetitionDuration);

            UpdateTriggerPanelVisibility();
            
            // Enable/Disable move buttons
            int index = TriggerList.SelectedIndex;
            BtnMoveTriggerUp.IsEnabled = index > 0;
            BtnMoveTriggerDown.IsEnabled = index >= 0 && index < _tempTriggers.Count - 1;
            
            _isPopulatingTriggerDetails = false;
        }

        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newTrigger = new TaskTriggerModel 
                { 
                    TriggerType = "Daily", 
                    ScheduleInfo = DateTime.Now.ToString("g"),
                    DailyInterval = 1
                };
                _tempTriggers.Add(newTrigger);
                TriggerList.SelectedItem = newTrigger;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding trigger: {ex.Message}");
            }
        }

        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var trigger = TriggerList.SelectedItem as TaskTriggerModel;
                if (trigger != null && _tempTriggers.Count > 1) // Keep at least one trigger
                {
                    _tempTriggers.Remove(trigger);
                    if (_tempTriggers.Count > 0) TriggerList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing trigger: {ex.Message}");
            }
        }

        private void BtnMoveTriggerUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int index = TriggerList.SelectedIndex;
                if (index > 0)
                {
                    _tempTriggers.Move(index, index - 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving trigger up: {ex.Message}");
            }
        }

        private void BtnMoveTriggerDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int index = TriggerList.SelectedIndex;
                if (index >= 0 && index < _tempTriggers.Count - 1)
                {
                    _tempTriggers.Move(index, index + 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving trigger down: {ex.Message}");
            }
        }

        private void SaveCurrentTriggerToModel()
        {
            if (_isPopulatingTriggerDetails) return;
            if (TriggerList.SelectedItem is not TaskTriggerModel trigger) return;
            
            // Trigger type
            trigger.TriggerType = (EditTaskTriggerType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
            
            // Start time
            var startDate = EditTaskStartDate.Date.Date;
            var startTime = EditTaskStartTime.Time;
            trigger.ScheduleInfo = (startDate + startTime).ToString("g");
            
            // Daily
            if (short.TryParse(DailyInterval.Text, out short dailyInt) && dailyInt > 0)
                trigger.DailyInterval = dailyInt;
            
            // Weekly
            if (short.TryParse(WeeklyInterval.Text, out short weeklyInt) && weeklyInt > 0)
                trigger.WeeklyInterval = weeklyInt;
            trigger.WeeklyDays = GetWeeklyDays();
            
            // Monthly
            trigger.MonthlyMonths = GetSelectedMonths();
            trigger.MonthlyIsDayOfWeek = MonthlyRadioOn.IsChecked == true;
            if (trigger.MonthlyIsDayOfWeek)
            {
                trigger.MonthlyWeek = (MonthlyWeekCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "First";
                trigger.MonthlyDayOfWeek = (MonthlyDayCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Monday";
            }
            else
            {
                trigger.MonthlyDays = ParseMonthlyDays(MonthlyDaysInput.Text);
            }
            
            // Expiration
            if (EditTaskExpires.IsChecked == true)
            {
                var expDate = EditTaskExpirationDate.Date.Date;
                var expTime = EditTaskExpirationTime.Time;
                trigger.ExpirationDate = expDate + expTime;
            }
            else
            {
                trigger.ExpirationDate = null;
            }
            
            // Random Delay
            trigger.RandomDelay = EditTaskRandomDelay.IsChecked == true ? EditTaskRandomDelayVal.Text : "";
            
            // Event
            if (trigger.TriggerType == "Event")
            {
                trigger.EventLog = EditTaskEventLog.Text;
                trigger.EventSource = EditTaskEventSource.Text;
                trigger.EventId = int.TryParse(EditTaskEventId.Text, out int eid) ? eid : null;
            }
            
            // Repetition
            trigger.RepetitionInterval = (EditTaskRepetitionInterval.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            trigger.RepetitionDuration = (EditTaskRepetitionDuration.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        }

        private void SetComboBoxByTag(ComboBox comboBox, string tag)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if ((comboBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == tag)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }

        private void ToggleSwitch_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Mark that user actually clicked this toggle
            if (sender is ToggleSwitch toggle && toggle.DataContext is ScheduledTaskModel task)
            {
                _userInteractedToggles[task.Path] = true;
                System.Diagnostics.Debug.WriteLine($"User clicked toggle for: {task.Name}");
            }
        }

        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        // CRITICAL: Don't process toggles during filtering - this was causing tasks to be enabled/disabled when changing filters!
        if (_isApplyingFilters || _isLoading)
            return;

        // Safety check for null sender
        if (sender is not ToggleSwitch toggle)
            return;

        // During virtualization, DataContext might be null or not yet set
        if (toggle.DataContext is not ScheduledTaskModel task)
            return;

        // Prevent redundant operations during data binding/virtualization
        if (task.IsEnabled == toggle.IsOn)
            return;

        // CRITICAL: Only process if user actually interacted with this toggle
        if (!_userInteractedToggles.ContainsKey(task.Path) || !_userInteractedToggles[task.Path])
        {
            System.Diagnostics.Debug.WriteLine($"Ignoring toggle for {task.Name} - no user interaction detected");
            return;
        }

        // Clear the interaction flag
        _userInteractedToggles[task.Path] = false;

        try
        {
            // Update the model first
            task.IsEnabled = toggle.IsOn;
            
            // Then update the service
            if (toggle.IsOn)
            {
                _taskService.EnableTask(task.Path);
                task.State = "Ready";
            }
            else
            {
                _taskService.DisableTask(task.Path);
                task.State = "Disabled";
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error toggling task: {ex.Message}");
            // Revert toggle on error, but protect against re-entry
            var currentState = toggle.IsOn;
            toggle.Toggled -= ToggleSwitch_Toggled;
            toggle.IsOn = !currentState;
            toggle.Toggled += ToggleSwitch_Toggled;
        }
    }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }


#pragma warning disable CS1998 // Async method lacks 'await' operators
        private async void TaskEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
#pragma warning restore CS1998
        {
            if (string.IsNullOrWhiteSpace(EditTaskName.Text))
            {
                args.Cancel = true;
                return;
            }

            try
            {
                // Save current trigger state before saving
                SaveCurrentTriggerToModel();
                
                var newTask = new ScheduledTaskModel
                {
                    Name = EditTaskName.Text,
                    Description = EditTaskDescription.Text,
                    Author = EditTaskAuthor.Text,
                    IsEnabled = EditTaskEnabled.IsOn,
                    Actions = new ObservableCollection<TaskActionModel>(_tempActions),
                    TriggersList = new ObservableCollection<TaskTriggerModel>(_tempTriggers),
                    RunWithHighestPrivileges = EditTaskRunWithHighestPrivileges.IsChecked ?? false,
                    
                    // Conditions
                    OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked ?? false,
                    OnlyIfAC = EditTaskOnlyIfAC.IsChecked ?? false,
                    OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked ?? false,
                    WakeToRun = EditTaskWakeToRun.IsChecked ?? false,
                    StopOnBattery = EditTaskStopOnBattery.IsChecked ?? false,
                    
                    // Settings
                    StopIfRunsLongerThan = EditTaskStopAfter.IsChecked == true ? ((EditTaskStopAfterVal.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PT72H") : "",
                    RunIfMissed = EditTaskRunIfMissed.IsChecked ?? false,
                    RestartOnFailure = EditTaskRestartOnFailure.IsChecked ?? false,
                    RestartInterval = ParseRestartInterval(),
                    RestartCount = int.TryParse(EditTaskRestartCount.Text, out var count) ? count : 3
                };

                if (_isEditMode && _selectedTask != null)
                {
                    // Delete old task and create new one with updated values
                    _taskService.DeleteTask(_selectedTask.Path);
                }
                
                _taskService.RegisterTask(newTask);
                
                // Defer LoadTasks to avoid collection modification issues
                _dispatcherQueue.TryEnqueue(() => LoadTasks());
            }
            catch (System.Exception ex)
            {
                // Log to file
                try 
                {
                    string logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "crash_log.txt");
                    string logContent = $"[{DateTime.Now}] Save Error: {ex.Message}\nStack Trace: {ex.StackTrace}\n\n";
                    System.IO.File.AppendAllText(logPath, logContent);
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"Error creating task: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Cancel dialog close - error is logged to crash_log.txt
                args.Cancel = true;
            }
        }

        private void RunTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask != null)
            {
                try
                {
                    _taskService.RunTask(_selectedTask.Path);
                    TaskDetailsDialog.Hide();
                    
                    // Defer LoadTasks to avoid collection modification issues
                    _dispatcherQueue.TryEnqueue(() => LoadTasks());
                }
                catch { }
            }
        }

        private void StopTask_Click(object sender, RoutedEventArgs e)
        {
             if (_selectedTask != null)
            {
                try
                {
                    _taskService.StopTask(_selectedTask.Path);
                    TaskDetailsDialog.Hide();
                    
                    // Defer LoadTasks to avoid collection modification issues
                    _dispatcherQueue.TryEnqueue(() => LoadTasks());
                }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}");
            }
        }
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        
        try
        {
            var taskToDelete = _selectedTask;
            
            // Must hide TaskDetailsDialog FIRST - can't have 2 ContentDialogs open
            TaskDetailsDialog.Hide();
            
            if (FluentTaskScheduler.Services.SettingsService.ConfirmDelete)
            {
                // Small delay to ensure TaskDetailsDialog is fully closed
                await System.Threading.Tasks.Task.Delay(100);
                
                var confirmDialog = new ContentDialog
                {
                    Title = "Delete Task",
                    Content = $"Are you sure you want to delete '{taskToDelete.Name}'?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };
                
                var result = await confirmDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;
            }

            // Delete the task
            _taskService.DeleteTask(taskToDelete.Path);
            
            // Reload tasks after a short delay
            await System.Threading.Tasks.Task.Delay(200);
            LoadTasks();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}\n{ex.StackTrace}");
            
            // Try to recover by reloading tasks
            try
            {
                await System.Threading.Tasks.Task.Delay(100);
                LoadTasks();
            }
            catch { }
        }
    }

        private async void EditXml_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;

            // Hide main dialog to show the editor (WinUI only allows one ContentDialog at a time)
            TaskDetailsDialog.Hide();

            try
            {
                string xml = await Task.Run(() => _taskService.GetTaskXml(_selectedTask.Path));
                var dialog = new FluentTaskScheduler.Dialogs.XmlEditorDialog(xml);
                dialog.XamlRoot = this.XamlRoot;
                
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                     string newXml = dialog.XmlContent;
                     try 
                     {
                        await Task.Run(() => _taskService.UpdateTaskXml(_selectedTask.Path, newXml));
                        
                        // Refresh task details to reflect potentially changed name/author/etc
                        var updatedTask = await Task.Run(() => _taskService.GetTaskDetails(_selectedTask.Path));
                        if (updatedTask != null)
                        {
                            _selectedTask = updatedTask;
                            // Update UI fields
                            DialogTaskName.Text = updatedTask.Name;
                            DialogTaskDescription.Text = updatedTask.Description;
                            DialogTaskAuthor.Text = updatedTask.Author;
                            RunTaskButton.IsEnabled = updatedTask.IsEnabled;
                        }
                     }
                     catch (Exception ex)
                     {
                         var errDialog = new ContentDialog
                         {
                             Title = "Error Saving XML",
                             Content = ex.Message,
                             CloseButtonText = "OK",
                             XamlRoot = this.XamlRoot
                         };
                         await errDialog.ShowAsync();
                     }
                }
            }
            catch
            {
                 // Handle errors getting XML
            }
            finally
            {
                // Re-show the details dialog
                await TaskDetailsDialog.ShowAsync();
            }
        }

        private async void ExportTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;

            try
            {
                var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                
                savePicker.SuggestedFileName = _selectedTask.Name;
                savePicker.FileTypeChoices.Add("XML File", new System.Collections.Generic.List<string> { ".xml" });
                
                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    _taskService.ExportTask(_selectedTask.Path, file.Path);
                    
                    var successDialog = new ContentDialog
                    {
                        Title = "Success",
                        Content = $"Task exported to: {file.Path}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to export task: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void CloneTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;

            try
            {
                TaskDetailsDialog.Hide();

                _isEditMode = false; // New task mode

                // Pre-fill with cloned data
                EditTaskName.Text = _selectedTask.Name + " (Copy)";
                EditTaskDescription.Text = _selectedTask.Description;
                EditTaskAuthor.Text = _selectedTask.Author;
                EditTaskEnabled.IsOn = _selectedTask.IsEnabled;
                EditTaskRunWithHighestPrivileges.IsChecked = _selectedTask.RunWithHighestPrivileges;

                // Clone Actions
                _tempActions = new ObservableCollection<TaskActionModel>();
                foreach (var act in _selectedTask.Actions)
                {
                    _tempActions.Add(new TaskActionModel
                    {
                        Command = act.Command,
                        Arguments = act.Arguments,
                        WorkingDirectory = act.WorkingDirectory
                    });
                }
                ActionList.ItemsSource = _tempActions;
                if (_tempActions.Count > 0) ActionList.SelectedIndex = 0;

                // Clone Triggers
                _tempTriggers = new ObservableCollection<TaskTriggerModel>();
                foreach (var trig in _selectedTask.TriggersList)
                {
                    _tempTriggers.Add(new TaskTriggerModel
                    {
                        TriggerType = trig.TriggerType,
                        ScheduleInfo = trig.ScheduleInfo,
                        DailyInterval = trig.DailyInterval,
                        WeeklyInterval = trig.WeeklyInterval,
                        WeeklyDays = new List<string>(trig.WeeklyDays),
                        MonthlyIsDayOfWeek = trig.MonthlyIsDayOfWeek,
                        MonthlyMonths = new List<string>(trig.MonthlyMonths),
                        MonthlyDays = new List<int>(trig.MonthlyDays),
                        MonthlyWeek = trig.MonthlyWeek,
                        MonthlyDayOfWeek = trig.MonthlyDayOfWeek,
                        ExpirationDate = trig.ExpirationDate,
                        RandomDelay = trig.RandomDelay,
                        EventLog = trig.EventLog,
                        EventSource = trig.EventSource,
                        EventId = trig.EventId,
                        RepetitionInterval = trig.RepetitionInterval,
                        RepetitionDuration = trig.RepetitionDuration
                    });
                }
                TriggerList.ItemsSource = _tempTriggers;
                if (_tempTriggers.Count > 0) TriggerList.SelectedIndex = 0;

                // Clone Conditions
                EditTaskOnlyIfIdle.IsChecked = _selectedTask.OnlyIfIdle;
                EditTaskOnlyIfAC.IsChecked = _selectedTask.OnlyIfAC;
                EditTaskOnlyIfNetwork.IsChecked = _selectedTask.OnlyIfNetwork;
                EditTaskWakeToRun.IsChecked = _selectedTask.WakeToRun;
                EditTaskStopOnBattery.IsChecked = _selectedTask.StopOnBattery;

                // Clone Settings
                SetComboBoxByTag(EditTaskStopAfterVal, _selectedTask.StopIfRunsLongerThan);
                EditTaskStopAfter.IsChecked = !string.IsNullOrEmpty(_selectedTask.StopIfRunsLongerThan) && _selectedTask.StopIfRunsLongerThan != "PT0S";
                EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked ?? false;
                EditTaskRunIfMissed.IsChecked = _selectedTask.RunIfMissed;
                EditTaskRestartOnFailure.IsChecked = _selectedTask.RestartOnFailure;
                EditTaskRestartInterval.Text = _selectedTask.RestartInterval;
                EditTaskRestartCount.Value = _selectedTask.RestartCount;

                await TaskEditDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cloning task: {ex.Message}");
            }
        }

        // Dictionary to track temporarily disabled tasks and their re-enable times
        private Dictionary<string, (DateTime ReEnableTime, Microsoft.UI.Dispatching.DispatcherQueueTimer Timer)> _disabledTasks = new();

        private async void DisableFor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null) return;

            var menuItem = sender as MenuFlyoutItem;
            if (menuItem?.Tag == null) return;

            if (int.TryParse(menuItem.Tag.ToString(), out int hours))
            {
                await DisableTaskForDuration(TimeSpan.FromHours(hours));
            }
        }

        private async void DisableForCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTask == null)
                {
                    System.Diagnostics.Debug.WriteLine("DisableForCustom_Click: No task selected.");
                    return;
                }

                // Must hide the current dialog first before showing another
                try { TaskDetailsDialog.Hide(); } catch {}
            
                // Wait for dialog to close
                await Task.Delay(300);

                var inputDialog = new ContentDialog
                {
                    Title = "Disable for Custom Duration",
                    PrimaryButtonText = "Disable",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };
                
                var hoursInput = new NumberBox
                {
                    Header = "Hours",
                    Minimum = 0,
                    Maximum = 168,
                    Value = 1,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
                };
                
                var minutesInput = new NumberBox
                {
                    Header = "Minutes",
                    Minimum = 0,
                    Maximum = 59,
                    Value = 0,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
                };

                panel.Children.Add(hoursInput);
                panel.Children.Add(minutesInput);

                inputDialog.Content = panel;

                var result = await inputDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var duration = TimeSpan.FromHours(hoursInput.Value).Add(TimeSpan.FromMinutes(minutesInput.Value));
                    await DisableTaskForDuration(duration);
                }
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"DisableForCustom_Click Error: {ex.Message}");
            }
        }

        private async Task DisableTaskForDuration(TimeSpan duration)
        {
            if (_selectedTask == null) return;

            try
            {
                // Disable the task
                _taskService.DisableTask(_selectedTask.Path);
                _selectedTask.IsEnabled = false;
                _selectedTask.State = "Disabled";

                // Calculate re-enable time
                var reEnableTime = DateTime.Now.Add(duration);

                // Cancel existing timer if any
                if (_disabledTasks.ContainsKey(_selectedTask.Path))
                {
                    _disabledTasks[_selectedTask.Path].Timer.Stop();
                    _disabledTasks.Remove(_selectedTask.Path);
                }

                // Create timer to re-enable
                var timer = _dispatcherQueue.CreateTimer();
                timer.Interval = duration;
                timer.IsRepeating = false;

                var taskPath = _selectedTask.Path; // Capture for closure
                timer.Tick += (s, args) =>
                {
                    try
                    {
                        _taskService.EnableTask(taskPath);
                        _disabledTasks.Remove(taskPath);
                        LoadTasks(); // Refresh list
                    }
                    catch { }
                };

                _disabledTasks[_selectedTask.Path] = (reEnableTime, timer);
                timer.Start();

                // Show confirmation
                try { TaskDetailsDialog.Hide(); } catch {}
                var confirmDialog = new ContentDialog
                {
                    Title = "Task Disabled",
                    Content = $"'{_selectedTask.Name}' will be re-enabled at {reEnableTime:HH:mm}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await confirmDialog.ShowAsync();

                LoadTasks();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = $"Failed to disable task: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }
        
        private string ParseRestartInterval()
        {
            // Parse interval from simple text like "1 minute", "5m", "2h"
            var text = EditTaskRestartInterval.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return "PT1M";
            
            // Try to parse direct duration format first (e.g., "PT5M")
            if (text.StartsWith("PT") || text.StartsWith("P"))
                return text;
            
            // Simple parsing for common formats
            if (int.TryParse(new string(text.Where(char.IsDigit).ToArray()), out var value))
            {
                var lowerText = text.ToLower();
                if (lowerText.Contains("h"))
                    return $"PT{value}H";
                if (lowerText.Contains("m") || lowerText.Contains("min"))
                    return $"PT{value}M";
                if (lowerText.Contains("s") || lowerText.Contains("sec"))
                    return $"PT{value}S";
            }
            
            return "PT1M"; // Default fallback
        }

        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTriggerPanelVisibility();
        }

        private void UpdateTriggerPanelVisibility()
        {
            if (PanelDaily == null || PanelWeekly == null || PanelMonthly == null) return;
            // PanelStartTime might be null if XAML hasn't processed it yet (though usually it is)
            
            var tag = (EditTaskTriggerType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            PanelDaily.Visibility = tag == "Daily" ? Visibility.Visible : Visibility.Collapsed;
            PanelWeekly.Visibility = tag == "Weekly" ? Visibility.Visible : Visibility.Collapsed;
            PanelMonthly.Visibility = tag == "Monthly" ? Visibility.Visible : Visibility.Collapsed;
            if (PanelEvent != null) PanelEvent.Visibility = tag == "Event" ? Visibility.Visible : Visibility.Collapsed;
            
            // Hide Start Date/Time for events that don't use it
            if (PanelStartTime != null)
            {
                bool showStart = tag != "Event" && tag != "AtLogon" && tag != "AtStartup";
                PanelStartTime.Visibility = showStart ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        private List<string> GetWeeklyDays()
        {
             var days = new List<string>();
             if (WeeklyMon.IsChecked == true) days.Add("Monday");
             if (WeeklyTue.IsChecked == true) days.Add("Tuesday");
             if (WeeklyWed.IsChecked == true) days.Add("Wednesday");
             if (WeeklyThu.IsChecked == true) days.Add("Thursday");
             if (WeeklyFri.IsChecked == true) days.Add("Friday");
             if (WeeklySat.IsChecked == true) days.Add("Saturday");
             if (WeeklySun.IsChecked == true) days.Add("Sunday");
             return days;
        }

        private List<string> GetSelectedMonths()
        {
            var months = new List<string>();
            if (MonthJan.IsChecked == true) months.Add("January");
            if (MonthFeb.IsChecked == true) months.Add("February");
            if (MonthMar.IsChecked == true) months.Add("March");
            if (MonthApr.IsChecked == true) months.Add("April");
            if (MonthMay.IsChecked == true) months.Add("May");
            if (MonthJun.IsChecked == true) months.Add("June");
            if (MonthJul.IsChecked == true) months.Add("July");
            if (MonthAug.IsChecked == true) months.Add("August");
            if (MonthSep.IsChecked == true) months.Add("September");
            if (MonthOct.IsChecked == true) months.Add("October");
            if (MonthNov.IsChecked == true) months.Add("November");
            if (MonthDec.IsChecked == true) months.Add("December");
            return months;
        }
        
        private void SetMonthChecks(List<string> months)
        {
            MonthJan.IsChecked = months.Contains("January");
            MonthFeb.IsChecked = months.Contains("February");
            MonthMar.IsChecked = months.Contains("March");
            MonthApr.IsChecked = months.Contains("April");
            MonthMay.IsChecked = months.Contains("May");
            MonthJun.IsChecked = months.Contains("June");
            MonthJul.IsChecked = months.Contains("July");
            MonthAug.IsChecked = months.Contains("August");
            MonthSep.IsChecked = months.Contains("September");
            MonthOct.IsChecked = months.Contains("October");
            MonthNov.IsChecked = months.Contains("November");
            MonthDec.IsChecked = months.Contains("December");
        }
        
        private List<int> ParseMonthlyDays(string input)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(input)) return list;
            
            var parts = input.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Equals("Last", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(32);
                }
                else if (int.TryParse(trimmed, out int day) && day >= 1 && day <= 31)
                {
                    list.Add(day);
                }
            }
            return list;
        }

        private void SetComboBoxText(ComboBox combo, string text)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Content.ToString() == text)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private void DialogScrollViewer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Prevent the event from bubbling up which would cause focus to reset
            e.Handled = true;
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            // Simplest possible test - just show the dialog
            TaskHistoryDialog.XamlRoot = this.XamlRoot;
            HistoryTaskInfo.Text = "TEST: Button was clicked!";
            HistoryListView.ItemsSource = new List<TaskHistoryEntry>
            {
                new TaskHistoryEntry { Time = "Test", Result = "Test", ExitCode = "0", Message = "This is a test entry" }
            };
            await TaskHistoryDialog.ShowAsync();
        }
    }
}
