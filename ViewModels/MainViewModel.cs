using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace FluentTaskScheduler.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ITaskService _taskService = App.Container.GetRequiredService<ITaskService>();
        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();
        private List<ScheduledTaskModel> _allTasks = new();
        private bool _isLoading;
        private string _searchText = "";
        private string _currentFolderPath = "\\";
        private FilterMode _filterMode = FilterMode.All;
        private ScheduledTaskModel? _selectedTask;

        public string ActionRunPrefix => Services.LocalizationService.GetString("Trigger.Run", "Run:");

        // Sorting
        public SortColumn SortColumn { get; private set; } = SortColumn.None;
        public bool SortAscending { get; private set; } = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ScheduledTaskModel> FilteredTasks { get; } = new();
        
        // Expose service for direct calls from UI where Command isn't appropriate yet
        public ITaskService TaskService => _taskService;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public ScheduledTaskModel? SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(); }
        }

        public List<string> SavedCategories => Settings.SavedCategories;
        public List<string> SavedTags => Settings.SavedTags;
        public void RefreshSavedCategories() 
        { 
            OnPropertyChanged(nameof(SavedCategories)); 
            OnPropertyChanged(nameof(SavedTags)); 
        }

        public MainViewModel()
        {
            Services.LocalizationService.LanguageChanged += (s, e) => {
                OnPropertyChanged(nameof(ActionRunPrefix));
            };
        }

        public bool IsTrayIconVisible => Settings.EnableTrayIcon;
        public void RefreshTrayIconVisibility() => OnPropertyChanged(nameof(IsTrayIconVisible));

        public async Task LoadTasksAsync()
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                var tasks = await Task.Run(() => _taskService.GetAllTasks());
                _allTasks = tasks ?? new List<ScheduledTaskModel>();
                ApplyFilters();
                Services.TrayIconService.UpdateBadge(_allTasks.Count(t => t.State == TaskState.Running));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tasks: {ex.Message}");
                _allTasks = new List<ScheduledTaskModel>();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void SetFilter(string filterTag)
        {
            _filterMode = filterTag switch
            {
                "all" => FilterMode.All,
                "running" => FilterMode.Running,
                "enabled" => FilterMode.Enabled,
                "disabled" => FilterMode.Disabled,
                _ => FilterMode.Folder
            };

            // If it's a global filter (footer items), reset folder path
            if (_filterMode != FilterMode.Folder)
            {
                _currentFolderPath = "\\";
            }
            else if (!string.IsNullOrEmpty(filterTag) && filterTag != "Add")
            {
                _currentFolderPath = filterTag;
            }
            ApplyFilters();
        }

        /// <summary>Cycles sort: same column toggles Asc/Desc, new column defaults to Asc.</summary>
        public void SortBy(SortColumn column)
        {
            if (SortColumn == column) SortAscending = !SortAscending;
            else { SortColumn = column; SortAscending = true; }
            ApplyFilters();
        }

        /// <summary>Clears any active sort.</summary>
        public void ClearSort()
        {
            SortColumn = SortColumn.None;
            SortAscending = true;
            ApplyFilters();
        }

        public void ApplyFilters()
        {
            if (_allTasks == null) return;

            var query = _allTasks.AsEnumerable();

            // Hidden Visibility Filter
            if (!Settings.ShowHiddenTasks)
            {
                query = query.Where(t => !t.IsHidden);
            }

            // Search Filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(t => 
                    (t.Name != null && t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Category != null && t.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Tags != null && t.Tags.Any(tag => tag.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
                );
            }

            // Tag/Folder Filter
            if (_filterMode == FilterMode.Folder)
            {
                // Folder logic
                query = query.Where(t =>
                {
                    var taskDir = System.IO.Path.GetDirectoryName(t.Path);
                    if (string.IsNullOrEmpty(taskDir)) taskDir = "\\";
                    return taskDir.Equals(_currentFolderPath, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                // Status Logic
                if (_filterMode == FilterMode.Running) query = query.Where(t => t.State == TaskState.Running);
                else if (_filterMode == FilterMode.Enabled) query = query.Where(t => t.IsEnabled);
                else if (_filterMode == FilterMode.Disabled) query = query.Where(t => !t.IsEnabled);
            }

            var results = SortColumn switch
            {
                SortColumn.Name    => SortAscending ? query.OrderBy(t => t.Name)         : query.OrderByDescending(t => t.Name),
                SortColumn.Status  => SortAscending ? query.OrderBy(t => t.State)        : query.OrderByDescending(t => t.State),
                SortColumn.NextRun => SortAscending ? query.OrderBy(t => t.NextRunTime)  : query.OrderByDescending(t => t.NextRunTime),
                SortColumn.LastRun => SortAscending ? query.OrderBy(t => t.LastRunTime)  : query.OrderByDescending(t => t.LastRunTime),
                _                  => query
            };
            UpdateFilteredTasksCollection(results.ToList());
        }

        private void UpdateFilteredTasksCollection(List<ScheduledTaskModel> results)
        {
            // Optimization: Handle initial load or empty state efficiently (O(N))
            if (FilteredTasks.Count == 0)
            {
                foreach (var taskModel in results) FilteredTasks.Add(taskModel);
                return;
            }

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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
