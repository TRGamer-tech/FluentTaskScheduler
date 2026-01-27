using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentTaskScheduler.Models
{
    public class TaskTriggerModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _triggerType = "Daily";
        public string TriggerType
        {
            get => _triggerType;
            set { if (_triggerType != value) { _triggerType = value; OnPropertyChanged(); OnPropertyChanged(nameof(Descriptor)); } }
        }

        private string _scheduleInfo = "";
        public string ScheduleInfo
        {
            get => _scheduleInfo;
            set { if (_scheduleInfo != value) { _scheduleInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(Descriptor)); } }
        }

        public short DailyInterval { get; set; } = 1;
        public short WeeklyInterval { get; set; } = 1;
        public List<string> WeeklyDays { get; set; } = new();

        public bool MonthlyIsDayOfWeek { get; set; }
        public List<string> MonthlyMonths { get; set; } = new();
        public List<int> MonthlyDays { get; set; } = new();
        public string MonthlyWeek { get; set; } = "First";
        public string MonthlyDayOfWeek { get; set; } = "Monday";

        public DateTime? ExpirationDate { get; set; }
        public string RandomDelay { get; set; } = "";

        public string EventLog { get; set; } = "Application";
        public string EventSource { get; set; } = "";
        public int? EventId { get; set; }

        public string RepetitionInterval { get; set; } = "";
        public string RepetitionDuration { get; set; } = "";

        public string Descriptor
        {
            get
            {
                if (string.IsNullOrEmpty(ScheduleInfo)) return TriggerType;
                return $"{TriggerType} at {ScheduleInfo}";
            }
        }
    }
}
