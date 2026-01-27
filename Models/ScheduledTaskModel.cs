using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentTaskScheduler.Models
{
    public class ScheduledTaskModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _state = "";
        private bool _isEnabled;

        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        
        public string State 
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

        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public DateTime? LastRunTime { get; set; }
        public DateTime? NextRunTime { get; set; }
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
                    OnPropertyChanged();
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
        public string TriggerType 
        { 
            get => TriggersList.Count > 0 ? TriggersList[0].TriggerType : "Daily"; 
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
        public bool OnlyIfAC { get; set; }
        public bool OnlyIfNetwork { get; set; }
        public bool WakeToRun { get; set; }
        public bool StopOnBattery { get; set; }
        
        // Settings
        public string StopIfRunsLongerThan { get; set; } = "PT72H"; // Default 3 days
        public bool RestartOnFailure { get; set; }
        public string RestartInterval { get; set; } = "PT1M"; // Default 1 minute
        public int RestartCount { get; set; } = 3;
        public bool RunIfMissed { get; set; }
    }
}
