using System.Collections.Generic;

namespace FluentTaskScheduler.Models
{
    /// <summary>
    /// A ready-to-deploy task preset: command, arguments, recommended trigger and metadata.
    /// Deploying one opens the normal task editor pre-filled, so the user always reviews
    /// (and can change) everything before it is registered with Windows.
    /// </summary>
    public class TaskTemplate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Library group, e.g. "System Maintenance", "Developer Tools", "Power &amp; Utility".</summary>
        public string Group { get; set; } = "";

        /// <summary>Segoe Fluent Icons glyph shown on the card.</summary>
        public string Glyph { get; set; } = "";

        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";

        public bool RunAsAdmin { get; set; }
        public bool WakeToRun { get; set; }
        public bool RunIfMissed { get; set; } = true;
        public bool OnlyIfIdle { get; set; }
        public bool OnlyIfAC { get; set; }

        /// <summary>Task category written into the task metadata.</summary>
        public string TaskCategory { get; set; } = "";
        public List<string> Tags { get; set; } = new();

        // ── Recommended trigger ─────────────────────────────────────────────────
        /// <summary>Trigger type understood by TaskServiceWrapper: Daily, Weekly, AtLogon, AtStartup, OnIdle, Once.</summary>
        public string TriggerType { get; set; } = "Daily";
        public int TriggerHour { get; set; } = 3;
        public int TriggerMinute { get; set; } = 0;
        public short DailyInterval { get; set; } = 1;
        public short WeeklyInterval { get; set; } = 1;
        public List<string> WeeklyDays { get; set; } = new();

        /// <summary>Optional caveat shown on the card (e.g. requires admin, is destructive).</summary>
        public string Note { get; set; } = "";

        public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    }
}
