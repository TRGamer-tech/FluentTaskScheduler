using Microsoft.UI.Xaml;
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

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTasks();
        }

        private bool _isLoading = false;

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
                if (LoadingRing != null) LoadingRing.IsActive = true;
                
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
                        query = query.Where(t => t.State != "Disabled");
                    }
                    else if (tag == "disabled")
                    {
                        query = query.Where(t => t.State == "Disabled");
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
            EditTaskRestartInterval.Text = "1";
            EditTaskRestartIntervalUnit.SelectedIndex = 0;
            EditTaskRestartCount.Text = "3";

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
                EditTaskActionCommand.Text = file.Path;
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
                DialogTaskName.Text = task.Name;
                DialogTaskDescription.Text = task.Description;
                DialogTaskAuthor.Text = task.Author;
                
                // Update button states based on task state
                RunTaskButton.IsEnabled = task.IsEnabled;
                
                await TaskDetailsDialog.ShowAsync();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTasks();
        }

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask != null)
            {
                _isEditMode = true;
                
                // Populate dialog with existing task data
                EditTaskName.Text = _selectedTask.Name;
                EditTaskDescription.Text = _selectedTask.Description;
                EditTaskAuthor.Text = _selectedTask.Author;
                EditTaskEnabled.IsOn = _selectedTask.IsEnabled;
                EditTaskActionCommand.Text = _selectedTask.ActionCommand;
                EditTaskArguments.Text = _selectedTask.Arguments;
                EditTaskWorkingDirectory.Text = _selectedTask.WorkingDirectory;
                
                // Parse Start Time/Date from ScheduleInfo if possible, else default
                DateTime start = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(_selectedTask.ScheduleInfo) && DateTime.TryParse(_selectedTask.ScheduleInfo, out var parsedStart))
                {
                    start = parsedStart;
                }
                EditTaskStartDate.Date = start;
                EditTaskStartTime.Time = start.TimeOfDay;

                // Set trigger type
                for (int i = 0; i < EditTaskTriggerType.Items.Count; i++)
                {
                    if ((EditTaskTriggerType.Items[i] as ComboBoxItem)?.Tag?.ToString() == _selectedTask.TriggerType)
                    {
                        EditTaskTriggerType.SelectedIndex = i;
                        break;
                    }
                }
                
                
                // Populate Granular Trigger Details
                DailyInterval.Text = (_selectedTask.DailyInterval > 0 ? _selectedTask.DailyInterval : 1).ToString();
                WeeklyInterval.Text = (_selectedTask.WeeklyInterval > 0 ? _selectedTask.WeeklyInterval : 1).ToString();
                
                // Weekly Days
                WeeklyMon.IsChecked = _selectedTask.WeeklyDays.Contains("Monday");
                WeeklyTue.IsChecked = _selectedTask.WeeklyDays.Contains("Tuesday");
                WeeklyWed.IsChecked = _selectedTask.WeeklyDays.Contains("Wednesday");
                WeeklyThu.IsChecked = _selectedTask.WeeklyDays.Contains("Thursday");
                WeeklyFri.IsChecked = _selectedTask.WeeklyDays.Contains("Friday");
                WeeklySat.IsChecked = _selectedTask.WeeklyDays.Contains("Saturday");
                WeeklySun.IsChecked = _selectedTask.WeeklyDays.Contains("Sunday");

                // Monthly
                SetMonthChecks(_selectedTask.MonthlyMonths);
                if (_selectedTask.MonthlyIsDayOfWeek)
                {
                    MonthlyRadioOn.IsChecked = true;
                    SetComboBoxText(MonthlyWeekCombo, _selectedTask.MonthlyWeek);
                    SetComboBoxText(MonthlyDayCombo, _selectedTask.MonthlyDayOfWeek);
                }
                else
                {
                    MonthlyRadioDays.IsChecked = true;
                    // Format days list to string, e.g. "1, 15, Last"
                    var daysList = _selectedTask.MonthlyDays.Select(d => d == 32 ? "Last" : d.ToString()).ToList();
                    MonthlyDaysInput.Text = string.Join(", ", daysList);
                }

                // Expiration
                if (_selectedTask.ExpirationDate.HasValue)
                {
                    EditTaskExpires.IsChecked = true;
                    EditTaskExpirationDate.Date = _selectedTask.ExpirationDate.Value;
                    EditTaskExpirationTime.Time = _selectedTask.ExpirationDate.Value.TimeOfDay;
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
                if (!string.IsNullOrEmpty(_selectedTask.RandomDelay))
                {
                    EditTaskRandomDelay.IsChecked = true;
                    EditTaskRandomDelayVal.IsEnabled = true;
                    EditTaskRandomDelayVal.Text = _selectedTask.RandomDelay;
                }
                else
                {
                     EditTaskRandomDelay.IsChecked = false;
                     EditTaskRandomDelayVal.IsEnabled = false;
                     EditTaskRandomDelayVal.Text = "";
                }

                // Stop If Runs Longer Than
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
                
                UpdateTriggerPanelVisibility();
                
                // Populate advanced settings
                SetComboBoxByTag(EditTaskRepetitionInterval, _selectedTask.RepetitionInterval);
                SetComboBoxByTag(EditTaskRepetitionDuration, _selectedTask.RepetitionDuration);
                EditTaskOnlyIfIdle.IsChecked = _selectedTask.OnlyIfIdle;
                EditTaskOnlyIfAC.IsChecked = _selectedTask.OnlyIfAC;
                EditTaskOnlyIfNetwork.IsChecked = _selectedTask.OnlyIfNetwork;
                EditTaskWakeToRun.IsChecked = _selectedTask.WakeToRun;
                EditTaskStopOnBattery.IsChecked = _selectedTask.StopOnBattery;
                EditTaskRunIfMissed.IsChecked = _selectedTask.RunIfMissed;
                EditTaskRestartOnFailure.IsChecked = _selectedTask.RestartOnFailure;
                
                TaskDetailsDialog.Hide();
                await TaskEditDialog.ShowAsync();
            }
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

        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        // Safety check for null sender
        if (sender is not ToggleSwitch toggle)
            return;

        // During virtualization, DataContext might be null or not yet set
        if (toggle.DataContext is not ScheduledTaskModel task)
            return;

        // Prevent redundant operations during data binding/virtualization
        if (task.IsEnabled == toggle.IsOn)
            return;

        try
        {
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
            
            // Update the model without reloading entire list
            task.IsEnabled = toggle.IsOn;
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
                var triggerTag = (EditTaskTriggerType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
                
                // Construct Start DateTime
                // Construct Start DateTime
                var startDate = EditTaskStartDate.Date.Date;
                var startTime = EditTaskStartTime.Time;
                var startDateTime = startDate + startTime;
                
                var newTask = new ScheduledTaskModel
                {
                    Name = EditTaskName.Text,
                    Description = EditTaskDescription.Text,
                    Author = EditTaskAuthor.Text,
                    IsEnabled = EditTaskEnabled.IsOn,
                    ActionCommand = EditTaskActionCommand.Text,
                    Arguments = EditTaskArguments.Text,
                    WorkingDirectory = EditTaskWorkingDirectory.Text,
                    ScheduleInfo = startDateTime.ToString("yyyy-MM-dd HH:mm:ss"), // Store full start time
                    TriggerType = triggerTag,
                    
                    // Trigger Specifics
                    DailyInterval = short.TryParse(DailyInterval.Text, out var daily) ? daily : (short)1,
                    WeeklyInterval = short.TryParse(WeeklyInterval.Text, out var weekly) ? weekly : (short)1,
                    WeeklyDays = GetWeeklyDays(),
                    
                    MonthlyIsDayOfWeek = MonthlyRadioOn.IsChecked == true,
                    MonthlyMonths = GetSelectedMonths(),
                    MonthlyDays = ParseMonthlyDays(MonthlyDaysInput.Text),
                    MonthlyWeek = (MonthlyWeekCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "First",
                    MonthlyDayOfWeek = (MonthlyDayCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Monday",

                    ExpirationDate = EditTaskExpires.IsChecked == true 
                        ? EditTaskExpirationDate.Date.Date + EditTaskExpirationTime.Time 
                        : null,
                    
                    RandomDelay = EditTaskRandomDelay.IsChecked == true ? EditTaskRandomDelayVal.Text : "",
                    
                    // Advanced settings
                    RepetitionInterval = (EditTaskRepetitionInterval.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
                    RepetitionDuration = (EditTaskRepetitionDuration.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
                    OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked ?? false,
                    OnlyIfAC = EditTaskOnlyIfAC.IsChecked ?? false,
                    OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked ?? false,
                    WakeToRun = EditTaskWakeToRun.IsChecked ?? false,
                    StopOnBattery = EditTaskStopOnBattery.IsChecked ?? false,
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
        
        private string ParseRestartInterval()
        {
            if (!int.TryParse(EditTaskRestartInterval.Text, out var value) || value <= 0)
                return "PT1M";
                
            var unit = (EditTaskRestartIntervalUnit.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "M";
            return unit == "H" ? $"PT{value}H" : $"PT{value}M";
        }

        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTriggerPanelVisibility();
        }

        private void UpdateTriggerPanelVisibility()
        {
            if (PanelDaily == null || PanelWeekly == null || PanelMonthly == null) return;

            var tag = (EditTaskTriggerType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            PanelDaily.Visibility = tag == "Daily" ? Visibility.Visible : Visibility.Collapsed;
            PanelWeekly.Visibility = tag == "Weekly" ? Visibility.Visible : Visibility.Collapsed;
            PanelMonthly.Visibility = tag == "Monthly" ? Visibility.Visible : Visibility.Collapsed;
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
    }
}
