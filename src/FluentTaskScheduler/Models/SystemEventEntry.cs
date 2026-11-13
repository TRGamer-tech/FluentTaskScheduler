namespace FluentTaskScheduler.Models
{
    public class SystemEventEntry
    {
        public string Time { get; set; } = "";
        public int EventId { get; set; }
        public string EventType { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
