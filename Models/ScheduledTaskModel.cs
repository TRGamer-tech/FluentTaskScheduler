using System;
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
        public string Triggers { get; set; } = "";

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
        public string ActionCommand { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string ScheduleInfo { get; set; } = "";
        public string TriggerType { get; set; } = "Daily";
        
        // Trigger Specifics
        public short DailyInterval { get; set; } = 1;
        public short WeeklyInterval { get; set; } = 1;
        public System.Collections.Generic.List<string> WeeklyDays { get; set; } = new();
        
        // Monthly
        public bool MonthlyIsDayOfWeek { get; set; } // false=Days (1, 15), true=On (First Monday)
        public System.Collections.Generic.List<string> MonthlyMonths { get; set; } = new();
        public System.Collections.Generic.List<int> MonthlyDays { get; set; } = new();
        public string MonthlyWeek { get; set; } = "First"; // First, Second, Third, Fourth, Last
        public string MonthlyDayOfWeek { get; set; } = "Monday"; // Monday..Sunday

        // Expiration
        public DateTime? ExpirationDate { get; set; }
        
        public string RandomDelay { get; set; } = ""; // e.g., "PT1M"


        // Repetition
        public string RepetitionInterval { get; set; } = ""; // e.g., "PT15M" for 15 minutes
        public string RepetitionDuration { get; set; } = ""; // e.g., "PT1H" for 1 hour, empty for indefinitely
        
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
