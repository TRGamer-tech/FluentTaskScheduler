using System;

namespace FluentTaskScheduler.Models
{
    public class TaskHistoryEntry
    {
        public string Time { get; set; } = "";
        public string Result { get; set; } = "";
        public string ExitCode { get; set; } = "";
        public string Message { get; set; } = "";
        public int EventId { get; set; }
        public string User { get; set; } = "";
        public Guid? ActivityId { get; set; }
        public string TaskPath { get; set; } = "";
        public string TaskName { get; set; } = "";

        // Detailed fields
        public string Level { get; set; } = "";
        public string Keywords { get; set; } = "";
        public string Computer { get; set; } = "";
        public string TaskCategory { get; set; } = "";
        public string OpCode { get; set; } = "";
    }
}
