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
    }
}
