using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.UI;

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

    /// <summary>One hour-of-day cell in the 24-hour execution heatmap.</summary>
    public class HeatmapCell
    {
        public int Hour { get; set; }
        public string HourLabel => Hour.ToString("00");
        public int Runs { get; set; }

        /// <summary>0..1 relative to the busiest hour — drives the cell's fill opacity.</summary>
        public double Intensity { get; set; }

        /// <summary>Keeps an hour with at least one run visible even when it is far from the peak.</summary>
        public double FillOpacity => Runs == 0 ? 0.06 : 0.20 + (Intensity * 0.80);

        public double CountOpacity => Runs == 0 ? 0.25 : 0.9;
        public string Tooltip { get; set; } = "";
    }

    /// <summary>A row in the real-time execution log stream.</summary>
    public class ExecutionLogEntry
    {
        public DateTime Time { get; set; }
        public string TimeText => Time.ToString("MM-dd HH:mm:ss");
        public string TaskName { get; set; } = "";
        public string TaskPath { get; set; } = "";

        /// <summary>"Success", "Failed" or "Snoozed".</summary>
        public string Status { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string Detail { get; set; } = "";

        public string Glyph => Status switch
        {
            "Success" => "\uE73E",
            "Failed" => "\uE711",
            _ => "\uE769"
        };

        public Brush StatusBrush => new SolidColorBrush(Status switch
        {
            "Success" => Color.FromArgb(255, 60, 160, 90),
            "Failed" => Color.FromArgb(255, 200, 60, 60),
            _ => Color.FromArgb(255, 202, 128, 0)
        });
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
        private readonly ITaskService _taskService;
        private int _totalTasks;
        private int _enabledTasks;
        private int _disabledTasks;
        private int _lastRunSuccess;
        private int _lastRunFailed;
        private int _healthScore;
        private bool _isLoading;
        private int _runningTasks;
        private DispatcherQueueTimer? _autoRefreshTimer;

        // Captured once here, on the UI thread that always constructs this view model (from
        // DashboardPage's constructor) — calling DispatcherQueue.GetForCurrentThread() again inside
        // LoadDashboardData is null when that method is entered from a non-UI thread (e.g. a
        // background LanguageChanged notification), which crashes the finally block (see 3.6).
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Analytics
        private int _runs24h;
        private int _runs7d;
        private int _successRate = 100;
        private string _averageDurationText = "—";
        private string _longestTaskName = "—";
        private string _longestTaskDuration = "";
        private int _peakHourRuns;
        private string _peakHourLabel = "—";
        private string _executionFilter = "All";
        private List<ExecutionLogEntry> _allExecutionEntries = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardViewModel()
        {
            _taskService = App.Container.GetRequiredService<ITaskService>();
            RecentHistory = new ObservableCollection<TaskHistoryEntry>();
            UpcomingTasks = new ObservableCollection<ScheduledTaskModel>();
            DailyHistory = new ObservableCollection<DailyChartPoint>();
            RunningTasksList = new ObservableCollection<RunningTaskInfo>();
            FailedTasksList = new ObservableCollection<FailedTaskInfo>();
            AvailableTags = new ObservableCollection<FilterItem>();
            AvailableCategories = new ObservableCollection<FilterItem>();
            Heatmap = new ObservableCollection<HeatmapCell>();
            ExecutionLog = new ObservableCollection<ExecutionLogEntry>();

            RefreshFilterLabels();

            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            RefreshFilterLabels();
            _ = LoadDashboardData();
        }

        /// <summary>
        /// Re-arms the static LocalizationService subscription after <see cref="Cleanup"/>, and
        /// re-syncs the filter labels with the current language. Called every time the (cached)
        /// dashboard page is shown, so a language switch that happened while it was off-screen is
        /// picked up instead of leaving the "All tags"/"All categories" selection stranded in the
        /// previous locale — where it matched no chip and zeroed out every statistic.
        /// Idempotent: safe to call when already subscribed.
        /// </summary>
        public void Resume()
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            RefreshFilterLabels();
        }

        /// <summary>Unsubscribes from the static LocalizationService event — see 3.3.</summary>
        public void Cleanup()
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            StopAutoRefresh();
        }

        private void RefreshFilterLabels()
        {
            // Re-point SelectedTag/Category at the new locale's "All" label if that's what was
            // selected, tracked via a language-independent flag rather than comparing against a
            // hardcoded list of translated "All" strings (which silently breaks for any language
            // not in that list - e.g. ja-JP zeroed out the whole dashboard until re-clicked).
            if (string.IsNullOrEmpty(_selectedTag) || _selectedTagIsAll)
            {
                _selectedTag = AllTagsLabel;
            }
            if (string.IsNullOrEmpty(_selectedCategory) || _selectedCategoryIsAll)
            {
                _selectedCategory = AllCategoriesLabel;
            }

            OnPropertyChanged(nameof(SelectedTag));
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(ExitCodePrefix));
        }

        public int RunningTasks
        {
            get => _runningTasks;
            set { _runningTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunningTasks)); OnPropertyChanged(nameof(NoRunningTasksVisible)); }
        }

        public bool HasRunningTasks => _runningTasks > 0;
        public Visibility NoRunningTasksVisible => _runningTasks == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoFailedTasksVisible => FailedTasksList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoExecutionEntriesVisible => ExecutionLog.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            set { _healthScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(HealthScoreText)); HealthScoreChanged?.Invoke(this, EventArgs.Empty); }
        }

        public string HealthScoreText => $"{_healthScore}%";

        /// <summary>Raised when the ring needs redrawing (the arc geometry is built in the page).</summary>
        public event EventHandler? HealthScoreChanged;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentVisible)); }
        }

        /// <summary>Hides all figures while a load is in flight, so stale/zeroed numbers never flash on screen.</summary>
        public Visibility ContentVisible => IsLoading ? Visibility.Collapsed : Visibility.Visible;

        // ── Analytics surface ───────────────────────────────────────────────────

        public int Runs24h
        {
            get => _runs24h;
            set { _runs24h = value; OnPropertyChanged(); }
        }

        public int Runs7d
        {
            get => _runs7d;
            set { _runs7d = value; OnPropertyChanged(); }
        }

        public int SuccessRate
        {
            get => _successRate;
            set { _successRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(SuccessRateText)); }
        }

        public string SuccessRateText => $"{_successRate}%";

        public string AverageDurationText
        {
            get => _averageDurationText;
            set { _averageDurationText = value; OnPropertyChanged(); }
        }

        public string LongestTaskName
        {
            get => _longestTaskName;
            set { _longestTaskName = value; OnPropertyChanged(); }
        }

        public string LongestTaskDuration
        {
            get => _longestTaskDuration;
            set { _longestTaskDuration = value; OnPropertyChanged(); }
        }

        public int PeakHourRuns
        {
            get => _peakHourRuns;
            set { _peakHourRuns = value; OnPropertyChanged(); }
        }

        public string PeakHourLabel
        {
            get => _peakHourLabel;
            set { _peakHourLabel = value; OnPropertyChanged(); }
        }

        /// <summary>"All", "Success", "Failed" or "Snoozed".</summary>
        public string ExecutionFilter
        {
            get => _executionFilter;
            set
            {
                if (_executionFilter == value) return;
                _executionFilter = value;
                OnPropertyChanged();
                ApplyExecutionFilter();
            }
        }

        public ObservableCollection<TaskHistoryEntry> RecentHistory { get; }
        public ObservableCollection<ScheduledTaskModel> UpcomingTasks { get; }
        public ObservableCollection<DailyChartPoint> DailyHistory { get; }
        public ObservableCollection<RunningTaskInfo> RunningTasksList { get; }
        public ObservableCollection<FailedTaskInfo> FailedTasksList { get; }
        public ObservableCollection<FilterItem> AvailableTags { get; }
        public ObservableCollection<FilterItem> AvailableCategories { get; }
        public ObservableCollection<HeatmapCell> Heatmap { get; }
        public ObservableCollection<ExecutionLogEntry> ExecutionLog { get; }

        public string AllTagsLabel => LocalizationService.GetString("Dashboard.AllTags", "All Tags");
        public string AllCategoriesLabel => LocalizationService.GetString("Dashboard.AllCategories", "All Categories");
        public string ExitCodePrefix => LocalizationService.GetString("DashboardExitCode.Text", "Exit Code: ");

        private string _selectedTag = LocalizationService.GetString("Dashboard.AllTags", "All Tags");
        private bool _selectedTagIsAll = true;
        public string SelectedTag
        {
            get => _selectedTag;
            set
            {
                if (_selectedTag != value)
                {
                    _selectedTagIsAll = value == null || value == AllTagsLabel;
                    _selectedTag = value ?? AllTagsLabel;
                    OnPropertyChanged();

                    foreach (var tag in AvailableTags) tag.IsSelected = (tag.Name == _selectedTag);
                    _ = LoadDashboardData();
                }
            }
        }

        private string _selectedCategory = LocalizationService.GetString("Dashboard.AllCategories", "All Categories");
        private bool _selectedCategoryIsAll = true;
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategoryIsAll = value == null || value == AllCategoriesLabel;
                    _selectedCategory = value ?? AllCategoriesLabel;
                    OnPropertyChanged();

                    foreach (var cat in AvailableCategories) cat.IsSelected = (cat.Name == _selectedCategory);
                    _ = LoadDashboardData();
                }
            }
        }

        public async Task LoadDashboardData()
        {
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
                    bool isFiltered = false;
                    if (!string.IsNullOrEmpty(SelectedTag) && SelectedTag != AllTagsLabel)
                    {
                        allTasks = allTasks.Where(t => t.Tags != null && t.Tags.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase)).ToList();
                        isFiltered = true;
                    }
                    if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != AllCategoriesLabel)
                    {
                        allTasks = allTasks.Where(t => string.Equals(t.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase)).ToList();
                        isFiltered = true;
                    }

                    // 4. Calculate Counts
                    int total = allTasks.Count;
                    int enabled = allTasks.Count(t => t.IsEnabled);
                    int disabled = allTasks.Count(t => !t.IsEnabled);

                    // 5. Currently Running Tasks with process detection
                    var runningTasks = allTasks.Where(t => t.State == TaskState.Running).ToList();
                    var runningInfos = BuildRunningTaskInfos(runningTasks);

                    // 6. One bulk read of the operational log powers every analytic below.
                    var records = _taskService.GetRecentRunRecords(TimeSpan.FromDays(7));
                    if (isFiltered)
                    {
                        var allowed = new HashSet<string>(allTasks.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);
                        records = records.Where(r => allowed.Contains(r.TaskPath)).ToList();
                    }

                    var analytics = ComputeAnalytics(records, allTasks);

                    // Update UI
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        TotalTasks = total;
                        EnabledTasks = enabled;
                        DisabledTasks = disabled;
                        RunningTasks = runningTasks.Count;
                        LastRunSuccess = analytics.SuccessCount;
                        LastRunFailed = analytics.FailureCount;
                        HealthScore = analytics.HealthScore;
                        SuccessRate = analytics.SuccessRate;
                        Runs24h = analytics.Runs24h;
                        Runs7d = analytics.Runs7d;
                        AverageDurationText = analytics.AverageDurationText;
                        LongestTaskName = analytics.LongestTaskName;
                        LongestTaskDuration = analytics.LongestTaskDuration;
                        PeakHourRuns = analytics.PeakHourRuns;
                        PeakHourLabel = analytics.PeakHourLabel;

                        RunningTasksList.Clear();
                        foreach (var r in runningInfos)
                            RunningTasksList.Add(r);

                        FailedTasksList.Clear();
                        foreach (var f in analytics.FailedTasks)
                            FailedTasksList.Add(f);
                        OnPropertyChanged(nameof(NoFailedTasksVisible));

                        RecentHistory.Clear();
                        foreach (var h in analytics.RecentHistory)
                            RecentHistory.Add(h);

                        UpcomingTasks.Clear();
                        foreach (var u in analytics.Upcoming)
                            UpcomingTasks.Add(u);

                        DailyHistory.Clear();
                        foreach (var p in analytics.ChartPoints)
                            DailyHistory.Add(p);

                        Heatmap.Clear();
                        foreach (var c in analytics.Heatmap)
                            Heatmap.Add(c);

                        _allExecutionEntries = analytics.ExecutionEntries;
                        ApplyExecutionFilter();

                        // Update available tags
                        var currentTags = AvailableTags.Select(t => t.Name).ToList();
                        var newTags = new List<string> { AllTagsLabel };
                        newTags.AddRange(uniqueTags);

                        if (!currentTags.SequenceEqual(newTags))
                        {
                            AvailableTags.Clear();
                            foreach (var tag in newTags)
                                AvailableTags.Add(new FilterItem { Name = tag, IsSelected = (tag == SelectedTag) });
                        }

                        // Update available categories
                        var currentCats = AvailableCategories.Select(t => t.Name).ToList();
                        var newCats = new List<string> { AllCategoriesLabel };
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
                Serilog.Log.Error(ex, "{Message}", "Dashboard data load failed.");
            }
            finally
            {
                _dispatcherQueue.TryEnqueue(() => IsLoading = false);
            }
        }

        // ── Analytics computation ───────────────────────────────────────────────

        private sealed class AnalyticsResult
        {
            public int SuccessCount;
            public int FailureCount;
            public int HealthScore = 100;
            public int SuccessRate = 100;
            public int Runs24h;
            public int Runs7d;
            public string AverageDurationText = "—";
            public string LongestTaskName = "—";
            public string LongestTaskDuration = "";
            public int PeakHourRuns;
            public string PeakHourLabel = "—";
            public List<HeatmapCell> Heatmap = new();
            public List<DailyChartPoint> ChartPoints = new();
            public List<ExecutionLogEntry> ExecutionEntries = new();
            public List<FailedTaskInfo> FailedTasks = new();
            public List<TaskHistoryEntry> RecentHistory = new();
            public List<ScheduledTaskModel> Upcoming = new();
        }

        private AnalyticsResult ComputeAnalytics(List<TaskRunRecord> records, List<ScheduledTaskModel> tasks)
        {
            var result = new AnalyticsResult();
            var now = DateTime.Now;
            var today = DateTime.Today;

            var starts = records.Where(r => r.IsStart).ToList();
            var outcomes = records.Where(r => r.IsSuccess || r.IsFailure).ToList();

            result.Runs7d = starts.Count;
            result.Runs24h = starts.Count(r => r.Time >= now.AddHours(-24));
            result.SuccessCount = outcomes.Count(r => r.IsSuccess);
            result.FailureCount = outcomes.Count(r => r.IsFailure);

            int totalOutcomes = result.SuccessCount + result.FailureCount;
            result.SuccessRate = totalOutcomes == 0 ? 100 : (int)Math.Round(result.SuccessCount * 100.0 / totalOutcomes);
            // Health = success rate, but a system with no data at all is reported as healthy rather than 0.
            result.HealthScore = result.SuccessRate;

            // Heatmap: aggregate the 7-day window by hour-of-day.
            var byHour = new int[24];
            foreach (var s in starts) byHour[s.Time.Hour]++;
            int maxHour = byHour.Max();
            for (int h = 0; h < 24; h++)
            {
                result.Heatmap.Add(new HeatmapCell
                {
                    Hour = h,
                    Runs = byHour[h],
                    Intensity = maxHour == 0 ? 0 : byHour[h] / (double)maxHour,
                    Tooltip = string.Format(
                        LocalizationService.GetString("Dashboard.Heatmap.CellTooltip", "{0}:00 – {1} run(s) in the last 7 days"),
                        h.ToString("00"), byHour[h])
                });
            }
            if (maxHour > 0)
            {
                int peak = Array.IndexOf(byHour, maxHour);
                result.PeakHourRuns = maxHour;
                result.PeakHourLabel = $"{peak:00}:00";
            }

            // Durations: pair a start with its completion via the task-scheduler instance id.
            var durations = new List<(string Task, TimeSpan Duration)>();
            foreach (var group in records.Where(r => !string.IsNullOrEmpty(r.InstanceId))
                                         .GroupBy(r => r.TaskPath + "|" + r.InstanceId))
            {
                var start = group.Where(r => r.IsStart).OrderBy(r => r.Time).FirstOrDefault();
                var end = group.Where(r => r.IsCompletion || r.IsActionResult).OrderByDescending(r => r.Time).FirstOrDefault();
                if (start == null || end == null) continue;

                var span = end.Time - start.Time;
                // Ignore impossible pairings (clock changes) and instances that clearly span log gaps.
                if (span <= TimeSpan.Zero || span > TimeSpan.FromHours(24)) continue;
                durations.Add((start.TaskName, span));
            }

            if (durations.Count > 0)
            {
                var avg = TimeSpan.FromTicks((long)durations.Average(d => d.Duration.Ticks));
                result.AverageDurationText = FormatDuration(avg);

                var longest = durations.OrderByDescending(d => d.Duration).First();
                result.LongestTaskName = longest.Task;
                result.LongestTaskDuration = FormatDuration(longest.Duration);
            }

            // 7-day success/failure bar chart, oldest first.
            var chartPoints = Enumerable.Range(0, 7)
                .Select(i => today.AddDays(-6 + i))
                .Select(day => new DailyChartPoint
                {
                    Label = day == today ? LocalizationService.GetString("Dashboard.Today", "Today") : day.ToString("ddd"),
                    Successes = outcomes.Count(o => o.IsSuccess && o.Time.Date == day),
                    Failures = outcomes.Count(o => o.IsFailure && o.Time.Date == day),
                    LabelOpacity = day == today ? 1.0 : 0.6
                }).ToList();

            const double MaxBarHeight = 100.0;
            int maxVal = Math.Max(1, chartPoints.Max(p => Math.Max(p.Successes, p.Failures)));
            foreach (var p in chartPoints)
            {
                p.SuccessHeight = (p.Successes / (double)maxVal) * MaxBarHeight;
                p.FailureHeight = (p.Failures / (double)maxVal) * MaxBarHeight;
            }
            result.ChartPoints = chartPoints;

            // Execution log stream — outcomes plus snooze-suppressed runs, newest first.
            var entries = outcomes
                .OrderByDescending(o => o.Time)
                .Take(200)
                .Select(o => new ExecutionLogEntry
                {
                    Time = o.Time,
                    TaskName = o.TaskName,
                    TaskPath = o.TaskPath,
                    Status = o.IsSuccess ? "Success" : "Failed",
                    StatusText = o.IsSuccess
                        ? LocalizationService.GetString("Dashboard.Status.Success", "Success")
                        : LocalizationService.GetString("Dashboard.Status.Failed", "Failed"),
                    Detail = o.IsLaunchFailure
                        ? LocalizationService.GetString("Dashboard.Status.LaunchFailed", "Task Scheduler could not launch the action")
                        : string.Format(LocalizationService.GetString("Dashboard.Status.ExitCode", "Exit code {0}"),
                                        o.ExitCodeText)
                })
                .ToList();

            var cutoff = now.AddDays(-7);
            entries.AddRange(SnoozeService.SuppressedRuns
                .Where(s => s.TimeUtc.ToLocalTime() >= cutoff)
                .Select(s => new ExecutionLogEntry
                {
                    Time = s.TimeUtc.ToLocalTime(),
                    TaskName = s.TaskName,
                    TaskPath = s.TaskPath,
                    Status = "Snoozed",
                    StatusText = LocalizationService.GetString("Dashboard.Status.Snoozed", "Snoozed"),
                    Detail = string.Format(
                        LocalizationService.GetString("Dashboard.Status.SuppressedOrigin", "Suppressed ({0})"), s.Origin)
                }));

            result.ExecutionEntries = entries.OrderByDescending(e => e.Time).Take(200).ToList();

            // Recently failed tasks card — one row per task, most recent failure first.
            result.FailedTasks = outcomes
                .Where(o => o.IsFailure)
                .GroupBy(o => o.TaskPath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(o => o.Time).First())
                .OrderByDescending(o => o.Time)
                .Take(10)
                .Select(o => new FailedTaskInfo
                {
                    Name = o.TaskName,
                    Path = o.TaskPath,
                    LastRunTime = o.Time.ToString("g"),
                    ErrorMessage = o.IsLaunchFailure
                        ? LocalizationService.GetString("Dashboard.Status.LaunchFailed", "Task Scheduler could not launch the action")
                        : string.Format(LocalizationService.GetString("Dashboard.Status.ExitCode", "Exit code {0}"),
                                        o.ExitCodeText),
                    ExitCode = o.ExitCodeText
                })
                .ToList();

            // Activity stream (legacy card) — most recent records of any kind.
            result.RecentHistory = records
                .OrderByDescending(r => r.Time)
                .Take(15)
                .Select(r => new TaskHistoryEntry
                {
                    Time = r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    Result = $"{r.TaskName} — {DescribeEvent(r)}",
                    ExitCode = r.ResultCode.HasValue ? r.ExitCodeText : "",
                    TaskPath = r.TaskPath,
                    TaskName = r.TaskName,
                    EventId = r.EventId
                })
                .ToList();

            result.Upcoming = tasks.Where(t => t.NextRunTime.HasValue && t.IsEnabled)
                                   .OrderBy(t => t.NextRunTime)
                                   .Take(5)
                                   .ToList();

            return result;
        }

        private static string DescribeEvent(TaskRunRecord r) => r.EventId switch
        {
            100 => LocalizationService.GetString("Dashboard.Event.Started", "Started"),
            102 => LocalizationService.GetString("Dashboard.Event.Finished", "Finished"),
            103 => LocalizationService.GetString("Dashboard.Event.StartFailed", "Failed to start"),
            201 => r.ResultCode.GetValueOrDefault() == 0
                    ? LocalizationService.GetString("Dashboard.Status.Success", "Success")
                    : string.Format(LocalizationService.GetString("Dashboard.Status.ExitCode", "Exit code {0}"), r.ExitCodeText),
            203 => LocalizationService.GetString("Dashboard.Event.LaunchFailed", "Action launch failed"),
            _ => $"Event {r.EventId}"
        };

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
            return $"{span.TotalSeconds:0.#}s";
        }

        private List<RunningTaskInfo> BuildRunningTaskInfos(List<ScheduledTaskModel> runningTasks)
        {
            var runningInfos = new List<RunningTaskInfo>();
            var enginePids = runningTasks.Count > 0 ? _taskService.GetRunningTaskEnginePids() : new Dictionary<string, int>();

            foreach (var task in runningTasks)
            {
                var actionCmd = task.ActionCommand;
                var processName = !string.IsNullOrEmpty(actionCmd) ? System.IO.Path.GetFileNameWithoutExtension(actionCmd.Trim('"')) : "";
                var processAlive = false;
                var processStatus = LocalizationService.GetString("Dashboard.Unknown", "Unknown");

                // Identified via the task's actual engine PID (Task Scheduler's own bookkeeping),
                // not by matching any process sharing the action's image name — a name match like
                // "powershell.exe" can hit a completely unrelated process on the machine.
                if (task.Path != null && enginePids.TryGetValue(task.Path, out int pid))
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        processAlive = true;
                        processStatus = string.Format(LocalizationService.GetString("Dashboard.ProcessActive", "Process active (PID {0})"), pid);
                    }
                    catch (ArgumentException)
                    {
                        // No process with that PID — the engine already exited.
                        processStatus = LocalizationService.GetString("Dashboard.ProcessNotFound", "Process not found");
                    }
                    catch (Exception ex)
                    {
                        processStatus = LocalizationService.GetString("Dashboard.ProcessUnableToCheck", "Unable to check");
                        Serilog.Log.Warning("{Message}", $"Could not inspect PID {pid} for task '{task.Path}': {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(actionCmd))
                {
                    processStatus = LocalizationService.GetString("Dashboard.ProcessNotFound", "Process not found");
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
                    Path = task.Path ?? "",
                    ActionCommand = string.IsNullOrEmpty(processName) ? actionCmd : processName,
                    RunningDuration = duration,
                    ProcessAlive = processAlive,
                    ProcessStatus = processStatus
                });
            }
            return runningInfos;
        }

        private void ApplyExecutionFilter()
        {
            ExecutionLog.Clear();
            IEnumerable<ExecutionLogEntry> source = _allExecutionEntries;

            if (!string.Equals(_executionFilter, "All", StringComparison.OrdinalIgnoreCase))
                source = source.Where(e => string.Equals(e.Status, _executionFilter, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in source.Take(100))
                ExecutionLog.Add(entry);

            OnPropertyChanged(nameof(NoExecutionEntriesVisible));
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
