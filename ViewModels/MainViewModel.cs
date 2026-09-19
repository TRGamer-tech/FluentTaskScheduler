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
        private bool _showAllFolders = true;
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
            Services.LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(ActionRunPrefix));
        }

        /// <summary>
        /// Unsubscribes from the static LocalizationService event. Without this, every MainPage
        /// (a new one is created per window) keeps its MainViewModel — and everything it
        /// transitively references — alive forever, even after the window closes (see 3.3).
        /// </summary>
        public void Cleanup()
        {
            Services.LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        public bool IsTrayIconVisible => Settings.EnableTrayIcon;
        public void RefreshTrayIconVisibility() => OnPropertyChanged(nameof(IsTrayIconVisible));

        private string? _loadErrorMessage;
        /// <summary>Set when the last <see cref="LoadTasksAsync"/> failed, so the page can surface
        /// an InfoBar instead of the failure only going to Debug output (see 3.11).</summary>
        public string? LoadErrorMessage
        {
            get => _loadErrorMessage;
            private set { _loadErrorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLoadError)); }
        }
        public bool HasLoadError => !string.IsNullOrEmpty(_loadErrorMessage);

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
                LoadErrorMessage = null;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load scheduled tasks");
                LoadErrorMessage = ex.Message;
                _allTasks = new List<ScheduledTaskModel>();
                ApplyFilters();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void SetFilter(string filterTag)
        {
            // "all" is the sidebar's All Tasks entry; anything else pins the list to one folder.
            _showAllFolders = filterTag == "all";

            // If it's the global filter, reset folder path
            if (_showAllFolders)
            {
                _currentFolderPath = "\\";
            }
            else if (!string.IsNullOrEmpty(filterTag) && filterTag != "Add")
            {
                _currentFolderPath = filterTag;
            }
            ApplyFilters();
        }

        private StatusFilter _statusFilter = StatusFilter.All;

        /// <summary>
        /// Status shown in the toolbar dropdown. This is independent of the folder selection,
        /// so a folder can be narrowed by status.
        /// </summary>
        public StatusFilter StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (_statusFilter == value) return;
                _statusFilter = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        /// <summary>Applies the toolbar status dropdown on top of the folder/search filters.</summary>
        private IEnumerable<ScheduledTaskModel> ApplyStatusFilter(IEnumerable<ScheduledTaskModel> query)
        {
            switch (_statusFilter)
            {
                case StatusFilter.Running:
                    return query.Where(t => t.State == TaskState.Running);
                case StatusFilter.Enabled:
                    return query.Where(t => t.IsEnabled);
                case StatusFilter.Disabled:
                    return query.Where(t => !t.IsEnabled);
                case StatusFilter.Snoozed:
                    // Only tasks this app suspended for the active global snooze — an empty result
                    // simply means nothing is currently suspended.
                    var suspended = new HashSet<string>(
                        Settings.SnoozeDisabledTaskPaths ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    return suspended.Count == 0
                        ? Enumerable.Empty<ScheduledTaskModel>()
                        : query.Where(t => suspended.Contains(t.Path));
                default:
                    return query;
            }
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

            // Folder filter — "all" spans every folder, anything else pins to one folder
            if (!_showAllFolders)
            {
                query = query.Where(t =>
                {
                    var taskDir = System.IO.Path.GetDirectoryName(t.Path);
                    if (string.IsNullOrEmpty(taskDir)) taskDir = "\\";
                    return taskDir.Equals(_currentFolderPath, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Toolbar status dropdown
            query = ApplyStatusFilter(query);

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

            // LoadTasksAsync always builds brand-new ScheduledTaskModel instances, so a
            // reference-based diff below would never find a match and this "preserve scroll
            // position" logic degenerated into remove-everything/insert-everything on every single
            // refresh. Reusing the existing instance (keyed by Path) and updating its fields in
            // place is what actually keeps scroll position, selection, and toggle state stable
            // across a refresh (see 3.4).
            var existingByPath = new Dictionary<string, ScheduledTaskModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in FilteredTasks)
            {
                if (!string.IsNullOrEmpty(t.Path)) existingByPath.TryAdd(t.Path, t);
            }

            var reconciled = new List<ScheduledTaskModel>(results.Count);
            foreach (var fresh in results)
            {
                if (!string.IsNullOrEmpty(fresh.Path) && existingByPath.TryGetValue(fresh.Path, out var existing))
                {
                    existing.UpdateFrom(fresh);
                    reconciled.Add(existing);
                }
                else
                {
                    reconciled.Add(fresh);
                }
            }

            var resultsSet = new HashSet<ScheduledTaskModel>(reconciled);
            var currentSet = new HashSet<ScheduledTaskModel>(FilteredTasks);

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
            for (int i = 0; i < reconciled.Count; i++)
            {
                var taskModel = reconciled[i];
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
