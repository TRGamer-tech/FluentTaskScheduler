using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;

namespace FluentTaskScheduler.ViewModels
{
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

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardViewModel()
        {
            _taskService = new TaskServiceWrapper();
            RecentHistory = new ObservableCollection<TaskHistoryEntry>();
            UpcomingTasks = new ObservableCollection<ScheduledTaskModel>();
        }

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

        public async Task LoadDashboardData()
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            IsLoading = true;
            try
            {
                await Task.Run(() =>
                {
                    // 1. Get All Tasks
                    var allTasks = _taskService.GetAllTasks(recursive: true);

                    // 2. Calculate Counts
                    int total = allTasks.Count;
                    int enabled = allTasks.Count(t => t.IsEnabled);
                    int disabled = allTasks.Count(t => !t.IsEnabled);

                    // 3. Get Recent History
                    int success = 0;
                    int failed = 0;
                    
                    var historyEntries = new System.Collections.Generic.List<TaskHistoryEntry>();

                    foreach (var task in allTasks.Where(t => t.LastRunTime.HasValue).OrderByDescending(t => t.LastRunTime).Take(10))
                    {
                        var taskHistory = _taskService.GetTaskHistory(task.Path);
                        if (taskHistory.Any())
                        {
                            var last = taskHistory.First();
                            if (last.Result == "Task Completed") success++;
                            else if (last.Result == "Task Failed") failed++;
                            
                            historyEntries.AddRange(taskHistory.Take(5));
                        }
                    }

                    // 4. Calculate Health Score
                    int score = 100;
                    if (failed > 0) score -= (failed * 10);
                    if (score < 0) score = 0;

                    // 5. Get Upcoming
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
                        LastRunSuccess = success;
                        LastRunFailed = failed;
                        HealthScore = score;

                        RecentHistory.Clear();
                        foreach (var h in historyEntries.OrderByDescending(x => x.Time).Take(10))
                        {
                            RecentHistory.Add(h);
                        }

                        UpcomingTasks.Clear();
                        foreach (var u in upcoming)
                        {
                            UpcomingTasks.Add(u);
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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
