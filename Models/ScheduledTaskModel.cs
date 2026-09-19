using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Models
{
    public class ScheduledTaskModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Every writable property except identity (Name/Path) and UI-only selection state — used by
        // UpdateFrom to refresh an existing instance in place instead of MainViewModel replacing it
        // wholesale on every list refresh (which defeated the ObservableCollection diff's scroll- and
        // selection-preservation, since LoadTasksAsync always builds brand-new instances — see 3.4).
        private static readonly PropertyInfo[] _updatableProperties = typeof(ScheduledTaskModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => p.Name is not (nameof(Name) or nameof(Path) or nameof(IsSelected) or nameof(State) or nameof(IsEnabled)))
            .ToArray();

        /// <summary>
        /// Copies every mutable field from <paramref name="fresh"/> (a newly-read task) onto this
        /// instance, preserving this instance's identity — and therefore its ObservableCollection
        /// position and IsSelected state — across a list refresh. State and IsEnabled are applied
        /// first, in that order, matching how TaskServiceWrapper.MapTaskToModel initializes a new
        /// model (IsEnabled's setter derives State when the task isn't Running).
        /// </summary>
        public void UpdateFrom(ScheduledTaskModel fresh)
        {
            State = fresh.State;
            IsEnabled = fresh.IsEnabled;
            foreach (var prop in _updatableProperties)
            {
                var newValue = prop.GetValue(fresh);
                var oldValue = prop.GetValue(this);
                if (!Equals(newValue, oldValue))
                    prop.SetValue(this, newValue);
            }
        }

        private Enums.TaskState _state = Enums.TaskState.Unknown;
        private bool _isEnabled;
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name { get; set; } = "";
        public string Path { get; set; } = "";

        /// <summary>
        /// Set when the underlying task contains actions or triggers this app cannot faithfully
        /// round-trip (e.g. email/COM/show-message actions, RegistrationTrigger, custom XML
        /// triggers). Editing and saving such a task through the model would silently drop or
        /// corrupt those elements, so the editor refuses to open it — see <see cref="UnsupportedElementsDescription"/>.
        /// </summary>
        public bool HasUnsupportedElements { get; set; }
        public string UnsupportedElementsDescription { get; set; } = "";
        
        public Enums.TaskState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isRunning;
        /// <summary>True while this task was manually started and has not yet finished.
        /// Drives the animated ProgressRing independently of the volatile Task Scheduler state string.</summary>
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description { get; set; } = "";
        public string Author { get; set; } = "";

        private string _category = "";
        public string Category
        {
            get => _category;
            set { if (_category != value) { _category = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<string> Tags { get; set; } = new();

        public string TagsDisplay => Tags.Count > 0 ? string.Join(", ", Tags) : "";

        private DateTime? _lastRunTime;
        public DateTime? LastRunTime
        {
            get => _lastRunTime;
            set { if (_lastRunTime != value) { _lastRunTime = value; OnPropertyChanged(); } }
        }

        private DateTime? _nextRunTime;
        public DateTime? NextRunTime
        {
            get => _nextRunTime;
            set { if (_nextRunTime != value) { _nextRunTime = value; OnPropertyChanged(); } }
        }
        public int LastTaskResult { get; set; }
        public string Triggers { get; set; } = ""; // Label for display

        public ObservableCollection<TaskTriggerModel> TriggersList { get; set; } = new();

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    if (_state != Enums.TaskState.Running)
                        State = value ? Enums.TaskState.Ready : Enums.TaskState.Disabled;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(State));
                }
            }
        }
        public System.Collections.ObjectModel.ObservableCollection<TaskActionModel> Actions { get; set; } = new();

        // Legacy properties for binding compatibility (synced with first action)
        public string ActionCommand 
        { 
            get => Actions.Count > 0 ? Actions[0].Command : ""; 
            set 
            {
                if (Actions.Count == 0) Actions.Add(new TaskActionModel());
                Actions[0].Command = value;
                OnPropertyChanged();
            }
        }
        public string Arguments 
        { 
            get => Actions.Count > 0 ? Actions[0].Arguments : ""; 
            set 
            {
                if (Actions.Count == 0) Actions.Add(new TaskActionModel());
                Actions[0].Arguments = value;
                OnPropertyChanged();
            }
        }
        public string WorkingDirectory 
        { 
            get => Actions.Count > 0 ? Actions[0].WorkingDirectory : ""; 
            set 
            {
                if (Actions.Count == 0) Actions.Add(new TaskActionModel());
                Actions[0].WorkingDirectory = value;
                OnPropertyChanged();
            }
        }
        
        public string ScheduleInfo 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].ScheduleInfo : ""; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].ScheduleInfo = value; OnPropertyChanged(); }
        }
        public Enums.TriggerType TriggerType
        {
            get => TriggersList.Count > 0 ? TriggersList[0].TriggerType : Enums.TriggerType.Daily;
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].TriggerType = value; OnPropertyChanged(); }
        }
        public bool RunWithHighestPrivileges { get; set; } = false;
        
        // Trigger Specifics Proxy
        public short DailyInterval 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].DailyInterval : (short)1; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].DailyInterval = value; OnPropertyChanged(); }
        }
        public short WeeklyInterval 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].WeeklyInterval : (short)1; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].WeeklyInterval = value; OnPropertyChanged(); }
        }
        public List<string> WeeklyDays 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].WeeklyDays : new List<string>(); 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].WeeklyDays = value; OnPropertyChanged(); }
        }
        
        // Monthly Proxy
        public bool MonthlyIsDayOfWeek 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].MonthlyIsDayOfWeek : false; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].MonthlyIsDayOfWeek = value; OnPropertyChanged(); }
        }
        public List<string> MonthlyMonths 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].MonthlyMonths : new List<string>(); 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].MonthlyMonths = value; OnPropertyChanged(); }
        }
        public List<int> MonthlyDays 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].MonthlyDays : new List<int>(); 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].MonthlyDays = value; OnPropertyChanged(); }
        }
        public string MonthlyWeek 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].MonthlyWeek : "First"; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].MonthlyWeek = value; OnPropertyChanged(); }
        }
        public string MonthlyDayOfWeek 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].MonthlyDayOfWeek : "Monday"; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].MonthlyDayOfWeek = value; OnPropertyChanged(); }
        }

        // Expiration Proxy
        public DateTime? ExpirationDate 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].ExpirationDate : null; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].ExpirationDate = value; OnPropertyChanged(); }
        }
        
        public string RandomDelay 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].RandomDelay : ""; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].RandomDelay = value; OnPropertyChanged(); }
        }
        
        // Event Log Trigger Proxy
        public string EventLog 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].EventLog : "Application"; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].EventLog = value; OnPropertyChanged(); }
        }
        public string EventSource 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].EventSource : ""; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].EventSource = value; OnPropertyChanged(); }
        }
        public int? EventId 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].EventId : null; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].EventId = value; OnPropertyChanged(); }
        }

        // Repetition Proxy
        public string RepetitionInterval 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].RepetitionInterval : ""; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].RepetitionInterval = value; OnPropertyChanged(); }
        }
        public string RepetitionDuration 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].RepetitionDuration : ""; 
            set { if (TriggersList.Count == 0) TriggersList.Add(new TaskTriggerModel()); TriggersList[0].RepetitionDuration = value; OnPropertyChanged(); }
        }
        
        // Conditions
        public bool OnlyIfIdle { get; set; }
        public string IdleDuration { get; set; } = "PT10M"; // Default 10 minutes
        public bool StopOnIdleEnd { get; set; }
        public bool OnlyIfAC { get; set; }
        public bool DisallowStartOnBatteries { get; set; }
        public bool OnlyIfNetwork { get; set; }
        public string NetworkId { get; set; } = ""; // GUID of specific network
        public string NetworkName { get; set; } = ""; // Name for display
        public bool WakeToRun { get; set; }
        public bool StopOnBattery { get; set; }
        
        // Settings
        public string StopIfRunsLongerThan { get; set; } = "PT72H"; // Default 3 days
        public bool RestartOnFailure { get; set; }
        public string RestartInterval { get; set; } = "PT1M"; // Default 1 minute
        public int RestartCount { get; set; } = 3;
        public bool RunIfMissed { get; set; }
        public TaskInstancesPolicy MultipleInstancesPolicy { get; set; } = TaskInstancesPolicy.IgnoreNew;
        public int TaskPriority { get; set; } = 7; // 0=Realtime to 10=Idle, 7=Normal
        public bool DeleteExpiredTaskAfter { get; set; }
        public bool AllowHardTerminate { get; set; } = true;
        
        // User Context
        public string RunAsUser { get; set; } = ""; // Empty = current user
        public bool RunAsSystem { get; set; } = false;

        private bool _isHidden;
        public bool IsHidden
        {
            get => _isHidden;
            set { if (_isHidden != value) { _isHidden = value; OnPropertyChanged(); } }
        }

        private bool _isFromEventLog;
        public bool IsFromEventLog
        {
            get => _isFromEventLog;
            set { if (_isFromEventLog != value) { _isFromEventLog = value; OnPropertyChanged(); } }
        }

        private bool _isReadOnlyFallback;
        public bool IsReadOnlyFallback
        {
            get => _isReadOnlyFallback;
            set { if (_isReadOnlyFallback != value) { _isReadOnlyFallback = value; OnPropertyChanged(); } }
        }

        private TaskPipeline _pipeline = new();
        /// <summary>Completion actions (task chaining) configured for this task.</summary>
        public TaskPipeline Pipeline
        {
            get => _pipeline;
            set
            {
                _pipeline = value ?? new TaskPipeline();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPipeline));
                OnPropertyChanged(nameof(PipelineSummary));
            }
        }

        public bool HasPipeline => _pipeline.IsActive;

        /// <summary>Short "2 on success · 1 on failure" style label for list rows and the edit dialog.</summary>
        public string PipelineSummary => _pipeline.HasAnyTargets
            ? $"{_pipeline.OnSuccessTasks.Count} / {_pipeline.OnFailureTasks.Count}"
            : "";
    }
}
