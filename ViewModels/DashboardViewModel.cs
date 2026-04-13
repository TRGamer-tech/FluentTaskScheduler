using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;

namespace FluentTaskScheduler.ViewModels
{
    public class DailyChartPoint
    {
        public string Label { get; set; } = "";
        public int Successes { get; set; }
        public int Failures { get; set; }
        public double SuccessHeight { get; set; }
        public double FailureHeight { get; set; }
        public double LabelOpacity { get; set; } = 0.6;
    }

    public class FilterItem : INotifyPropertyChanged
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RunningTaskInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ActionCommand { get; set; } = "";
        public string RunningDuration { get; set; } = "";
        public bool ProcessAlive { get; set; }
        public string ProcessStatus { get; set; } = "";
    }

    public class FailedTaskInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string LastRunTime { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string ExitCode { get; set; } = "";
    }
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly TaskServiceWrapper _taskService;
        private int _totalTasks;
        private int _enabledTasks;
        private int _disabledTasks;
        private int _lastRunSuccess;
        private int _lastRunFailed;
        private int _healthScore;
        private bool _isLoading;
        private int _runningTasks;
        private DispatcherQueueTimer? _autoRefreshTimer;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardViewModel()
        {
            _taskService = new TaskServiceWrapper();
            RecentHistory = new ObservableCollection<TaskHistoryEntry>();
            UpcomingTasks = new ObservableCollection<ScheduledTaskModel>();
            DailyHistory = new ObservableCollection<DailyChartPoint>();
            RunningTasksList = new ObservableCollection<RunningTaskInfo>();
            FailedTasksList = new ObservableCollection<FailedTaskInfo>();
            AvailableTags = new ObservableCollection<FilterItem> { new FilterItem { Name = "All Tags", IsSelected = true } };
            _selectedTag = "All Tags";
            AvailableCategories = new ObservableCollection<FilterItem> { new FilterItem { Name = "All Categories", IsSelected = true } };
            _selectedCategory = "All Categories";
        }

        public int RunningTasks
        {
            get => _runningTasks;
            set { _runningTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunningTasks)); OnPropertyChanged(nameof(NoRunningTasksVisible)); }
        }

        public bool HasRunningTasks => _runningTasks > 0;
        public Visibility NoRunningTasksVisible => _runningTasks == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoFailedTasksVisible => FailedTasksList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public int TotalTasks
        {
            get => _totalTasks;
            set { _totalTasks = value; OnPropertyChanged(); }
        }

        public int EnabledTasks
        {
            get => _enabledTasks;
            set { _enabledTasks = value; OnPropertyChanged(); }
        }

        public int DisabledTasks
        {
            get => _disabledTasks;
            set { _disabledTasks = value; OnPropertyChanged(); }
        }

        public int LastRunSuccess
        {
            get => _lastRunSuccess;
            set { _lastRunSuccess = value; OnPropertyChanged(); }
        }

        public int LastRunFailed
        {
            get => _lastRunFailed;
            set { _lastRunFailed = value; OnPropertyChanged(); }
        }

        public int HealthScore
        {
            get => _healthScore;
            set { _healthScore = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TaskHistoryEntry> RecentHistory { get; }
        public ObservableCollection<ScheduledTaskModel> UpcomingTasks { get; }
        public ObservableCollection<DailyChartPoint> DailyHistory { get; }
        public ObservableCollection<RunningTaskInfo> RunningTasksList { get; }
        public ObservableCollection<FailedTaskInfo> FailedTasksList { get; }
        public ObservableCollection<FilterItem> AvailableTags { get; }
        public ObservableCollection<FilterItem> AvailableCategories { get; }

        private string _selectedTag = "All Tags";
        public string SelectedTag
        {
            get => _selectedTag;
            set
            {
                if (_selectedTag != value)
                {
                    _selectedTag = value ?? "All Tags";
                    OnPropertyChanged();

                    foreach (var tag in AvailableTags) tag.IsSelected = (tag.Name == _selectedTag);
                    _ = LoadDashboardData();
                }
            }
        }

        private string _selectedCategory = "All Categories";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value ?? "All Categories";
                    OnPropertyChanged();

                    foreach (var cat in AvailableCategories) cat.IsSelected = (cat.Name == _selectedCategory);
                    _ = LoadDashboardData();
                }
            }
        }

        public async Task LoadDashboardData()
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            IsLoading = true;
            try
            {
                await Task.Run(() =>
                {
                    // 1. Get All Tasks
                    var allTasksRaw = _taskService.GetAllTasks(recursive: true);

                    // 2. Extract unique tags and categories
                    var uniqueTags = allTasksRaw
                        .Where(t => t.Tags != null)
                        .SelectMany(t => t.Tags)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t)
                        .ToList();

                    var uniqueCategories = allTasksRaw
                        .Where(t => !string.IsNullOrWhiteSpace(t.Category))
                        .Select(t => t.Category)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t)
                        .ToList();

                    // 3. Filter tasks if needed
                    var allTasks = allTasksRaw;
                    if (!string.IsNullOrEmpty(SelectedTag) && SelectedTag != "All Tags")
                    {
                        allTasks = allTasks.Where(t => t.Tags != null && t.Tags.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase)).ToList();
                    }
                    if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All Categories")
                    {
                        allTasks = allTasks.Where(t => string.Equals(t.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    // 4. Calculate Counts
                    int total = allTasks.Count;
                    int enabled = allTasks.Count(t => t.IsEnabled);
                    int disabled = allTasks.Count(t => !t.IsEnabled);

                    // 3. Currently Running Tasks with process detection
                    var runningTasks = allTasks.Where(t => t.State == "Running").ToList();
                    var runningInfos = new List<RunningTaskInfo>();
                    foreach (var task in runningTasks)
                    {
                        var actionCmd = task.ActionCommand;
                        var processName = "";
                        var processAlive = false;
                        var processStatus = "Unknown";

                        if (!string.IsNullOrEmpty(actionCmd))
                        {
                            // Extract process name from command (e.g. "C:\Python\python.exe" -> "python")
                            processName = System.IO.Path.GetFileNameWithoutExtension(actionCmd.Trim('"'));
                            try
                            {
                                var procs = Process.GetProcessesByName(processName);
                                processAlive = procs.Length > 0;
                                processStatus = processAlive ? $"Process active ({procs.Length} instance{(procs.Length > 1 ? "s" : "")})" : "Process not found";
                            }
                            catch { processStatus = "Unable to check"; }
                        }

                        var duration = "";
                        if (task.LastRunTime.HasValue)
                        {
                            var elapsed = DateTime.Now - task.LastRunTime.Value;
                            if (elapsed.TotalDays >= 1) duration = $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
                            else if (elapsed.TotalHours >= 1) duration = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                            else duration = $"{(int)elapsed.TotalMinutes}m";
                        }

                        runningInfos.Add(new RunningTaskInfo
                        {
                            Name = task.Name,
                            Path = task.Path,
                            ActionCommand = string.IsNullOrEmpty(processName) ? actionCmd : processName,
                            RunningDuration = duration,
                            ProcessAlive = processAlive,
                            ProcessStatus = processStatus
                        });
                    }

                    // 4. Get Recent History + chart data + failed tasks
                    int success = 0;
                    int failed = 0;
                    var historyEntries = new List<TaskHistoryEntry>();
                    var allHistoryForChart = new List<TaskHistoryEntry>();
                    var failedInfos = new List<FailedTaskInfo>();

                    foreach (var task in allTasks.Where(t => t.LastRunTime.HasValue)
                                                  .OrderByDescending(t => t.LastRunTime).Take(20))
                    {
                        var taskHistory = _taskService.GetTaskHistory(task.Path);
                        if (taskHistory.Any())
                        {
                            var last = taskHistory.First();
                            if (last.Result == "Task Completed") success++;
                            else if (last.Result == "Task Failed")
                            {
                                failed++;
                                failedInfos.Add(new FailedTaskInfo
                                {
                                    Name = task.Name,
                                    Path = task.Path,
                                    LastRunTime = task.LastRunTime?.ToString("g") ?? "",
                                    ErrorMessage = last.Message,
                                    ExitCode = last.ExitCode
                                });
                            }
                            historyEntries.AddRange(taskHistory.Take(5));
                        }
                        allHistoryForChart.AddRange(taskHistory);
                    }

                    // 5. Build 7-day chart (last 7 days, oldest first)
                    var today = DateTime.Today;
                    var chartPoints = Enumerable.Range(0, 7)
                        .Select(i => today.AddDays(-6 + i))
                        .Select(day =>
                        {
                            var dayEntries = allHistoryForChart.Where(h =>
                                DateTime.TryParse(h.Time, out var dt) && dt.Date == day);
                            return new DailyChartPoint
                            {
                                Label = day == today ? "Today" : day.ToString("ddd"),
                                Successes = dayEntries.Count(e => e.Result == "Task Completed"),
                                Failures  = dayEntries.Count(e => e.Result != "Task Completed"
                                                                && !string.IsNullOrEmpty(e.Result)),
                                LabelOpacity = day == today ? 1.0 : 0.6
                            };
                        }).ToList();

                    const double MaxBarHeight = 100.0;
                    int maxVal = Math.Max(1, chartPoints.Max(p => Math.Max(p.Successes, p.Failures)));
                    foreach (var p in chartPoints)
                    {
                        p.SuccessHeight = (p.Successes / (double)maxVal) * MaxBarHeight;
                        p.FailureHeight = (p.Failures  / (double)maxVal) * MaxBarHeight;
                    }

                    // 6. Calculate Health Score
                    int score = 100;
                    if (failed > 0) score -= (failed * 10);
                    if (score < 0) score = 0;

                    // 7. Get Upcoming
                    var upcoming = allTasks.Where(t => t.NextRunTime.HasValue && t.IsEnabled)
                                           .OrderBy(t => t.NextRunTime)
                                           .Take(5)
                                           .ToList();

                    // Update UI
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        TotalTasks = total;
                        EnabledTasks = enabled;
                        DisabledTasks = disabled;
                        RunningTasks = runningTasks.Count;
                        LastRunSuccess = success;
                        LastRunFailed = failed;
                        HealthScore = score;

                        RunningTasksList.Clear();
                        foreach (var r in runningInfos)
                            RunningTasksList.Add(r);

                        FailedTasksList.Clear();
                        foreach (var f in failedInfos)
                            FailedTasksList.Add(f);
                        OnPropertyChanged(nameof(NoFailedTasksVisible));

                        RecentHistory.Clear();
                        foreach (var h in historyEntries.OrderByDescending(x => x.Time).Take(10))
                            RecentHistory.Add(h);

                        UpcomingTasks.Clear();
                        foreach (var u in upcoming)
                            UpcomingTasks.Add(u);

                        DailyHistory.Clear();
                        foreach (var p in chartPoints)
                            DailyHistory.Add(p);

                        // Update available tags
                        var currentTags = AvailableTags.Select(t => t.Name).ToList();
                        var newTags = new List<string> { "All Tags" };
                        newTags.AddRange(uniqueTags);

                        if (!currentTags.SequenceEqual(newTags))
                        {
                            AvailableTags.Clear();
                            foreach (var tag in newTags)
                                AvailableTags.Add(new FilterItem { Name = tag, IsSelected = (tag == SelectedTag) });
                        }

                        // Update available categories
                        var currentCats = AvailableCategories.Select(t => t.Name).ToList();
                        var newCats = new List<string> { "All Categories" };
                        newCats.AddRange(uniqueCategories);

                        if (!currentCats.SequenceEqual(newCats))
                        {
                            AvailableCategories.Clear();
                            foreach (var cat in newCats)
                                AvailableCategories.Add(new FilterItem { Name = cat, IsSelected = (cat == SelectedCategory) });
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard Load Error: {ex}");
            }
            finally
            {
                dispatcherQueue.TryEnqueue(() => IsLoading = false);
            }
        }

        public void NavigateToTask(string taskPath)
        {
            if (MainPage.Current != null)
            {
                MainPage.Current.NavigateToTask(taskPath);
            }
        }

        public void StartAutoRefresh()
        {
            if (_autoRefreshTimer != null) return;
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq == null) return;
            _autoRefreshTimer = dq.CreateTimer();
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(30);
            _autoRefreshTimer.Tick += async (s, e) => { await LoadDashboardData(); };
            _autoRefreshTimer.Start();
        }

        public void StopAutoRefresh()
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
