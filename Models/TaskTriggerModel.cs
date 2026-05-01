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

        // Idle Trigger
        public string IdleDuration { get; set; } = "PT10M"; // Default 10 minutes

        // Session State Change Trigger (Lock/Unlock/Connect/Disconnect)
        public string SessionStateChangeType { get; set; } = "Lock"; // Lock, Unlock, RemoteConnect, RemoteDisconnect
        

        public string Descriptor
        {
            get
            {
                if (TriggerType == "SessionStateChange") 
                {
                    string sess = Services.LocalizationService.GetString("Dialog.Sess." + SessionStateChangeType, SessionStateChangeType);
                    return string.Format(Services.LocalizationService.GetString("Trigger.SessionStateChange", "At {0}"), sess);
                }
                if (TriggerType == "OnIdle") return Services.LocalizationService.GetString("Trigger.OnIdle", "On Idle");
                if (TriggerType == "AtLogon") return Services.LocalizationService.GetString("Trigger.AtLogon", "At Logon");
                if (TriggerType == "AtStartup") return Services.LocalizationService.GetString("Trigger.AtStartup", "At Startup");
                
                string typeLoc = Services.LocalizationService.GetString("Dialog.Trigger." + TriggerType, TriggerType);
                if (string.IsNullOrEmpty(ScheduleInfo)) return typeLoc;
                
                string at = Services.LocalizationService.GetString("Trigger.At", "at");
                return $"{typeLoc} {at} {ScheduleInfo}";
            }
        }
    }
}
