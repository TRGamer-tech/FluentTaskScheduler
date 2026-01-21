namespace FluentTaskScheduler.Models
{
    public class TaskHistoryEntry
    {
        public string Time { get; set; } = "";
        public string Result { get; set; } = "";
        public string ExitCode { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
